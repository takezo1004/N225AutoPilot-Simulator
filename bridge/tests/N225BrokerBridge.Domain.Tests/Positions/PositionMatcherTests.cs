using N225BrokerBridge.Domain.Positions;
using N225BrokerBridge.Domain.ValueObjects;
using Xunit;

namespace N225BrokerBridge.Domain.Tests.Positions;

public class PositionMatcherTests
{
    private static Position MakePos(string id, int qty)
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
            openedAtUtc: DateTime.UtcNow);

    // ── 基本シナリオ ─────────────────────────────────────────────

    [Fact]
    public void EmptyCandidates_RequestPositive_PlanIsEmptyShortfallEqualsRequest()
    {
        var plan = PositionMatcher.BuildPlan(Array.Empty<Position>(), new Quantity(3));
        Assert.Empty(plan.Allocations);
        Assert.Equal(new Quantity(3), plan.Shortfall);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void Request_Zero_Throws()
    {
        // ゼロ要求は呼び出し側の不具合 (webhook order_contracts=0)。
        Assert.Throws<ArgumentException>(() =>
            PositionMatcher.BuildPlan(new[] { MakePos("E1", 1) }, Quantity.Zero));
    }

    // ── V7-7 fixed の運用シナリオ (1+1+1 + 部分返済) ──────────────

    [Fact]
    public void ThreePositionsOfOne_RequestOne_ConsumesFirstOnly()
    {
        // V7-7: 3 ポジ (1+1+1) があり「1 枚返済」シグナル
        var p1 = MakePos("E1", 1);
        var p2 = MakePos("E2", 1);
        var p3 = MakePos("E3", 1);

        var plan = PositionMatcher.BuildPlan(new[] { p1, p2, p3 }, new Quantity(1));

        Assert.True(plan.IsComplete);
        Assert.Equal(new Quantity(1), plan.TotalToClose);
        Assert.Single(plan.Allocations);
        Assert.Equal(p1, plan.Allocations[0].Position);   // ExecutionId 順で E1 が先
        Assert.Equal(new Quantity(1), plan.Allocations[0].Quantity);
    }

    [Fact]
    public void ThreePositionsOfOne_RequestThree_ConsumesAll()
    {
        // V7-7 stoploss: 全 3 枚一括返済
        var p1 = MakePos("E1", 1);
        var p2 = MakePos("E2", 1);
        var p3 = MakePos("E3", 1);

        var plan = PositionMatcher.BuildPlan(new[] { p1, p2, p3 }, new Quantity(3));

        Assert.True(plan.IsComplete);
        Assert.Equal(new Quantity(3), plan.TotalToClose);
        Assert.Equal(3, plan.Allocations.Count);
        Assert.All(plan.Allocations, a => Assert.Equal(new Quantity(1), a.Quantity));
    }

    // ── 跨ぎ消化シナリオ ─────────────────────────────────────────

    [Fact]
    public void TwoPlusOne_RequestTwo_SpansAcrossPositions()
    {
        // 建玉 (2+1)、2 枚返済
        // → ExecutionId 順で E_A(2 枚) が先 → 2 枚全消化、要求満了
        var pA = MakePos("E_A", 2);
        var pB = MakePos("E_B", 1);

        var plan = PositionMatcher.BuildPlan(new[] { pA, pB }, new Quantity(2));

        Assert.True(plan.IsComplete);
        Assert.Single(plan.Allocations);
        Assert.Equal(pA, plan.Allocations[0].Position);
        Assert.Equal(new Quantity(2), plan.Allocations[0].Quantity);
    }

    [Fact]
    public void OnePlusTwo_RequestTwo_SpansAcrossPositions()
    {
        // 建玉 (1+2)、2 枚返済
        // → ExecutionId 順で E_A(1 枚) が先 → 1 枚全消化、残要求 1
        // → 次に E_B(2 枚) → 1 枚部分消化、要求満了
        var pA = MakePos("E_A", 1);
        var pB = MakePos("E_B", 2);

        var plan = PositionMatcher.BuildPlan(new[] { pA, pB }, new Quantity(2));

        Assert.True(plan.IsComplete);
        Assert.Equal(2, plan.Allocations.Count);
        Assert.Equal(pA, plan.Allocations[0].Position);
        Assert.Equal(new Quantity(1), plan.Allocations[0].Quantity);
        Assert.Equal(pB, plan.Allocations[1].Position);
        Assert.Equal(new Quantity(1), plan.Allocations[1].Quantity);
    }

    // ── 要求 > 残合計シナリオ (C1 動作) ──────────────────────────

    [Fact]
    public void RequestExceedsTotalAvailable_ReturnsPartialPlanWithShortfall()
    {
        // 建玉 (1+1)、4 枚返済要求 → 2 枚消化 + Shortfall=2
        var p1 = MakePos("E1", 1);
        var p2 = MakePos("E2", 1);

        var plan = PositionMatcher.BuildPlan(new[] { p1, p2 }, new Quantity(4));

        Assert.False(plan.IsComplete);
        Assert.Equal(new Quantity(2), plan.TotalToClose);
        Assert.Equal(new Quantity(2), plan.Shortfall);
        Assert.Equal(2, plan.Allocations.Count);
    }

    // ── 既に拘束中の建玉は除外 ──────────────────────────────────

    [Fact]
    public void PositionWithFullHoldQty_IsSkipped()
    {
        // 建玉 E1 (1 枚、既に 1 枚拘束済み → Available=0)
        // → 候補から除外され Shortfall に計上
        var p1 = MakePos("E1", 1);
        p1.ReserveForClose(new Quantity(1));

        var p2 = MakePos("E2", 1);

        var plan = PositionMatcher.BuildPlan(new[] { p1, p2 }, new Quantity(2));

        Assert.False(plan.IsComplete);
        Assert.Single(plan.Allocations);
        Assert.Equal(p2, plan.Allocations[0].Position);
        Assert.Equal(new Quantity(1), plan.Shortfall);
    }

    [Fact]
    public void PartiallyHoldPosition_ContributesAvailableOnly()
    {
        // 建玉 E1 (3 枚、既に 2 枚拘束 → Available=1)
        // → 1 枚分だけ計画に入る
        var p1 = MakePos("E1", 3);
        p1.ReserveForClose(new Quantity(2));

        var plan = PositionMatcher.BuildPlan(new[] { p1 }, new Quantity(3));

        Assert.False(plan.IsComplete);
        Assert.Single(plan.Allocations);
        Assert.Equal(new Quantity(1), plan.Allocations[0].Quantity);
        Assert.Equal(new Quantity(2), plan.Shortfall);
    }

    // ── ExecutionId 順の決定性 ──────────────────────────────────

    [Fact]
    public void OrderingByExecutionId_IsDeterministic_RegardlessOfInputOrder()
    {
        var p1 = MakePos("E_A", 1);
        var p2 = MakePos("E_B", 1);
        var p3 = MakePos("E_C", 1);

        // 入力順を変えても結果は同じ
        var planAsc = PositionMatcher.BuildPlan(new[] { p1, p2, p3 }, new Quantity(2));
        var planDesc = PositionMatcher.BuildPlan(new[] { p3, p2, p1 }, new Quantity(2));

        Assert.Equal(planAsc.Allocations[0].Position.Id, planDesc.Allocations[0].Position.Id);
        Assert.Equal(planAsc.Allocations[1].Position.Id, planDesc.Allocations[1].Position.Id);
    }
}
