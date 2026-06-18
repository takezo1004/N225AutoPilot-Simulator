using Microsoft.Extensions.Logging.Abstractions;
using N225BrokerBridge.Application.Orders;
using N225BrokerBridge.Application.Signals;
using N225BrokerBridge.Application.Tests.TestDoubles;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.ValueObjects;
using N225BrokerBridge.Infrastructure.Persistence;
using Xunit;

namespace N225BrokerBridge.Application.Tests.Orders;

public class PlaceNewOrderUseCaseTests
{
    private readonly FakeBrokerAdapter _broker = new();
    private readonly InMemoryOrderRepository _orderRepo = new();
    private readonly FixedDateTimeProvider _clock = new();
    private readonly StubOrderMetadataStore _orderMetaStore = new();
    private readonly StubPendingOrderTracker _pendingTracker = new();

    private PlaceNewOrderUseCase NewUseCase()
        => new(_broker, _orderRepo, _orderMetaStore, _pendingTracker, _clock, NullLogger<PlaceNewOrderUseCase>.Instance);

    private static NewOrderIntent SampleIntent(int qty = 3, Side side = Side.Buy)
        => new(
            Strategy: new StrategyName("V7-7-fixed"),
            Interval: 5,
            TradeMode: TradeMode.Auto,
            Symbol: new SymbolCode("OSE:NK225M1!"),
            Side: side,
            Quantity: new Quantity(qty),
            OrderPrice: Price.Zero);

    [Fact]
    public async Task Accepted_OrderIsSubmitted_AndSaved()
    {
        var uc = NewUseCase();
        var result = await uc.ExecuteAsync(SampleIntent());

        Assert.Equal(OrderResultStatus.Accepted, result.Status);
        Assert.Equal(OrderState.Submitted, result.Order.State);
        Assert.NotNull(result.Order.BrokerOrderId);
        Assert.Equal(new Quantity(3), result.Order.RequestedQuantity);

        // リポジトリにも保存されている
        var stored = await _orderRepo.FindByIdAsync(result.Order.Id);
        Assert.Equal(result.Order, stored);

        // ブローカー呼び出しが 1 回
        Assert.Single(_broker.PlaceOrderCalls);
        var req = _broker.PlaceOrderCalls[0];
        Assert.Equal(new Quantity(3), req.Quantity);
        Assert.Equal(Side.Buy, req.Side);
    }

    [Fact]
    public async Task Rejected_OrderIsMarkedRejected()
    {
        _broker.PlaceOrderResponder = req => new OrderResult(
            req.CorrelationId, OrderResultStatus.Rejected, null,
            "E001", "Margin insufficient", DateTime.UtcNow);

        var uc = NewUseCase();
        var result = await uc.ExecuteAsync(SampleIntent());

        Assert.Equal(OrderResultStatus.Rejected, result.Status);
        Assert.Equal(OrderState.Rejected, result.Order.State);
        Assert.True(result.Order.IsTerminal);
        Assert.Null(result.Order.BrokerOrderId);
    }

    [Fact]
    public async Task BrokerException_OrderIsMarkedRejected()
    {
        _broker.PlaceOrderResponder = _ => throw new TimeoutException("kabu API timeout");

        var uc = NewUseCase();
        var result = await uc.ExecuteAsync(SampleIntent());

        Assert.Equal(OrderResultStatus.NetworkError, result.Status);
        Assert.Equal(OrderState.Rejected, result.Order.State);
        Assert.True(result.Order.IsTerminal);
    }

    [Fact]
    public async Task LimitOrderType_PassedThrough()
    {
        var intent = SampleIntent() with
        {
            OrderPrice = new Price(38000m),
            OrderType = OrderType.Limit
        };
        var uc = NewUseCase();
        await uc.ExecuteAsync(intent);

        Assert.Equal(OrderType.Limit, _broker.PlaceOrderCalls[0].OrderType);
    }

    [Fact]
    public async Task BestMarketOrderType_PassedThrough()
    {
        // intent.OrderType がそのまま OrderRequest に伝わる (旧 heuristic は廃止)
        var uc = NewUseCase();
        await uc.ExecuteAsync(SampleIntent());

        Assert.Equal(OrderType.BestMarket, _broker.PlaceOrderCalls[0].OrderType);
    }
}
