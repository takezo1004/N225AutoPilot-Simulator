using Microsoft.Extensions.Logging.Abstractions;
using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Application.Orders;
using N225BrokerBridge.Application.Tests.TestDoubles;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.Positions;
using N225BrokerBridge.Domain.ValueObjects;
using N225BrokerBridge.Infrastructure.Persistence;
using Xunit;

namespace N225BrokerBridge.Application.Tests.Orders;

public class ExecutionApplierTests
{
    private readonly InMemoryOrderRepository _orderRepo = new();
    private readonly InMemoryPositionRepository _positionRepo = new();
    private readonly StubAutoPositionMetadataStore _autoStore = new();
    private readonly StubPendingOrderTracker _pendingTracker = new();
    private readonly StubClosedTradeStore _closedStore = new();
    private readonly ContractMultiplierRegistry _multipliers = new();
    private readonly FixedDateTimeProvider _clock = new();

    private ExecutionApplier NewApplier() =>
        new(_orderRepo, _positionRepo, _autoStore, _pendingTracker, _closedStore, _multipliers, _clock,
            NullLogger<ExecutionApplier>.Instance);

    private static readonly StrategyName Strategy = new("V7-7-fixed");
    private static readonly SymbolCode Symbol = new("OSE:NK225M1!");
    private static readonly OrderId BrokerOrderId = new("BO-001");

    private async Task<Order> SeedSubmittedNewOrderAsync(int qty = 3)
    {
        var order = new Order(
            id: Guid.NewGuid(), brokerCode: BrokerCode.Kabu, strategy: Strategy, interval: 5,
            tradeMode: TradeMode.Auto, symbol: Symbol, side: Side.Buy,
            tradeType: TradeType.NewOrder, orderType: OrderType.BestMarket,
            timeInForce: TimeInForce.FAS, requestedQuantity: new Quantity(qty),
            limitPrice: Price.Zero, stopPrice: Price.Zero, targetExecutionId: null,
            createdAtUtc: _clock.UtcNow);
        await _orderRepo.AddAsync(order);
        order.MarkSubmitted(BrokerOrderId, _clock.UtcNow);
        await _orderRepo.UpdateAsync(order);
        return order;
    }

    private async Task<(Order, Position)> SeedSubmittedExitOrderAsync(string targetId, int qty = 1)
    {
        // 既存建玉を作成 + Reserve
        var pos = new Position(
            id: new ExecutionId(targetId), brokerCode: BrokerCode.Kabu,
            strategy: Strategy, interval: 5, tradeMode: TradeMode.Auto,
            symbol: Symbol, side: Side.Buy,
            initialQuantity: new Quantity(qty), entryPrice: new Price(38000m),
            openedAtUtc: _clock.UtcNow);
        await _positionRepo.AddAsync(pos);
        pos.ReserveForClose(new Quantity(qty));
        await _positionRepo.UpdateAsync(pos);

        // 返済 Order を作成
        var order = new Order(
            id: Guid.NewGuid(), brokerCode: BrokerCode.Kabu, strategy: Strategy, interval: 5,
            tradeMode: TradeMode.Auto, symbol: Symbol, side: Side.Sell,
            tradeType: TradeType.ExitOrder, orderType: OrderType.BestMarket,
            timeInForce: TimeInForce.FAS, requestedQuantity: new Quantity(qty),
            limitPrice: Price.Zero, stopPrice: Price.Zero,
            targetExecutionId: new ExecutionId(targetId),
            createdAtUtc: _clock.UtcNow);
        await _orderRepo.AddAsync(order);
        order.MarkSubmitted(BrokerOrderId, _clock.UtcNow);
        await _orderRepo.UpdateAsync(order);
        return (order, pos);
    }

    // ── 新規約定 → 建玉作成 ──────────────────────────────────────

    [Fact]
    public async Task NewOrderFill_FullFill_CreatesPositionAndFillsOrder()
    {
        var order = await SeedSubmittedNewOrderAsync(qty: 1);

        var ev = new ExecutionEvent(
            BrokerCode.Kabu, BrokerOrderId, new ExecutionId("E1"),
            TradeType.NewOrder, Side.Buy, Symbol,
            new Quantity(1), new Price(38000m), _clock.UtcNow, TargetPositionId: null);

        await NewApplier().ApplyAsync(ev);

        // Order が Filled
        var updatedOrder = await _orderRepo.FindByIdAsync(order.Id);
        Assert.Equal(OrderState.Filled, updatedOrder!.State);

        // Position が作成された
        var pos = await _positionRepo.FindByIdAsync(new ExecutionId("E1"));
        Assert.NotNull(pos);
        Assert.Equal(new Quantity(1), pos!.LeaveQuantity);
        Assert.Equal(Quantity.Zero, pos.HoldQuantity);
        Assert.Equal(Side.Buy, pos.Side);
    }

    [Fact]
    public async Task NewOrderFill_SplitFill_CreatesMultiplePositions()
    {
        // 3 枚注文が 1+1+1 で分割約定
        var order = await SeedSubmittedNewOrderAsync(qty: 3);
        var applier = NewApplier();

        await applier.ApplyAsync(new ExecutionEvent(
            BrokerCode.Kabu, BrokerOrderId, new ExecutionId("E1"),
            TradeType.NewOrder, Side.Buy, Symbol,
            new Quantity(1), new Price(38000m), _clock.UtcNow, null));
        await applier.ApplyAsync(new ExecutionEvent(
            BrokerCode.Kabu, BrokerOrderId, new ExecutionId("E2"),
            TradeType.NewOrder, Side.Buy, Symbol,
            new Quantity(1), new Price(38010m), _clock.UtcNow, null));
        await applier.ApplyAsync(new ExecutionEvent(
            BrokerCode.Kabu, BrokerOrderId, new ExecutionId("E3"),
            TradeType.NewOrder, Side.Buy, Symbol,
            new Quantity(1), new Price(38020m), _clock.UtcNow, null));

        // 3 建玉が作成された (1 約定 = 1 建玉モデル)
        var positions = await _positionRepo.FindActiveAsync();
        Assert.Equal(3, positions.Count);

        var updated = await _orderRepo.FindByIdAsync(order.Id);
        Assert.Equal(OrderState.Filled, updated!.State);
    }

    [Fact]
    public async Task NewOrderFill_Partial_OrderStaysPartiallyFilled()
    {
        var order = await SeedSubmittedNewOrderAsync(qty: 3);
        await NewApplier().ApplyAsync(new ExecutionEvent(
            BrokerCode.Kabu, BrokerOrderId, new ExecutionId("E1"),
            TradeType.NewOrder, Side.Buy, Symbol,
            new Quantity(1), new Price(38000m), _clock.UtcNow, null));

        var updated = await _orderRepo.FindByIdAsync(order.Id);
        Assert.Equal(OrderState.PartiallyFilled, updated!.State);
        Assert.Equal(new Quantity(2), updated.RemainingQuantity);
    }

    // ── 返済約定 → 建玉減算 ──────────────────────────────────────

    [Fact]
    public async Task ExitOrderFill_FullClose_RemovesPosition()
    {
        var (order, pos) = await SeedSubmittedExitOrderAsync("E1", qty: 1);

        await NewApplier().ApplyAsync(new ExecutionEvent(
            BrokerCode.Kabu, BrokerOrderId, new ExecutionId("EX_FILL_1"),
            TradeType.ExitOrder, Side.Sell, Symbol,
            new Quantity(1), new Price(38050m), _clock.UtcNow,
            TargetPositionId: new ExecutionId("E1")));

        var updatedOrder = await _orderRepo.FindByIdAsync(order.Id);
        Assert.Equal(OrderState.Filled, updatedOrder!.State);

        // 建玉は消滅
        var missing = await _positionRepo.FindByIdAsync(new ExecutionId("E1"));
        Assert.Null(missing);
    }

    [Fact]
    public async Task ExitOrderFill_PartialClose_PositionRemains()
    {
        // 建玉 (3 枚)、ReserveForClose(1) → 1 枚分の返済 Order が Submitted
        var pos = new Position(
            id: new ExecutionId("E1"), brokerCode: BrokerCode.Kabu,
            strategy: Strategy, interval: 5, tradeMode: TradeMode.Auto,
            symbol: Symbol, side: Side.Buy,
            initialQuantity: new Quantity(3), entryPrice: new Price(38000m),
            openedAtUtc: _clock.UtcNow);
        await _positionRepo.AddAsync(pos);
        pos.ReserveForClose(new Quantity(1));
        await _positionRepo.UpdateAsync(pos);

        var order = new Order(
            id: Guid.NewGuid(), brokerCode: BrokerCode.Kabu, strategy: Strategy, interval: 5,
            tradeMode: TradeMode.Auto, symbol: Symbol, side: Side.Sell,
            tradeType: TradeType.ExitOrder, orderType: OrderType.BestMarket,
            timeInForce: TimeInForce.FAS, requestedQuantity: new Quantity(1),
            limitPrice: Price.Zero, stopPrice: Price.Zero,
            targetExecutionId: new ExecutionId("E1"), createdAtUtc: _clock.UtcNow);
        await _orderRepo.AddAsync(order);
        order.MarkSubmitted(BrokerOrderId, _clock.UtcNow);
        await _orderRepo.UpdateAsync(order);

        await NewApplier().ApplyAsync(new ExecutionEvent(
            BrokerCode.Kabu, BrokerOrderId, new ExecutionId("EX_FILL_1"),
            TradeType.ExitOrder, Side.Sell, Symbol,
            new Quantity(1), new Price(38050m), _clock.UtcNow,
            TargetPositionId: new ExecutionId("E1")));

        var remaining = await _positionRepo.FindByIdAsync(new ExecutionId("E1"));
        Assert.NotNull(remaining);
        Assert.Equal(new Quantity(2), remaining!.LeaveQuantity);
        Assert.Equal(Quantity.Zero, remaining.HoldQuantity);
    }

    // ── 異常系 ──────────────────────────────────────────────────

    [Fact]
    public async Task UnknownOrder_LoggedAndIgnored()
    {
        var ev = new ExecutionEvent(
            BrokerCode.Kabu, new OrderId("UNKNOWN"), new ExecutionId("E1"),
            TradeType.NewOrder, Side.Buy, Symbol,
            new Quantity(1), new Price(38000m), _clock.UtcNow, null);

        // 例外を投げず黙って終了
        await NewApplier().ApplyAsync(ev);
    }

    [Fact]
    public async Task ExitOrder_WithoutTargetPositionId_LoggedAndIgnored()
    {
        var (order, _) = await SeedSubmittedExitOrderAsync("E1", qty: 1);

        // TargetPositionId が null の ExitOrder → スキップ
        await NewApplier().ApplyAsync(new ExecutionEvent(
            BrokerCode.Kabu, BrokerOrderId, new ExecutionId("EX_FILL_1"),
            TradeType.ExitOrder, Side.Sell, Symbol,
            new Quantity(1), new Price(38050m), _clock.UtcNow,
            TargetPositionId: null));

        // 建玉は変化なし
        var pos = await _positionRepo.FindByIdAsync(new ExecutionId("E1"));
        Assert.NotNull(pos);
        Assert.Equal(new Quantity(1), pos!.HoldQuantity);  // Reserve 状態維持
    }
}
