using Microsoft.Extensions.Logging.Abstractions;
using N225BrokerBridge.Application.Orders;
using N225BrokerBridge.Application.Positions;
using N225BrokerBridge.Application.Signals;
using N225BrokerBridge.Application.Tests.TestDoubles;
using N225BrokerBridge.Domain.ValueObjects;
using N225BrokerBridge.Infrastructure.Persistence;
using Xunit;

namespace N225BrokerBridge.Application.Tests.Signals;

public class SignalHandlerTests
{
    private readonly FakeBrokerAdapter _broker = new();
    private readonly InMemoryOrderRepository _orderRepo = new();
    private readonly InMemoryPositionRepository _positionRepo = new();
    private readonly FixedDateTimeProvider _clock = new();

    private readonly StubStrategyRegistry _strategyRegistry = new();
    private readonly StubOrderMetadataStore _orderMetaStore = new();
    private readonly StubPendingOrderTracker _pendingTracker = new();

    private readonly AutoTradeGate _autoTradeGate = new() { IsEnabled = true };
    // テスト時の発注先銘柄コード (本番では UI 選択中の銘柄を IAutoTradeInstrumentProvider が供給)
    private readonly AutoTradeInstrumentProvider _instrumentProvider;

    public SignalHandlerTests()
    {
        _instrumentProvider = new AutoTradeInstrumentProvider();
        _instrumentProvider.SetInstrument("161060023", "日経225Micro", "2026年6月限");
    }

    private SignalHandler BuildHandler(string? configuredPassphrase = null)
    {
        var auth = new ConfiguredSignalAuthenticator(configuredPassphrase);
        var place = new PlaceNewOrderUseCase(_broker, _orderRepo, _orderMetaStore, _pendingTracker, _clock,
            NullLogger<PlaceNewOrderUseCase>.Instance);
        var close = new ClosePositionUseCase(_broker, _orderRepo, _positionRepo, _orderMetaStore, _pendingTracker, _clock,
            NullLogger<ClosePositionUseCase>.Instance);
        var doten = new DotenUseCase(close, place, NullLogger<DotenUseCase>.Instance);
        return new SignalHandler(auth, _autoTradeGate, _instrumentProvider, _strategyRegistry, place, close, doten,
            NullLogger<SignalHandler>.Instance);
    }

    private static SignalPayload Payload(
        string action = "buy", string prev = "flat", string current = "long",
        int qty = 3, string? passphrase = null)
        => new("V7-7-fixed", 5, action, current, prev, qty, qty, 0, 0m,
            "OSE:NK225M1!", passphrase);

    // ── 認証 ───────────────────────────────────────────────────

    [Fact]
    public async Task NoConfiguredPassphrase_AlwaysAuthenticated()
    {
        var h = BuildHandler(configuredPassphrase: null);
        var outcome = await h.HandleAsync(Payload(passphrase: null), TradeMode.Auto);
        Assert.IsType<SignalHandleOutcome.NewOrderDispatched_>(outcome);
    }

    [Fact]
    public async Task ConfiguredPassphrase_Mismatch_Rejected()
    {
        var h = BuildHandler(configuredPassphrase: "secret");
        var outcome = await h.HandleAsync(Payload(passphrase: "wrong"), TradeMode.Auto);
        Assert.IsType<SignalHandleOutcome.Authenticated_Failed>(outcome);
        Assert.Empty(_broker.PlaceOrderCalls);
    }

    [Fact]
    public async Task ConfiguredPassphrase_Match_Accepted()
    {
        var h = BuildHandler(configuredPassphrase: "secret");
        var outcome = await h.HandleAsync(Payload(passphrase: "secret"), TradeMode.Auto);
        Assert.IsType<SignalHandleOutcome.NewOrderDispatched_>(outcome);
    }

    // ── 振り分け ───────────────────────────────────────────────

    [Fact]
    public async Task NewOrderIntent_DispatchedToPlaceNewOrder()
    {
        var h = BuildHandler();
        var outcome = await h.HandleAsync(
            Payload(action: "buy", prev: "flat", current: "long", qty: 3),
            TradeMode.Auto);
        var dispatched = Assert.IsType<SignalHandleOutcome.NewOrderDispatched_>(outcome);
        Assert.Single(_broker.PlaceOrderCalls);
        Assert.Empty(_broker.ClosePositionCalls);
    }

    [Fact]
    public async Task ExitOrderIntent_DispatchedToClosePosition()
    {
        // 既存建玉を仕込む
        var pos = new Domain.Positions.Position(
            new ExecutionId("E1"), BrokerCode.Kabu, new StrategyName("V7-7-fixed"), 5,
            TradeMode.Auto, new SymbolCode("OSE:NK225M1!"), Side.Buy,
            new Quantity(3), new Price(38000m), _clock.UtcNow);
        await _positionRepo.AddAsync(pos);

        var h = BuildHandler();
        var outcome = await h.HandleAsync(
            Payload(action: "sell", prev: "long", current: "flat", qty: 3),
            TradeMode.Auto);
        Assert.IsType<SignalHandleOutcome.ExitOrderDispatched_>(outcome);
        Assert.Single(_broker.ClosePositionCalls);
        Assert.Empty(_broker.PlaceOrderCalls);
    }

    [Fact]
    public async Task DotenIntent_DispatchedToDoten()
    {
        // 既存 Short 建玉を仕込む
        var pos = new Domain.Positions.Position(
            new ExecutionId("E1"), BrokerCode.Kabu, new StrategyName("V7-7-fixed"), 5,
            TradeMode.Auto, new SymbolCode("OSE:NK225M1!"), Side.Sell,
            new Quantity(1), new Price(38000m), _clock.UtcNow);
        await _positionRepo.AddAsync(pos);

        var h = BuildHandler();
        var outcome = await h.HandleAsync(
            new SignalPayload(
                "V7-7-fixed", 5, "buy", "long", "short",
                OrderContracts: 2, MarketPositionSize: 1, PrevMarketPositionSize: 1,
                OrderPrice: 0m, SymbolTicker: "OSE:NK225M1!", Passphrase: null),
            TradeMode.Auto);

        Assert.IsType<SignalHandleOutcome.DotenDispatched_>(outcome);
        Assert.Single(_broker.ClosePositionCalls);   // Short 返済
        Assert.Single(_broker.PlaceOrderCalls);      // Long 新規
    }

    [Fact]
    public async Task IgnoreIntent_NoDispatch()
    {
        var h = BuildHandler();
        var outcome = await h.HandleAsync(
            Payload(action: "buy", prev: "flat", current: "flat", qty: 1),  // 遷移なし
            TradeMode.Auto);
        Assert.IsType<SignalHandleOutcome.Ignored_>(outcome);
        Assert.Empty(_broker.PlaceOrderCalls);
        Assert.Empty(_broker.ClosePositionCalls);
    }

    [Fact]
    public async Task InvalidPayload_InterpretationFailed()
    {
        var h = BuildHandler();
        var outcome = await h.HandleAsync(
            new SignalPayload(
                AlertName: "X", Interval: 5,
                OrderAction: "hold",  // 不明
                MarketPosition: "long", PrevMarketPosition: "flat",
                OrderContracts: 1, MarketPositionSize: 1, PrevMarketPositionSize: 0,
                OrderPrice: 0m, SymbolTicker: "S", Passphrase: null),
            TradeMode.Auto);
        Assert.IsType<SignalHandleOutcome.Interpretation_Failed>(outcome);
    }

    // ── 銘柄注入 (TV ティッカーではなく provider 値を使う) ──────────

    [Fact]
    public async Task NewOrder_UsesProviderSymbol_NotPayloadTicker()
    {
        // payload.SymbolTicker は TV のミニ ("NK225M1!") だが、ブリッジで選択中は Micro 想定。
        // 発注 Symbol は provider 由来 ("161060023") にならなければならない。
        var h = BuildHandler();
        await h.HandleAsync(
            Payload(action: "buy", prev: "flat", current: "long", qty: 1),
            TradeMode.Auto);

        Assert.Single(_broker.PlaceOrderCalls);
        var call = _broker.PlaceOrderCalls[0];
        Assert.Equal("161060023", call.Symbol.Value);
    }

    [Fact]
    public async Task ProviderUnresolved_SignalIgnored_NoBrokerCall()
    {
        // 起動直後 (kabu の現月解決前) は ResolvedSymbolCode が null。
        // この間シグナルが来ても発注は走らないことを保証する (安全側)。
        _instrumentProvider.SetInstrument(null, null, null);

        var h = BuildHandler();
        var outcome = await h.HandleAsync(
            Payload(action: "buy", prev: "flat", current: "long", qty: 1),
            TradeMode.Auto);

        var ignored = Assert.IsType<SignalHandleOutcome.Ignored_>(outcome);
        Assert.Contains("未解決", ignored.Reason);
        Assert.Empty(_broker.PlaceOrderCalls);
        Assert.Empty(_broker.ClosePositionCalls);
    }
}
