using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.Positions;
using N225BrokerBridge.Domain.Positions.Events;
using N225BrokerBridge.Domain.ValueObjects;
using Xunit;

namespace N225BrokerBridge.Domain.Tests.Positions;

public class PositionTests
{
    private static Position NewSamplePosition(int qty = 3, string id = "E001")
        => new(
            id: new ExecutionId(id),
            brokerCode: BrokerCode.Kabu,
            strategy: new StrategyName("V7-7-fixed"),
            interval: 5,
            tradeMode: TradeMode.Auto,
            symbol: new SymbolCode("167060019"),
            side: Side.Buy,
            initialQuantity: new Quantity(qty),
            entryPrice: new Price(38000m),
            openedAtUtc: new DateTime(2026, 5, 17, 9, 0, 0, DateTimeKind.Utc));

    // ── 構築 ─────────────────────────────────────────────────────

    [Fact]
    public void Construct_Succeeds_AndRaisesOpenedEvent()
    {
        var p = NewSamplePosition(qty: 3);
        Assert.Equal(new Quantity(3), p.LeaveQuantity);
        Assert.Equal(Quantity.Zero, p.HoldQuantity);
        Assert.Equal(new Quantity(3), p.AvailableForClose);
        Assert.False(p.IsClosed);
        Assert.Single(p.DomainEvents);
        Assert.IsType<PositionOpenedEvent>(p.DomainEvents[0]);
    }

    [Fact]
    public void Construct_ZeroQuantity_Throws()
    {
        Assert.Throws<InvalidValueObjectException>(() => NewSamplePosition(qty: 0));
    }

    // ── ReserveForClose ──────────────────────────────────────────

    [Fact]
    public void ReserveForClose_Partial_IncreasesHoldQty()
    {
        var p = NewSamplePosition(qty: 3);
        p.ReserveForClose(new Quantity(2));

        Assert.Equal(new Quantity(3), p.LeaveQuantity);
        Assert.Equal(new Quantity(2), p.HoldQuantity);
        Assert.Equal(new Quantity(1), p.AvailableForClose);
        Assert.IsType<PositionUpdatedEvent>(p.DomainEvents[^1]);
    }

    [Fact]
    public void ReserveForClose_ExceedsAvailable_Throws()
    {
        var p = NewSamplePosition(qty: 3);
        Assert.Throws<InvalidOperationException>(() => p.ReserveForClose(new Quantity(4)));
    }

    [Fact]
    public void ReserveForClose_AfterPartialReserve_LimitedToAvailable()
    {
        var p = NewSamplePosition(qty: 3);
        p.ReserveForClose(new Quantity(2));
        // AvailableForClose = 1 になっているので 2 枚追加発注は不可
        Assert.Throws<InvalidOperationException>(() => p.ReserveForClose(new Quantity(2)));
        // 1 枚なら OK
        p.ReserveForClose(new Quantity(1));
        Assert.Equal(new Quantity(3), p.HoldQuantity);
        Assert.Equal(Quantity.Zero, p.AvailableForClose);
    }

    [Fact]
    public void ReserveForClose_Zero_Throws()
    {
        var p = NewSamplePosition();
        Assert.Throws<InvalidValueObjectException>(() => p.ReserveForClose(Quantity.Zero));
    }

    // ── ApplyClosure ──────────────────────────────────────────

    [Fact]
    public void ApplyClosure_FullQuantity_ClosesPosition()
    {
        var p = NewSamplePosition(qty: 1);
        p.ReserveForClose(new Quantity(1));
        p.ApplyClosure(new Quantity(1), DateTime.UtcNow);

        Assert.True(p.IsClosed);
        Assert.Equal(Quantity.Zero, p.LeaveQuantity);
        Assert.Equal(Quantity.Zero, p.HoldQuantity);

        // 最後のイベントは PositionClosedEvent
        Assert.IsType<PositionClosedEvent>(p.DomainEvents[^1]);
    }

    [Fact]
    public void ApplyClosure_Partial_KeepsPositionOpen()
    {
        var p = NewSamplePosition(qty: 3);
        p.ReserveForClose(new Quantity(2));
        p.ApplyClosure(new Quantity(2), DateTime.UtcNow);

        Assert.False(p.IsClosed);
        Assert.Equal(new Quantity(1), p.LeaveQuantity);
        Assert.Equal(Quantity.Zero, p.HoldQuantity);
        Assert.Equal(new Quantity(1), p.AvailableForClose);

        // PositionClosedEvent は発火しない
        Assert.DoesNotContain(p.DomainEvents, e => e is PositionClosedEvent);
    }

    [Fact]
    public void ApplyClosure_WithoutPriorReserve_Throws()
    {
        // 不変条件: ApplyClosure(qty) ≤ HoldQty
        var p = NewSamplePosition(qty: 3);
        Assert.Throws<InvalidOperationException>(() =>
            p.ApplyClosure(new Quantity(1), DateTime.UtcNow));
    }

    [Fact]
    public void ApplyClosure_ExceedsLeave_Throws()
    {
        var p = NewSamplePosition(qty: 2);
        p.ReserveForClose(new Quantity(2));
        // 全部 Hold したが Closure 3 はあり得ない
        Assert.Throws<InvalidOperationException>(() =>
            p.ApplyClosure(new Quantity(3), DateTime.UtcNow));
    }

    [Fact]
    public void ApplyClosure_AfterClosed_Throws()
    {
        var p = NewSamplePosition(qty: 1);
        p.ReserveForClose(new Quantity(1));
        p.ApplyClosure(new Quantity(1), DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            p.ApplyClosure(new Quantity(1), DateTime.UtcNow));
    }

    // ── ReleaseReservation ───────────────────────────────────────

    [Fact]
    public void ReleaseReservation_Partial_DecreasesHoldQty()
    {
        var p = NewSamplePosition(qty: 3);
        p.ReserveForClose(new Quantity(2));
        p.ReleaseReservation(new Quantity(1));

        Assert.Equal(new Quantity(3), p.LeaveQuantity);
        Assert.Equal(new Quantity(1), p.HoldQuantity);
        Assert.Equal(new Quantity(2), p.AvailableForClose);
    }

    [Fact]
    public void ReleaseReservation_ExceedsHold_Throws()
    {
        var p = NewSamplePosition(qty: 3);
        p.ReserveForClose(new Quantity(1));
        Assert.Throws<InvalidOperationException>(() => p.ReleaseReservation(new Quantity(2)));
    }

    // ── 跨ぎ消化シナリオ (全体フロー) ─────────────────────────────

    [Fact]
    public void Scenario_SpanningCloseWithTwoPositions_WorksCorrectly()
    {
        // 想定:
        //   建玉 A (2 枚), 建玉 B (1 枚)
        //   webhook で「2 枚返済」シグナル
        //   → A から 1 枚部分返済 + B から 1 枚全消化、で合計 2 枚
        var posA = NewSamplePosition(qty: 2, id: "E_A");
        var posB = NewSamplePosition(qty: 1, id: "E_B");

        // 跨ぎ消化: B 全消化 + A から 1 枚
        posB.ReserveForClose(new Quantity(1));
        posA.ReserveForClose(new Quantity(1));

        posB.ApplyClosure(new Quantity(1), DateTime.UtcNow);
        posA.ApplyClosure(new Quantity(1), DateTime.UtcNow);

        Assert.True(posB.IsClosed);
        Assert.False(posA.IsClosed);
        Assert.Equal(new Quantity(1), posA.LeaveQuantity);
        Assert.Equal(Quantity.Zero, posA.HoldQuantity);
    }
}
