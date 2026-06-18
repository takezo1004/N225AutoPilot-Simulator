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

/// <summary>
/// ポジション履歴の記録 (ExecutionApplier 決済時フック) のテスト。
/// 詳細仕様: docs/position-history-spec.md §6 (PH-U1〜U4)。
/// </summary>
public class PositionHistoryRecordingTests
{
    private readonly InMemoryOrderRepository _orderRepo = new();
    private readonly InMemoryPositionRepository _positionRepo = new();
    private readonly StubAutoPositionMetadataStore _autoStore = new();
    private readonly StubPendingOrderTracker _pendingTracker = new();
    private readonly StubClosedTradeStore _closedStore = new();
    private readonly ContractMultiplierRegistry _multipliers = new();
    private readonly FixedDateTimeProvider _clock = new();

    private static readonly SymbolCode Micro = new("161060023");   // 日経225Micro
    private static readonly SymbolCode Mini = new("161060019");    // 日経225Mini

    public PositionHistoryRecordingTests()
    {
        _multipliers.Set(Micro.Value, 10);
        _multipliers.Set(Mini.Value, 100);
    }

    private ExecutionApplier NewApplier() =>
        new(_orderRepo, _positionRepo, _autoStore, _pendingTracker, _closedStore, _multipliers, _clock,
            NullLogger<ExecutionApplier>.Instance);

    private async Task<Position> SeedPositionAsync(
        string id, Side side, decimal entry, SymbolCode symbol, int qty,
        string strategy = "V7-7", int interval = 5, TradeMode mode = TradeMode.Auto)
    {
        var pos = new Position(
            id: new ExecutionId(id), brokerCode: BrokerCode.Kabu,
            strategy: new StrategyName(strategy), interval: interval, tradeMode: mode,
            symbol: symbol, side: side, initialQuantity: new Quantity(qty),
            entryPrice: new Price(entry), openedAtUtc: _clock.UtcNow);
        await _positionRepo.AddAsync(pos);
        return pos;
    }

    /// <summary>建玉を 1 回分 (qty 枚) 決済する。返済 Order を seed → ExecutionEvent を ApplyAsync。</summary>
    private async Task CloseOnceAsync(
        ExecutionApplier applier, Position pos, string brokerOrderId, string exitExecId,
        decimal exitPrice, int qty)
    {
        pos.ReserveForClose(new Quantity(qty));
        await _positionRepo.UpdateAsync(pos);

        var exitSide = pos.Side.Opposite();
        var order = new Order(
            id: Guid.NewGuid(), brokerCode: BrokerCode.Kabu, strategy: pos.Strategy,
            interval: pos.Interval, tradeMode: pos.TradeMode, symbol: pos.Symbol, side: exitSide,
            tradeType: TradeType.ExitOrder, orderType: OrderType.BestMarket, timeInForce: TimeInForce.FAS,
            requestedQuantity: new Quantity(qty), limitPrice: Price.Zero, stopPrice: Price.Zero,
            targetExecutionId: pos.Id, createdAtUtc: _clock.UtcNow);
        await _orderRepo.AddAsync(order);
        order.MarkSubmitted(new OrderId(brokerOrderId), _clock.UtcNow);
        await _orderRepo.UpdateAsync(order);

        var ev = new ExecutionEvent(
            BrokerCode.Kabu, new OrderId(brokerOrderId), new ExecutionId(exitExecId),
            TradeType.ExitOrder, exitSide, pos.Symbol, new Quantity(qty), new Price(exitPrice),
            _clock.UtcNow, TargetPositionId: pos.Id);
        await applier.ApplyAsync(ev);
    }

    // ── PH-U1: 損益の符号と金額 (買建/売建 × 利益/損失) ───────────────

    [Theory]
    [InlineData("Buy", 38000, 38100, +1000)]   // 買建 利益: (38100-38000)*1*1*10
    [InlineData("Buy", 38000, 37900, -1000)]   // 買建 損失
    [InlineData("Sell", 38000, 37900, +1000)]  // 売建 利益: 下げたら +
    [InlineData("Sell", 38000, 38100, -1000)]  // 売建 損失
    public async Task RealizedPnl_SignAndAmount(string sideStr, decimal entry, decimal exit, decimal expected)
    {
        var side = sideStr == "Buy" ? Side.Buy : Side.Sell;
        var pos = await SeedPositionAsync("E1", side, entry, Micro, qty: 1);

        await CloseOnceAsync(NewApplier(), pos, "BO-1", "EX-1", exit, qty: 1);

        var trade = Assert.Single(_closedStore.Trades);
        Assert.Equal(expected, trade.RealizedPnl);
        Assert.Equal(10, trade.ProfitMultiplier);
        Assert.Equal(sideStr, trade.Side);
        Assert.Equal(entry, trade.EntryPrice);
        Assert.Equal(exit, trade.ExitPrice);
    }

    // ── PH-U2: マイクロ倍率 10 / ミニ倍率 100 ──────────────────────

    [Fact]
    public async Task ProfitMultiplier_MicroVsMini()
    {
        var micro = await SeedPositionAsync("E-micro", Side.Buy, 38000, Micro, qty: 1);
        await CloseOnceAsync(NewApplier(), micro, "BO-m", "EX-m", 38100, qty: 1);

        var mini = await SeedPositionAsync("E-mini", Side.Buy, 38000, Mini, qty: 1);
        await CloseOnceAsync(NewApplier(), mini, "BO-n", "EX-n", 38100, qty: 1);

        var microTrade = _closedStore.Trades.Single(t => t.SymbolCode == Micro.Value);
        var miniTrade = _closedStore.Trades.Single(t => t.SymbolCode == Mini.Value);
        Assert.Equal(1000, microTrade.RealizedPnl);    // 100pt * 10
        Assert.Equal(10000, miniTrade.RealizedPnl);    // 100pt * 100
    }

    // ── PH-U3: 3 枚建てを 1 枚ずつ決済 → 同一 EntryExecutionId の 3 レコード ──

    [Fact]
    public async Task SplitClose_ThreeLots_ProducesThreeRecordsSameEntry()
    {
        var pos = await SeedPositionAsync("E-split", Side.Buy, 38000, Micro, qty: 3);
        var applier = NewApplier();

        await CloseOnceAsync(applier, pos, "BO-a", "EX-a", 38100, qty: 1);   // +1000
        await CloseOnceAsync(applier, pos, "BO-b", "EX-b", 38200, qty: 1);   // +2000
        await CloseOnceAsync(applier, pos, "BO-c", "EX-c", 37900, qty: 1);   // -1000

        Assert.Equal(3, _closedStore.Trades.Count);
        Assert.All(_closedStore.Trades, t => Assert.Equal("E-split", t.EntryExecutionId));
        Assert.Equal(2000m, _closedStore.Trades.Sum(t => t.RealizedPnl));    // 建玉合計
    }

    // ── PH-U4: 同一銘柄で 2 戦略同時保有 → 片方を決済 → 当該戦略のみ記録 ──

    [Fact]
    public async Task MultiStrategy_ClosingOne_RecordsOnlyThatStrategy()
    {
        var posA = await SeedPositionAsync("E-A", Side.Buy, 38000, Micro, qty: 1, strategy: "StrategyA", interval: 5);
        await SeedPositionAsync("E-B", Side.Buy, 38000, Micro, qty: 1, strategy: "StrategyB", interval: 15);

        await CloseOnceAsync(NewApplier(), posA, "BO-A", "EX-A", 38100, qty: 1);

        var trade = Assert.Single(_closedStore.Trades);
        Assert.Equal("StrategyA", trade.Strategy);
        Assert.Equal("E-A", trade.EntryExecutionId);
    }
}
