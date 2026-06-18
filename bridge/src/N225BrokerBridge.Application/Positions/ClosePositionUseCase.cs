using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Application.Signals;
using N225BrokerBridge.Application.Sync;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.Positions;
using N225BrokerBridge.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace N225BrokerBridge.Application.Positions;

/// <summary>
/// 部分返済対応の建玉返済ユースケース。
///
/// 現 N225OrderBridge のバグ修正版ロジック (TradeViewModel.cs:951-984) を、
/// DDD 4 層構造で正しく組み直したもの。
///
/// フロー:
///   1. ExitOrderIntent を受け取る
///   2. リポジトリから (BrokerCode, Strategy, Interval, TradeMode, OriginalSide) で
///      候補建玉群を取得
///   3. PositionMatcher.BuildPlan() で消化計画を作成 (跨ぎ消化対応)
///   4. 各 Allocation に対して:
///      a. Position.ReserveForClose(qty) で拘束 (HoldQty += qty)
///      b. Order 集約を作成 (TradeType.ExitOrder)
///      c. ブローカーへ ClosePosition 発注
///      d. リポジトリに保存
///   5. Shortfall があれば警告ログ + WriteMessage 相当を返却
/// </summary>
public sealed class ClosePositionUseCase
{
    private readonly IBrokerAdapter _broker;
    private readonly IOrderRepository _orderRepo;
    private readonly IPositionRepository _positionRepo;
    private readonly IOrderMetadataStore _orderMetaStore;
    private readonly IPendingOrderTracker _pendingTracker;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ClosePositionUseCase> _logger;

    public ClosePositionUseCase(
        IBrokerAdapter broker,
        IOrderRepository orderRepo,
        IPositionRepository positionRepo,
        IOrderMetadataStore orderMetaStore,
        IPendingOrderTracker pendingTracker,
        IDateTimeProvider clock,
        ILogger<ClosePositionUseCase> logger)
    {
        _broker = broker;
        _orderRepo = orderRepo;
        _positionRepo = positionRepo;
        _orderMetaStore = orderMetaStore;
        _pendingTracker = pendingTracker;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ClosePositionResult> ExecuteAsync(ExitOrderIntent intent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        // 1. 候補建玉群を取得
        var candidates = await _positionRepo.FindMatchingForCloseAsync(
            _broker.BrokerCode, intent.Strategy, intent.Interval, intent.TradeMode, intent.OriginalSide, ct);

        if (candidates.Count == 0)
        {
            _logger.LogInformation(
                "ClosePosition: 返済対象建玉なし strategy={Strategy} interval={Interval} side={Side}",
                intent.Strategy, intent.Interval, intent.OriginalSide);
            return ClosePositionResult.NoMatchingPositions(intent);
        }

        // 2. 消化計画
        var plan = PositionMatcher.BuildPlan(candidates, intent.Quantity);

        _logger.LogInformation(
            "ClosePosition: 候補={Count}件 要求={Requested} 計画={Plan}件 shortfall={Shortfall} strategy={Strategy}",
            candidates.Count, plan.Requested, plan.Allocations.Count, plan.Shortfall, intent.Strategy);

        var exitOrders = new List<Order>();

        // 3. 各 Allocation に対して発注
        foreach (var allocation in plan.Allocations)
        {
            var position = allocation.Position;
            var qty = allocation.Quantity;

            // 3a. 建玉を拘束
            position.ReserveForClose(qty);
            await _positionRepo.UpdateAsync(position, ct);

            // 3b. 返済 Order 集約を生成
            var exitOrder = new Order(
                id: Guid.NewGuid(),
                brokerCode: _broker.BrokerCode,
                strategy: intent.Strategy,
                interval: intent.Interval,
                tradeMode: intent.TradeMode,
                symbol: intent.Symbol,
                side: intent.OriginalSide.Opposite(),  // 返済は反対サイド
                tradeType: TradeType.ExitOrder,
                orderType: intent.OrderPrice.IsZero ? OrderType.BestMarket : OrderType.Limit,
                timeInForce: TimeInForce.FAS,
                requestedQuantity: qty,
                limitPrice: intent.OrderPrice,
                stopPrice: Price.Zero,
                targetExecutionId: position.Id,
                createdAtUtc: _clock.UtcNow);

            await _orderRepo.AddAsync(exitOrder, ct);

            // 3c. ブローカーへ発注
            var request = new ClosePositionRequest(
                CorrelationId: exitOrder.Id,
                Strategy: intent.Strategy,
                Interval: intent.Interval,
                TradeMode: intent.TradeMode,
                Symbol: intent.Symbol,
                OriginalSide: intent.OriginalSide,
                TargetExecutionId: position.Id,
                Quantity: qty,
                OrderType: exitOrder.OrderType,
                TimeInForce: exitOrder.TimeInForce,
                LimitPrice: exitOrder.LimitPrice,
                StopPrice: exitOrder.StopPrice);

            _logger.LogInformation(
                "ClosePosition: 発注 corr={Corr} target={Target} qty={Qty} (leave={Leave}, remaining_before={Remaining})",
                exitOrder.Id, position.Id, qty, position.LeaveQuantity, plan.Requested);

            OrderResult result;
            try
            {
                result = await _broker.ClosePositionAsync(request, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ClosePosition 発注失敗 corr={Corr} target={Target}", exitOrder.Id, position.Id);
                // 例外: 拘束を解放して終了
                position.ReleaseReservation(qty);
                await _positionRepo.UpdateAsync(position, ct);
                exitOrder.MarkTerminated(OrderState.Rejected, $"Exception: {ex.GetType().Name}", _clock.UtcNow);
                await _orderRepo.UpdateAsync(exitOrder, ct);
                exitOrders.Add(exitOrder);
                continue;
            }

            // 3d. 応答処理
            switch (result.Status)
            {
                case OrderResultStatus.Accepted:
                    if (result.BrokerOrderId is null)
                        throw new InvalidOperationException("Accepted result must have BrokerOrderId.");
                    exitOrder.MarkSubmitted(result.BrokerOrderId, result.ReceivedAt);
                    _pendingTracker.Track(result.BrokerOrderId.Value);
                    // 返済注文のメタを永続化
                    await _orderMetaStore.UpsertAsync(new OrderMetadata
                    {
                        BrokerOrderId = result.BrokerOrderId.Value,
                        BrokerCode = exitOrder.BrokerCode.Value,
                        Strategy = exitOrder.Strategy.Value,
                        Interval = exitOrder.Interval,
                        TradeMode = exitOrder.TradeMode.ToString(),
                        SymbolCode = exitOrder.Symbol.Value,
                        Side = exitOrder.Side.ToString(),
                        TradeType = exitOrder.TradeType.ToString(),
                        TargetExecutionId = position.Id.Value,
                        CreatedAt = exitOrder.CreatedAt
                    }, ct);
                    break;
                case OrderResultStatus.Rejected:
                    // 拘束を解放
                    position.ReleaseReservation(qty);
                    await _positionRepo.UpdateAsync(position, ct);
                    exitOrder.MarkTerminated(OrderState.Rejected, result.ErrorMessage, result.ReceivedAt);
                    break;
                case OrderResultStatus.NetworkError:
                    // 拘束は保持 (発注が通った可能性あり、後で照会して整合性確保が必要)
                    exitOrder.MarkTerminated(OrderState.Rejected, "NetworkError: " + result.ErrorMessage, result.ReceivedAt);
                    break;
            }

            await _orderRepo.UpdateAsync(exitOrder, ct);
            exitOrders.Add(exitOrder);
        }

        if (plan.Shortfall.IsPositive)
        {
            _logger.LogWarning(
                "ClosePosition: 要求枚数を満たせず shortfall={Shortfall} strategy={Strategy} side={Side} (残合計が要求未満)",
                plan.Shortfall, intent.Strategy, intent.OriginalSide);
        }

        return new ClosePositionResult(intent, plan, exitOrders, plan.Shortfall);
    }
}

public sealed record ClosePositionResult(
    ExitOrderIntent Intent,
    ClosurePlan? Plan,
    IReadOnlyList<Order> ExitOrders,
    Quantity Shortfall)
{
    public bool HasNoMatchingPositions => Plan is null;
    public bool IsCompletelyClosed => Plan is not null && Plan.IsComplete && ExitOrders.All(o => !o.IsTerminal);

    public static ClosePositionResult NoMatchingPositions(ExitOrderIntent intent)
        => new(intent, Plan: null, ExitOrders: Array.Empty<Order>(), Shortfall: Quantity.Zero);
}
