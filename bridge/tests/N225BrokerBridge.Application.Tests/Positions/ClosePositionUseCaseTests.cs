using Microsoft.Extensions.Logging.Abstractions;
using N225BrokerBridge.Application.Positions;
using N225BrokerBridge.Application.Signals;
using N225BrokerBridge.Application.Tests.TestDoubles;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.Positions;
using N225BrokerBridge.Domain.ValueObjects;
using N225BrokerBridge.Infrastructure.Persistence;
using Xunit;

namespace N225BrokerBridge.Application.Tests.Positions;

public class ClosePositionUseCaseTests
{
    private readonly FakeBrokerAdapter _broker = new();
    private readonly InMemoryOrderRepository _orderRepo = new();
    private readonly InMemoryPositionRepository _positionRepo = new();
    private readonly FixedDateTimeProvider _clock = new();
    private readonly StubOrderMetadataStore _orderMetaStore = new();
    private readonly StubPendingOrderTracker _pendingTracker = new();

    private ClosePositionUseCase NewUseCase()
        => new(_broker, _orderRepo, _positionRepo, _orderMetaStore, _pendingTracker, _clock,
            NullLogger<ClosePositionUseCase>.Instance);

    private static StrategyName Strategy => new("V7-7-fixed");
    private static SymbolCode Symbol => new("OSE:NK225M1!");

    private async Task<Position> SeedPositionAsync(string execId, int qty, Side side = Side.Buy)
    {
        var pos = new Position(
            id: new ExecutionId(execId),
            brokerCode: BrokerCode.Kabu,
            strategy: Strategy,
            interval: 5,
            tradeMode: TradeMode.Auto,
            symbol: Symbol,
            side: side,
            initialQuantity: new Quantity(qty),
            entryPrice: new Price(38000m),
            openedAtUtc: _clock.UtcNow);
        await _positionRepo.AddAsync(pos);
        return pos;
    }

    private static ExitOrderIntent MakeIntent(int requestQty, Side originalSide = Side.Buy)
        => new(Strategy, 5, TradeMode.Auto, Symbol, originalSide,
            new Quantity(requestQty), Price.Zero);

    // ── V7-7 fixed の運用シナリオ ────────────────────────────────

    [Fact]
    public async Task ThreePositionsOfOne_RequestOne_ClosesOnePositionOnly()
    {
        // 建玉 (1+1+1)、1 枚返済シグナル → 先頭 1 ポジを 1 枚で返済発注
        await SeedPositionAsync("E1", 1);
        await SeedPositionAsync("E2", 1);
        await SeedPositionAsync("E3", 1);

        var uc = NewUseCase();
        var result = await uc.ExecuteAsync(MakeIntent(1));

        Assert.False(result.HasNoMatchingPositions);
        Assert.True(result.Plan!.IsComplete);
        Assert.Equal(Quantity.Zero, result.Shortfall);

        // 発注は 1 件のみ
        Assert.Single(_broker.ClosePositionCalls);
        var call = _broker.ClosePositionCalls[0];
        Assert.Equal(new ExecutionId("E1"), call.TargetExecutionId);
        Assert.Equal(new Quantity(1), call.Quantity);
        Assert.Equal(Side.Buy, call.OriginalSide);

        // E2/E3 は触られていない
        var e2 = await _positionRepo.FindByIdAsync(new ExecutionId("E2"));
        var e3 = await _positionRepo.FindByIdAsync(new ExecutionId("E3"));
        Assert.Equal(Quantity.Zero, e2!.HoldQuantity);
        Assert.Equal(Quantity.Zero, e3!.HoldQuantity);

        // E1 は HoldQty=1 (拘束済み)
        var e1 = await _positionRepo.FindByIdAsync(new ExecutionId("E1"));
        Assert.Equal(new Quantity(1), e1!.HoldQuantity);
        Assert.Equal(new Quantity(1), e1.LeaveQuantity);  // 約定通知前なので残数量はそのまま
    }

    [Fact]
    public async Task ThreePositionsOfOne_RequestThree_ClosesAll()
    {
        // V7-7 stoploss: 3 ポジ全消化
        await SeedPositionAsync("E1", 1);
        await SeedPositionAsync("E2", 1);
        await SeedPositionAsync("E3", 1);

        var uc = NewUseCase();
        var result = await uc.ExecuteAsync(MakeIntent(3));

        Assert.True(result.Plan!.IsComplete);
        Assert.Equal(3, _broker.ClosePositionCalls.Count);
        Assert.All(_broker.ClosePositionCalls, c => Assert.Equal(new Quantity(1), c.Quantity));
    }

    // ── 跨ぎ消化 ────────────────────────────────────────────────

    [Fact]
    public async Task OnePlusTwo_RequestTwo_SpansAcrossPositions()
    {
        // 建玉 (1+2)、2 枚返済要求 → E_A (1 枚全消化) + E_B (1 枚部分消化)
        await SeedPositionAsync("E_A", 1);
        await SeedPositionAsync("E_B", 2);

        var uc = NewUseCase();
        var result = await uc.ExecuteAsync(MakeIntent(2));

        Assert.True(result.Plan!.IsComplete);
        Assert.Equal(2, _broker.ClosePositionCalls.Count);

        var first = _broker.ClosePositionCalls[0];
        Assert.Equal(new ExecutionId("E_A"), first.TargetExecutionId);
        Assert.Equal(new Quantity(1), first.Quantity);

        var second = _broker.ClosePositionCalls[1];
        Assert.Equal(new ExecutionId("E_B"), second.TargetExecutionId);
        Assert.Equal(new Quantity(1), second.Quantity);

        var posB = await _positionRepo.FindByIdAsync(new ExecutionId("E_B"));
        Assert.Equal(new Quantity(1), posB!.HoldQuantity);   // 1 枚拘束
        Assert.Equal(new Quantity(2), posB.LeaveQuantity);   // まだ約定通知前
    }

    // ── 残合計不足 (C1 動作) ────────────────────────────────────

    [Fact]
    public async Task RequestExceedsTotal_PartialClose_WithShortfall()
    {
        await SeedPositionAsync("E1", 1);

        var uc = NewUseCase();
        var result = await uc.ExecuteAsync(MakeIntent(3));

        Assert.False(result.Plan!.IsComplete);
        Assert.Equal(new Quantity(2), result.Shortfall);   // 3 要求 - 1 消化 = 2 不足
        Assert.Single(_broker.ClosePositionCalls);
    }

    // ── マッチなし ──────────────────────────────────────────────

    [Fact]
    public async Task NoMatchingPositions_ReturnsNoMatch()
    {
        var uc = NewUseCase();
        var result = await uc.ExecuteAsync(MakeIntent(1));

        Assert.True(result.HasNoMatchingPositions);
        Assert.Empty(_broker.ClosePositionCalls);
    }

    [Fact]
    public async Task DifferentStrategy_DoesNotMatch()
    {
        // 別 strategy の建玉は対象外
        var pos = new Position(
            id: new ExecutionId("E1"),
            brokerCode: BrokerCode.Kabu,
            strategy: new StrategyName("OtherStrategy"),
            interval: 5,
            tradeMode: TradeMode.Auto,
            symbol: Symbol,
            side: Side.Buy,
            initialQuantity: new Quantity(1),
            entryPrice: new Price(38000m),
            openedAtUtc: _clock.UtcNow);
        await _positionRepo.AddAsync(pos);

        var uc = NewUseCase();
        var result = await uc.ExecuteAsync(MakeIntent(1));

        Assert.True(result.HasNoMatchingPositions);
        Assert.Empty(_broker.ClosePositionCalls);
        Assert.Equal(Quantity.Zero, pos.HoldQuantity);  // 触られていない
    }

    // ── 発注失敗時の拘束解放 ────────────────────────────────────

    [Fact]
    public async Task BrokerRejected_ReservationIsReleased()
    {
        await SeedPositionAsync("E1", 1);
        _broker.ClosePositionResponder = req => new OrderResult(
            req.CorrelationId, OrderResultStatus.Rejected, null,
            "E999", "Rejected", DateTime.UtcNow);

        var uc = NewUseCase();
        await uc.ExecuteAsync(MakeIntent(1));

        var pos = await _positionRepo.FindByIdAsync(new ExecutionId("E1"));
        Assert.Equal(Quantity.Zero, pos!.HoldQuantity);    // 拘束解放済み
        Assert.Equal(new Quantity(1), pos.LeaveQuantity);  // 残数量はそのまま
    }

    [Fact]
    public async Task BrokerException_ReservationIsReleased()
    {
        await SeedPositionAsync("E1", 1);
        _broker.ClosePositionResponder = _ => throw new TimeoutException("kabu timeout");

        var uc = NewUseCase();
        await uc.ExecuteAsync(MakeIntent(1));

        var pos = await _positionRepo.FindByIdAsync(new ExecutionId("E1"));
        Assert.Equal(Quantity.Zero, pos!.HoldQuantity);
    }

    // ── 返済オーダーは反対サイドで作成される ───────────────────

    [Fact]
    public async Task ExitOrder_UsesOppositeSide()
    {
        // Long 建玉 (Side.Buy) → 返済発注は Side.Sell
        await SeedPositionAsync("E1", 1, side: Side.Buy);

        var uc = NewUseCase();
        var result = await uc.ExecuteAsync(MakeIntent(1, originalSide: Side.Buy));

        var exitOrder = result.ExitOrders.Single();
        Assert.Equal(Side.Sell, exitOrder.Side);  // 反対サイド
        Assert.Equal(TradeType.ExitOrder, exitOrder.TradeType);
        Assert.Equal(new ExecutionId("E1"), exitOrder.TargetExecutionId);
    }
}
