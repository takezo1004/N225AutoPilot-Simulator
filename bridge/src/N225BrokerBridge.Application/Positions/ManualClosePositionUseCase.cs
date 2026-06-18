using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Application.Sync;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.Positions;
using N225BrokerBridge.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace N225BrokerBridge.Application.Positions;

/// <summary>
/// 手動返済ユースケース (旧 N225OrderBridge.TradeView.ExitOrderButton_Click 相当)。
///
/// シグナル駆動の <see cref="ClosePositionUseCase"/> が
/// (Strategy, Interval, Side) で建玉群を絞り込んで PositionMatcher 経由で返済するのに対し、
/// 本ユースケースは UI で **直接選択された建玉** を ExecutionId で特定して返済する。
///
/// フロー:
///   1. ExecutionId で Position を取得
///   2. 指定枚数 (デフォルトは LeaveQty 全量) で ReserveForClose
///   3. Order 集約を生成 (TradeType.ExitOrder)
///   4. ブローカーへ ClosePosition 発注
///   5. 応答に応じて Order 状態更新 + 失敗時は ReleaseReservation
/// </summary>
public sealed class ManualClosePositionUseCase
{
    private readonly IBrokerAdapter _broker;
    private readonly IOrderRepository _orderRepo;
    private readonly IPositionRepository _positionRepo;
    private readonly IOrderMetadataStore _orderMetaStore;
    private readonly IPendingOrderTracker _pendingTracker;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ManualClosePositionUseCase> _logger;

    /// <summary>
    /// 手動返済ユースケースを生成する。
    /// </summary>
    /// <param name="broker">発注先ブローカーアダプタ。</param>
    /// <param name="orderRepo">Order 集約の永続化リポジトリ。</param>
    /// <param name="positionRepo">Position 集約の永続化リポジトリ。</param>
    /// <param name="orderMetaStore">注文メタデータ (戦略・足等) の保存先。</param>
    /// <param name="pendingTracker">ポーリング監視に追加する追跡器。</param>
    /// <param name="clock">UTC 時刻プロバイダ (テスト差し替え用)。</param>
    /// <param name="logger">ログ出力。</param>
    public ManualClosePositionUseCase(
        IBrokerAdapter broker,
        IOrderRepository orderRepo,
        IPositionRepository positionRepo,
        IOrderMetadataStore orderMetaStore,
        IPendingOrderTracker pendingTracker,
        IDateTimeProvider clock,
        ILogger<ManualClosePositionUseCase> logger)
    {
        _broker = broker;
        _orderRepo = orderRepo;
        _positionRepo = positionRepo;
        _orderMetaStore = orderMetaStore;
        _pendingTracker = pendingTracker;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// 指定された建玉を返済発注する。
    /// 内部で建玉サイドの反対を返済 Side に決定し、ブローカーへ送信する。
    /// </summary>
    /// <param name="targetExecutionId">返済対象の建玉 ID。</param>
    /// <param name="quantity">返済枚数 (null なら <c>Position.AvailableForClose</c> 全量)。</param>
    /// <param name="orderType">注文タイプ (デフォルト BestMarket)。</param>
    /// <param name="limitPrice">指値価格 (Limit / 対当時)。</param>
    /// <param name="stopPrice">逆指値価格 (Stop 時)。</param>
    /// <param name="timeInForce">有効期間条件 (デフォルト FAS)。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>
    /// 返済結果。建玉が見つからない・返済可能枚数なし・要求が available 超過・ブローカー例外の各失敗ケースは
    /// <see cref="ManualCloseResult"/> の static ファクトリで包んで返す (例外は投げない)。
    /// </returns>
    public async Task<ManualCloseResult> ExecuteAsync(
        ExecutionId targetExecutionId,
        Quantity? quantity = null,
        OrderType orderType = OrderType.BestMarket,
        Price? limitPrice = null,
        Price? stopPrice = null,
        TimeInForce timeInForce = TimeInForce.FAS,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(targetExecutionId);

        var position = await _positionRepo.FindByIdAsync(targetExecutionId, ct);
        if (position is null)
        {
            _logger.LogWarning("ManualClose: 建玉が見つかりません ExecutionId={Id}", targetExecutionId);
            return ManualCloseResult.PositionNotFound(targetExecutionId);
        }

        var qty = quantity ?? position.AvailableForClose;
        if (!qty.IsPositive)
        {
            _logger.LogWarning("ManualClose: 返済可能枚数なし ExecutionId={Id}", targetExecutionId);
            return ManualCloseResult.NoAvailableQuantity(position);
        }
        if (qty > position.AvailableForClose)
        {
            _logger.LogWarning(
                "ManualClose: 要求枚数 {Req} が available {Avail} を超過 ExecutionId={Id}",
                qty, position.AvailableForClose, targetExecutionId);
            return ManualCloseResult.QuantityExceedsAvailable(position, qty);
        }

        // 1. 拘束
        position.ReserveForClose(qty);
        await _positionRepo.UpdateAsync(position, ct);

        // 2. Order 集約生成
        var exitOrder = new Order(
            id: Guid.NewGuid(),
            brokerCode: _broker.BrokerCode,
            strategy: position.Strategy,
            interval: position.Interval,
            tradeMode: TradeMode.Manual,    // 手動操作なので Manual 固定
            symbol: position.Symbol,
            side: position.Side.Opposite(),
            tradeType: TradeType.ExitOrder,
            orderType: orderType,
            timeInForce: timeInForce,
            requestedQuantity: qty,
            limitPrice: limitPrice ?? Price.Zero,
            stopPrice: stopPrice ?? Price.Zero,
            targetExecutionId: position.Id,
            createdAtUtc: _clock.UtcNow);

        await _orderRepo.AddAsync(exitOrder, ct);

        // 3. ブローカー発注
        var request = new ClosePositionRequest(
            CorrelationId: exitOrder.Id,
            Strategy: position.Strategy,
            Interval: position.Interval,
            TradeMode: TradeMode.Manual,
            Symbol: position.Symbol,
            OriginalSide: position.Side,
            TargetExecutionId: position.Id,
            Quantity: qty,
            OrderType: orderType,
            TimeInForce: timeInForce,
            LimitPrice: limitPrice ?? Price.Zero,
            StopPrice: stopPrice ?? Price.Zero);

        _logger.LogInformation(
            "ManualClose: 発注 corr={Corr} target={Target} qty={Qty} side={Side}→{ExitSide}",
            exitOrder.Id, position.Id, qty, position.Side, position.Side.Opposite());

        OrderResult result;
        try
        {
            result = await _broker.ClosePositionAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ManualClose: 発注失敗 (例外) corr={Corr} target={Target}",
                exitOrder.Id, position.Id);
            position.ReleaseReservation(qty);
            await _positionRepo.UpdateAsync(position, ct);
            exitOrder.MarkTerminated(OrderState.Rejected, $"Exception: {ex.GetType().Name}", _clock.UtcNow);
            await _orderRepo.UpdateAsync(exitOrder, ct);
            return ManualCloseResult.BrokerException(position, exitOrder, ex);
        }

        switch (result.Status)
        {
            case OrderResultStatus.Accepted:
                if (result.BrokerOrderId is null)
                    throw new InvalidOperationException("Accepted result must have BrokerOrderId.");
                exitOrder.MarkSubmitted(result.BrokerOrderId, result.ReceivedAt);
                _pendingTracker.Track(result.BrokerOrderId.Value);
                await _orderMetaStore.UpsertAsync(new OrderMetadata
                {
                    BrokerOrderId = result.BrokerOrderId.Value,
                    BrokerCode = exitOrder.BrokerCode.Value,
                    Strategy = exitOrder.Strategy.Value,
                    Interval = exitOrder.Interval,
                    TradeMode = TradeMode.Manual.ToString(),
                    SymbolCode = exitOrder.Symbol.Value,
                    Side = exitOrder.Side.ToString(),
                    TradeType = exitOrder.TradeType.ToString(),
                    TargetExecutionId = position.Id.Value,
                    CreatedAt = exitOrder.CreatedAt
                }, ct);
                break;
            case OrderResultStatus.Rejected:
                position.ReleaseReservation(qty);
                await _positionRepo.UpdateAsync(position, ct);
                exitOrder.MarkTerminated(OrderState.Rejected, result.ErrorMessage, result.ReceivedAt);
                break;
            case OrderResultStatus.NetworkError:
                exitOrder.MarkTerminated(OrderState.Rejected, "NetworkError: " + result.ErrorMessage, result.ReceivedAt);
                break;
        }
        await _orderRepo.UpdateAsync(exitOrder, ct);

        return new ManualCloseResult(position, exitOrder, result.Status, result.ErrorMessage);
    }
}

/// <summary>
/// <see cref="ManualClosePositionUseCase.ExecuteAsync"/> の結果。
/// </summary>
/// <param name="Position">対象建玉 (見つからない時は null)。</param>
/// <param name="ExitOrder">生成された返済 Order 集約 (発注前に失敗した場合は null)。</param>
/// <param name="Status">受付結果 (Accepted / Rejected / NetworkError)。</param>
/// <param name="ErrorMessage">失敗理由 (kabu の Message やドメイン側のメッセージ)。</param>
public sealed record ManualCloseResult(
    Position? Position,
    Order? ExitOrder,
    OrderResultStatus Status,
    string? ErrorMessage)
{
    /// <summary>建玉が見つからない場合の結果。</summary>
    public static ManualCloseResult PositionNotFound(ExecutionId id) =>
        new(null, null, OrderResultStatus.Rejected, $"Position {id} not found");

    /// <summary>返済可能枚数が 0 の場合の結果。</summary>
    public static ManualCloseResult NoAvailableQuantity(Position pos) =>
        new(pos, null, OrderResultStatus.Rejected, "AvailableForClose is zero");

    /// <summary>要求枚数が <see cref="Position.AvailableForClose"/> を超過した場合の結果。</summary>
    public static ManualCloseResult QuantityExceedsAvailable(Position pos, Quantity qty) =>
        new(pos, null, OrderResultStatus.Rejected, $"Quantity {qty} exceeds available {pos.AvailableForClose}");

    /// <summary>ブローカー呼び出し中に例外が発生した場合の結果。Reservation は解放済み。</summary>
    public static ManualCloseResult BrokerException(Position pos, Order order, Exception ex) =>
        new(pos, order, OrderResultStatus.NetworkError, ex.Message);
}
