using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Application.Signals;
using N225BrokerBridge.Application.Sync;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace N225BrokerBridge.Application.Orders;

/// <summary>
/// 新規注文発注ユースケース。
///
/// 1. NewOrderIntent から Order 集約を生成
/// 2. ブローカーアダプタ経由で PlaceOrder
/// 3. 応答 (Accepted/Rejected/NetworkError) に応じて Order の状態を更新
/// 4. リポジトリに保存
///
/// 失敗時のリトライ・キュー化は呼び出し側 (SignalHandler) の責務。本ユースケースは
/// 1 シグナル = 1 試行に閉じる。
/// </summary>
public sealed class PlaceNewOrderUseCase
{
    private readonly IBrokerAdapter _broker;
    private readonly IOrderRepository _orderRepo;
    private readonly IOrderMetadataStore _orderMetaStore;
    private readonly IPendingOrderTracker _pendingTracker;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<PlaceNewOrderUseCase> _logger;

    public PlaceNewOrderUseCase(
        IBrokerAdapter broker,
        IOrderRepository orderRepo,
        IOrderMetadataStore orderMetaStore,
        IPendingOrderTracker pendingTracker,
        IDateTimeProvider clock,
        ILogger<PlaceNewOrderUseCase> logger)
    {
        _broker = broker;
        _orderRepo = orderRepo;
        _orderMetaStore = orderMetaStore;
        _pendingTracker = pendingTracker;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PlaceNewOrderResult> ExecuteAsync(NewOrderIntent intent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        // 1. Order 集約生成
        var order = new Order(
            id: Guid.NewGuid(),
            brokerCode: _broker.BrokerCode,
            strategy: intent.Strategy,
            interval: intent.Interval,
            tradeMode: intent.TradeMode,
            symbol: intent.Symbol,
            side: intent.Side,
            tradeType: TradeType.NewOrder,
            orderType: intent.OrderType,
            timeInForce: intent.TimeInForce,
            requestedQuantity: intent.Quantity,
            limitPrice: intent.OrderPrice,
            stopPrice: intent.StopPrice ?? Price.Zero,
            targetExecutionId: null,
            createdAtUtc: _clock.UtcNow);

        await _orderRepo.AddAsync(order, ct);

        // 2. ブローカーへ送信
        var request = new OrderRequest(
            CorrelationId: order.Id,
            Strategy: order.Strategy,
            Interval: order.Interval,
            TradeMode: order.TradeMode,
            Symbol: order.Symbol,
            Side: order.Side,
            OrderType: order.OrderType,
            TimeInForce: order.TimeInForce,
            Quantity: order.RequestedQuantity,
            LimitPrice: order.LimitPrice,
            StopPrice: order.StopPrice);

        _logger.LogInformation(
            "PlaceNewOrder: corr={CorrelationId} broker={Broker} strategy={Strategy} side={Side} qty={Qty}",
            order.Id, order.BrokerCode, order.Strategy, order.Side, order.RequestedQuantity);

        OrderResult result;
        try
        {
            result = await _broker.PlaceOrderAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "PlaceNewOrder failed corr={CorrelationId} broker={Broker}",
                order.Id, order.BrokerCode);
            // 通信例外: NetworkError として扱う (再照会で状態確認が必要)
            order.MarkTerminated(OrderState.Rejected, $"Exception: {ex.GetType().Name}", _clock.UtcNow);
            await _orderRepo.UpdateAsync(order, ct);
            return new PlaceNewOrderResult(order, OrderResultStatus.NetworkError, ex.Message);
        }

        // 3. 応答処理
        _logger.LogInformation(
            "PlaceNewOrder 応答: corr={Corr} status={Status} brokerOrderId={OrderId} errorMessage={Err}",
            order.Id, result.Status, result.BrokerOrderId?.Value ?? "(none)", result.ErrorMessage ?? "(none)");

        switch (result.Status)
        {
            case OrderResultStatus.Accepted:
                if (result.BrokerOrderId is null)
                    throw new InvalidOperationException("Accepted result must have BrokerOrderId.");
                order.MarkSubmitted(result.BrokerOrderId, result.ReceivedAt);
                // 約定検知用に Tracker へ追加 (旧 OrderInquiryList.Add 相当)
                _pendingTracker.Track(result.BrokerOrderId.Value);
                // 注文メタを永続化 (起動時 /orders 突合で TradeMode/Strategy/Interval を復元するため)
                await _orderMetaStore.UpsertAsync(new OrderMetadata
                {
                    BrokerOrderId = result.BrokerOrderId.Value,
                    BrokerCode = order.BrokerCode.Value,
                    Strategy = order.Strategy.Value,
                    Interval = order.Interval,
                    TradeMode = order.TradeMode.ToString(),
                    SymbolCode = order.Symbol.Value,
                    Side = order.Side.ToString(),
                    TradeType = order.TradeType.ToString(),
                    TargetExecutionId = null,
                    CreatedAt = order.CreatedAt
                }, ct);
                break;
            case OrderResultStatus.Rejected:
                order.MarkTerminated(OrderState.Rejected, result.ErrorMessage, result.ReceivedAt);
                break;
            case OrderResultStatus.NetworkError:
                // 発注済みか不明。当面は Rejected 扱いだが将来は再照会タスクを起動
                order.MarkTerminated(OrderState.Rejected, "NetworkError: " + result.ErrorMessage, result.ReceivedAt);
                break;
        }

        // 4. 永続化
        await _orderRepo.UpdateAsync(order, ct);

        return new PlaceNewOrderResult(order, result.Status, result.ErrorMessage);
    }
}

public sealed record PlaceNewOrderResult(Order Order, OrderResultStatus Status, string? ErrorMessage);
