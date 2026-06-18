using Microsoft.Extensions.Logging.Abstractions;
using N225BrokerBridge.Application.Orders;
using N225BrokerBridge.Application.Positions;
using N225BrokerBridge.Application.Signals;
using N225BrokerBridge.Application.Tests.TestDoubles;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Positions;
using N225BrokerBridge.Domain.ValueObjects;
using N225BrokerBridge.Infrastructure.Persistence;
using Xunit;

namespace N225BrokerBridge.Application.Tests.Positions;

public class DotenUseCaseTests
{
    private readonly FakeBrokerAdapter _broker = new();
    private readonly InMemoryOrderRepository _orderRepo = new();
    private readonly InMemoryPositionRepository _positionRepo = new();
    private readonly FixedDateTimeProvider _clock = new();
    private readonly StubOrderMetadataStore _orderMetaStore = new();
    private readonly StubPendingOrderTracker _pendingTracker = new();

    private DotenUseCase NewDotenUseCase()
    {
        var close = new ClosePositionUseCase(_broker, _orderRepo, _positionRepo, _orderMetaStore, _pendingTracker, _clock,
            NullLogger<ClosePositionUseCase>.Instance);
        var place = new PlaceNewOrderUseCase(_broker, _orderRepo, _orderMetaStore, _pendingTracker, _clock,
            NullLogger<PlaceNewOrderUseCase>.Instance);
        return new DotenUseCase(close, place, NullLogger<DotenUseCase>.Instance);
    }

    private static StrategyName Strategy => new("V7-7-fixed");
    private static SymbolCode Symbol => new("OSE:NK225M1!");

    [Fact]
    public async Task ShortToLongDoten_ClosesShort_AndOpensLong()
    {
        // 既存 Short 建玉 (1 枚)
        var shortPos = new Position(
            id: new ExecutionId("E_SHORT"),
            brokerCode: BrokerCode.Kabu,
            strategy: Strategy,
            interval: 5,
            tradeMode: TradeMode.Auto,
            symbol: Symbol,
            side: Side.Sell,
            initialQuantity: new Quantity(1),
            entryPrice: new Price(38000m),
            openedAtUtc: _clock.UtcNow);
        await _positionRepo.AddAsync(shortPos);

        // ドテン: Short 1 枚返済 + Long 1 枚新規
        var intent = new DotenIntent(
            Strategy, 5, TradeMode.Auto, Symbol,
            OriginalSide: Side.Sell,
            ExitQuantity: new Quantity(1),
            NewQuantity: new Quantity(1),
            OrderPrice: Price.Zero);

        var uc = NewDotenUseCase();
        var result = await uc.ExecuteAsync(intent);

        // 返済 + 新規 で計 2 件の broker 呼び出し
        Assert.Single(_broker.ClosePositionCalls);  // Short 返済
        Assert.Single(_broker.PlaceOrderCalls);     // Long 新規

        var exitCall = _broker.ClosePositionCalls[0];
        Assert.Equal(Side.Sell, exitCall.OriginalSide);
        Assert.Equal(new ExecutionId("E_SHORT"), exitCall.TargetExecutionId);

        var newCall = _broker.PlaceOrderCalls[0];
        Assert.Equal(Side.Buy, newCall.Side);  // Short の反対 = Buy
        Assert.Equal(new Quantity(1), newCall.Quantity);

        Assert.NotNull(result.ExitResult.Plan);
        Assert.Equal(OrderResultStatus.Accepted, result.NewResult.Status);
    }

    [Fact]
    public async Task LongToShortDoten_ClosesLong_AndOpensShort()
    {
        var longPos = new Position(
            id: new ExecutionId("E_LONG"),
            brokerCode: BrokerCode.Kabu,
            strategy: Strategy,
            interval: 5,
            tradeMode: TradeMode.Auto,
            symbol: Symbol,
            side: Side.Buy,
            initialQuantity: new Quantity(1),
            entryPrice: new Price(38000m),
            openedAtUtc: _clock.UtcNow);
        await _positionRepo.AddAsync(longPos);

        var intent = new DotenIntent(
            Strategy, 5, TradeMode.Auto, Symbol,
            OriginalSide: Side.Buy,
            ExitQuantity: new Quantity(1),
            NewQuantity: new Quantity(1),
            OrderPrice: Price.Zero);

        var uc = NewDotenUseCase();
        await uc.ExecuteAsync(intent);

        Assert.Single(_broker.ClosePositionCalls);
        var exitCall = _broker.ClosePositionCalls[0];
        Assert.Equal(Side.Buy, exitCall.OriginalSide);

        Assert.Single(_broker.PlaceOrderCalls);
        var newCall = _broker.PlaceOrderCalls[0];
        Assert.Equal(Side.Sell, newCall.Side);  // Long の反対 = Sell
    }

    [Fact]
    public async Task NoOriginalPosition_OnlyNewOrderIsPlaced()
    {
        // 旧建玉なし、ドテン要求 (理論上は起こらないが防御的テスト)
        var intent = new DotenIntent(
            Strategy, 5, TradeMode.Auto, Symbol,
            OriginalSide: Side.Sell,
            ExitQuantity: new Quantity(1),
            NewQuantity: new Quantity(1),
            OrderPrice: Price.Zero);

        var uc = NewDotenUseCase();
        var result = await uc.ExecuteAsync(intent);

        Assert.Empty(_broker.ClosePositionCalls);          // 返済対象なし
        Assert.Single(_broker.PlaceOrderCalls);            // 新規だけ実行
        Assert.True(result.ExitResult.HasNoMatchingPositions);
    }
}
