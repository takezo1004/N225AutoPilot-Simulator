using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.Orders.Events;
using N225BrokerBridge.Domain.ValueObjects;
using Xunit;

namespace N225BrokerBridge.Domain.Tests.Orders;

public class OrderTests
{
    private static Order NewSampleOrder(
        TradeType tradeType = TradeType.NewOrder,
        int qty = 3,
        ExecutionId? targetExecutionId = null)
        => new(
            id: Guid.NewGuid(),
            brokerCode: BrokerCode.Kabu,
            strategy: new StrategyName("V7-7-fixed"),
            interval: 5,
            tradeMode: TradeMode.Auto,
            symbol: new SymbolCode("167060019"),
            side: Side.Buy,
            tradeType: tradeType,
            orderType: OrderType.BestMarket,
            timeInForce: TimeInForce.FAS,
            requestedQuantity: new Quantity(qty),
            limitPrice: Price.Zero,
            stopPrice: Price.Zero,
            targetExecutionId: targetExecutionId,
            createdAtUtc: new DateTime(2026, 5, 17, 9, 0, 0, DateTimeKind.Utc));

    // ── 構築・不変条件 ───────────────────────────────────────────

    [Fact]
    public void Constructor_NewOrder_InitialStateIsCreated()
    {
        var order = NewSampleOrder();
        Assert.Equal(OrderState.Created, order.State);
        Assert.Equal(new Quantity(3), order.RequestedQuantity);
        Assert.Equal(Quantity.Zero, order.CumulativeExecutedQuantity);
        Assert.Equal(new Quantity(3), order.RemainingQuantity);
        Assert.Null(order.BrokerOrderId);
        Assert.False(order.IsTerminal);
    }

    [Fact]
    public void Constructor_ExitOrderWithoutTarget_Throws()
    {
        Assert.Throws<InvalidValueObjectException>(() => NewSampleOrder(
            tradeType: TradeType.ExitOrder, targetExecutionId: null));
    }

    [Fact]
    public void Constructor_ExitOrderWithTarget_Succeeds()
    {
        var order = NewSampleOrder(
            tradeType: TradeType.ExitOrder,
            targetExecutionId: new ExecutionId("E001"));
        Assert.Equal(TradeType.ExitOrder, order.TradeType);
        Assert.Equal(new ExecutionId("E001"), order.TargetExecutionId);
    }

    [Fact]
    public void Constructor_ZeroQuantity_Throws()
    {
        Assert.Throws<InvalidValueObjectException>(() => NewSampleOrder(qty: 0));
    }

    [Fact]
    public void Constructor_NonPositiveInterval_Throws()
    {
        Assert.Throws<InvalidValueObjectException>(() => new Order(
            id: Guid.NewGuid(),
            brokerCode: BrokerCode.Kabu,
            strategy: new StrategyName("X"),
            interval: 0,
            tradeMode: TradeMode.Auto,
            symbol: new SymbolCode("S"),
            side: Side.Buy,
            tradeType: TradeType.NewOrder,
            orderType: OrderType.Market,
            timeInForce: TimeInForce.FAK,
            requestedQuantity: new Quantity(1),
            limitPrice: Price.Zero,
            stopPrice: Price.Zero,
            targetExecutionId: null,
            createdAtUtc: DateTime.UtcNow));
    }

    // ── MarkSubmitted ───────────────────────────────────────────

    [Fact]
    public void MarkSubmitted_FromCreated_TransitionsToSubmitted()
    {
        var order = NewSampleOrder();
        var submittedAt = DateTime.UtcNow;
        order.MarkSubmitted(new OrderId("BO-001"), submittedAt);

        Assert.Equal(OrderState.Submitted, order.State);
        Assert.Equal(new OrderId("BO-001"), order.BrokerOrderId);
        Assert.Equal(submittedAt, order.SubmittedAt);
        Assert.Single(order.DomainEvents);
        Assert.IsType<OrderSubmittedEvent>(order.DomainEvents[0]);
    }

    [Fact]
    public void MarkSubmitted_FromSubmitted_Throws()
    {
        var order = NewSampleOrder();
        order.MarkSubmitted(new OrderId("BO-001"), DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() =>
            order.MarkSubmitted(new OrderId("BO-002"), DateTime.UtcNow));
    }

    // ── ApplyExecution ───────────────────────────────────────────

    [Fact]
    public void ApplyExecution_FromCreated_Throws()
    {
        var order = NewSampleOrder();
        var fill = new Execution(new ExecutionId("E1"), new Quantity(1), new Price(38000m), DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() => order.ApplyExecution(fill));
    }

    [Fact]
    public void ApplyExecution_PartialFill_TransitionsToPartiallyFilled()
    {
        var order = NewSampleOrder(qty: 3);
        order.MarkSubmitted(new OrderId("BO-001"), DateTime.UtcNow);

        order.ApplyExecution(new Execution(new ExecutionId("E1"), new Quantity(1), new Price(38000m), DateTime.UtcNow));

        Assert.Equal(OrderState.PartiallyFilled, order.State);
        Assert.Equal(new Quantity(1), order.CumulativeExecutedQuantity);
        Assert.Equal(new Quantity(2), order.RemainingQuantity);
        Assert.False(order.IsTerminal);

        var evt = (OrderExecutedEvent)order.DomainEvents[^1];
        Assert.False(evt.IsFullyFilled);
        Assert.Equal(new Quantity(1), evt.ExecutedQuantity);
    }

    [Fact]
    public void ApplyExecution_SplitFills_AllPartialUntilLast()
    {
        // 3 枚注文が 1+1+1 に分割約定するシナリオ
        var order = NewSampleOrder(qty: 3);
        order.MarkSubmitted(new OrderId("BO-001"), DateTime.UtcNow);

        order.ApplyExecution(new Execution(new ExecutionId("E1"), new Quantity(1), new Price(38000m), DateTime.UtcNow));
        Assert.Equal(OrderState.PartiallyFilled, order.State);

        order.ApplyExecution(new Execution(new ExecutionId("E2"), new Quantity(1), new Price(38010m), DateTime.UtcNow));
        Assert.Equal(OrderState.PartiallyFilled, order.State);

        order.ApplyExecution(new Execution(new ExecutionId("E3"), new Quantity(1), new Price(38020m), DateTime.UtcNow));
        Assert.Equal(OrderState.Filled, order.State);

        Assert.Equal(3, order.Executions.Count);
        Assert.Equal(new Quantity(3), order.CumulativeExecutedQuantity);
        Assert.Equal(Quantity.Zero, order.RemainingQuantity);
        Assert.True(order.IsTerminal);

        // 最後のイベントは IsFullyFilled=true
        var lastEvt = order.DomainEvents.OfType<OrderExecutedEvent>().Last();
        Assert.True(lastEvt.IsFullyFilled);
    }

    [Fact]
    public void ApplyExecution_ExceedsRemaining_Throws()
    {
        var order = NewSampleOrder(qty: 2);
        order.MarkSubmitted(new OrderId("BO-001"), DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() =>
            order.ApplyExecution(new Execution(new ExecutionId("E1"), new Quantity(3), new Price(38000m), DateTime.UtcNow)));
    }

    [Fact]
    public void ApplyExecution_DuplicateExecutionId_Throws()
    {
        var order = NewSampleOrder(qty: 3);
        order.MarkSubmitted(new OrderId("BO-001"), DateTime.UtcNow);

        order.ApplyExecution(new Execution(new ExecutionId("E1"), new Quantity(1), new Price(38000m), DateTime.UtcNow));
        Assert.Throws<InvalidOperationException>(() =>
            order.ApplyExecution(new Execution(new ExecutionId("E1"), new Quantity(1), new Price(38000m), DateTime.UtcNow)));
    }

    [Fact]
    public void ApplyExecution_AfterFilled_Throws()
    {
        var order = NewSampleOrder(qty: 1);
        order.MarkSubmitted(new OrderId("BO-001"), DateTime.UtcNow);
        order.ApplyExecution(new Execution(new ExecutionId("E1"), new Quantity(1), new Price(38000m), DateTime.UtcNow));
        // 終端 (Filled) 後はそれ以上の Execution 不可
        Assert.Throws<InvalidOperationException>(() =>
            order.ApplyExecution(new Execution(new ExecutionId("E2"), new Quantity(1), new Price(38000m), DateTime.UtcNow)));
    }

    // ── MarkTerminated ───────────────────────────────────────────

    [Theory]
    [InlineData(OrderState.Cancelled)]
    [InlineData(OrderState.Expired)]
    [InlineData(OrderState.Rejected)]
    public void MarkTerminated_AbnormalState_Succeeds(OrderState terminal)
    {
        var order = NewSampleOrder();
        order.MarkSubmitted(new OrderId("BO-001"), DateTime.UtcNow);
        order.MarkTerminated(terminal, "test reason", DateTime.UtcNow);
        Assert.Equal(terminal, order.State);
        Assert.True(order.IsTerminal);
        Assert.IsType<OrderTerminatedEvent>(order.DomainEvents[^1]);
    }

    [Fact]
    public void MarkTerminated_WithFilledState_Throws()
    {
        var order = NewSampleOrder();
        order.MarkSubmitted(new OrderId("BO-001"), DateTime.UtcNow);
        Assert.Throws<ArgumentException>(() =>
            order.MarkTerminated(OrderState.Filled, null, DateTime.UtcNow));
    }

    [Fact]
    public void MarkTerminated_AfterAlreadyTerminal_Throws()
    {
        var order = NewSampleOrder();
        order.MarkSubmitted(new OrderId("BO-001"), DateTime.UtcNow);
        order.MarkTerminated(OrderState.Cancelled, null, DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() =>
            order.MarkTerminated(OrderState.Rejected, null, DateTime.UtcNow));
    }

    // ── ドメインイベント発火順序 ──────────────────────────────────

    [Fact]
    public void DomainEvents_FiredInOrder()
    {
        var order = NewSampleOrder(qty: 2);
        order.MarkSubmitted(new OrderId("BO-001"), DateTime.UtcNow);
        order.ApplyExecution(new Execution(new ExecutionId("E1"), new Quantity(1), new Price(38000m), DateTime.UtcNow));
        order.ApplyExecution(new Execution(new ExecutionId("E2"), new Quantity(1), new Price(38010m), DateTime.UtcNow));

        Assert.Collection(order.DomainEvents,
            e => Assert.IsType<OrderSubmittedEvent>(e),
            e => Assert.IsType<OrderExecutedEvent>(e),
            e => Assert.IsType<OrderExecutedEvent>(e));
    }

    [Fact]
    public void ClearDomainEvents_RemovesAll()
    {
        var order = NewSampleOrder();
        order.MarkSubmitted(new OrderId("BO-001"), DateTime.UtcNow);
        Assert.NotEmpty(order.DomainEvents);
        order.ClearDomainEvents();
        Assert.Empty(order.DomainEvents);
    }

    // ── StrategyName VO 単体 ─────────────────────────────────────

    [Fact]
    public void StrategyName_Empty_Throws()
    {
        Assert.Throws<InvalidValueObjectException>(() => new StrategyName(""));
    }
}
