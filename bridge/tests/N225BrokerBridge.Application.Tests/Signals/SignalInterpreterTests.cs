using N225BrokerBridge.Application.Signals;
using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;
using Xunit;

namespace N225BrokerBridge.Application.Tests.Signals;

public class SignalInterpreterTests
{
    // テスト用の発注先銘柄コード (kabu の数値銘柄コード相当)。
    // 本番では IAutoTradeInstrumentProvider が供給する。
    private static readonly SymbolCode TestSymbol = new("161060023");

    private static SignalPayload MakePayload(
        string action = "buy",
        string prev = "flat",
        string current = "long",
        int orderContracts = 3,
        int marketPositionSize = 3,
        int prevMarketPositionSize = 0,
        decimal orderPrice = 0m,
        string alert = "V7-7-fixed",
        int interval = 5,
        string ticker = "OSE:NK225M1!")
        => new(
            AlertName: alert,
            Interval: interval,
            OrderAction: action,
            MarketPosition: current,
            PrevMarketPosition: prev,
            OrderContracts: orderContracts,
            MarketPositionSize: marketPositionSize,
            PrevMarketPositionSize: prevMarketPositionSize,
            OrderPrice: orderPrice,
            SymbolTicker: ticker,
            Passphrase: null);

    // ── 新規 ────────────────────────────────────────────────────

    [Fact]
    public void Flat_To_Long_Buy_IsNewBuyOrder()
    {
        var intent = SignalInterpreter.Interpret(
            MakePayload(action: "buy", prev: "flat", current: "long", orderContracts: 3),
            TradeMode.Auto, TestSymbol);
        var newOrder = Assert.IsType<NewOrderIntent>(intent);
        Assert.Equal(Side.Buy, newOrder.Side);
        Assert.Equal(new Quantity(3), newOrder.Quantity);
        Assert.Equal(new StrategyName("V7-7-fixed"), newOrder.Strategy);
    }

    [Fact]
    public void Flat_To_Short_Sell_IsNewSellOrder()
    {
        var intent = SignalInterpreter.Interpret(
            MakePayload(action: "sell", prev: "flat", current: "short", orderContracts: 2),
            TradeMode.Auto, TestSymbol);
        var newOrder = Assert.IsType<NewOrderIntent>(intent);
        Assert.Equal(Side.Sell, newOrder.Side);
        Assert.Equal(new Quantity(2), newOrder.Quantity);
    }

    // ── 全量返済 ────────────────────────────────────────────────

    [Fact]
    public void Long_To_Flat_Sell_IsFullExitFromLong()
    {
        var intent = SignalInterpreter.Interpret(
            MakePayload(action: "sell", prev: "long", current: "flat", orderContracts: 3),
            TradeMode.Auto, TestSymbol);
        var exit = Assert.IsType<ExitOrderIntent>(intent);
        Assert.Equal(Side.Buy, exit.OriginalSide);
        Assert.Equal(new Quantity(3), exit.Quantity);
    }

    [Fact]
    public void Short_To_Flat_Buy_IsFullExitFromShort()
    {
        var intent = SignalInterpreter.Interpret(
            MakePayload(action: "buy", prev: "short", current: "flat", orderContracts: 1),
            TradeMode.Auto, TestSymbol);
        var exit = Assert.IsType<ExitOrderIntent>(intent);
        Assert.Equal(Side.Sell, exit.OriginalSide);
        Assert.Equal(new Quantity(1), exit.Quantity);
    }

    // ── 部分返済 (V7-7 fixed の主要シナリオ) ─────────────────────

    [Fact]
    public void Long_To_Long_Sell_IsPartialExitFromLong()
    {
        // V7-7: 3 枚建てて 1 枚部分返済
        var intent = SignalInterpreter.Interpret(
            MakePayload(action: "sell", prev: "long", current: "long",
                orderContracts: 1, marketPositionSize: 2, prevMarketPositionSize: 3),
            TradeMode.Auto, TestSymbol);
        var exit = Assert.IsType<ExitOrderIntent>(intent);
        Assert.Equal(Side.Buy, exit.OriginalSide);
        Assert.Equal(new Quantity(1), exit.Quantity);
    }

    [Fact]
    public void Short_To_Short_Buy_IsPartialExitFromShort()
    {
        var intent = SignalInterpreter.Interpret(
            MakePayload(action: "buy", prev: "short", current: "short",
                orderContracts: 1, marketPositionSize: 2, prevMarketPositionSize: 3),
            TradeMode.Auto, TestSymbol);
        var exit = Assert.IsType<ExitOrderIntent>(intent);
        Assert.Equal(Side.Sell, exit.OriginalSide);
        Assert.Equal(new Quantity(1), exit.Quantity);
    }

    // ── ドテン ──────────────────────────────────────────────────

    [Fact]
    public void Short_To_Long_Buy_IsDoten()
    {
        var intent = SignalInterpreter.Interpret(
            MakePayload(action: "buy", prev: "short", current: "long",
                orderContracts: 2, marketPositionSize: 1, prevMarketPositionSize: 1),
            TradeMode.Auto, TestSymbol);
        var doten = Assert.IsType<DotenIntent>(intent);
        Assert.Equal(Side.Sell, doten.OriginalSide);
        Assert.Equal(new Quantity(1), doten.ExitQuantity);  // 旧 Short 1 枚返済
        Assert.Equal(new Quantity(1), doten.NewQuantity);   // 新規 Long 1 枚
    }

    [Fact]
    public void Long_To_Short_Sell_IsDoten()
    {
        var intent = SignalInterpreter.Interpret(
            MakePayload(action: "sell", prev: "long", current: "short",
                orderContracts: 2, marketPositionSize: 1, prevMarketPositionSize: 1),
            TradeMode.Auto, TestSymbol);
        var doten = Assert.IsType<DotenIntent>(intent);
        Assert.Equal(Side.Buy, doten.OriginalSide);
        Assert.Equal(new Quantity(1), doten.ExitQuantity);
        Assert.Equal(new Quantity(1), doten.NewQuantity);
    }

    // ── Ignore ──────────────────────────────────────────────────

    [Theory]
    [InlineData("flat", "long", "sell")]   // flat→long なのに sell (矛盾)
    [InlineData("long", "flat", "buy")]    // long を閉じるなら sell のはず
    [InlineData("flat", "flat", "buy")]    // 遷移なし
    public void InvalidTransition_IsIgnore(string prev, string current, string action)
    {
        var intent = SignalInterpreter.Interpret(
            MakePayload(action: action, prev: prev, current: current),
            TradeMode.Auto, TestSymbol);
        var ignore = Assert.IsType<IgnoreIntent>(intent);
        Assert.Contains("Unhandled transition", ignore.Reason);
    }

    // ── バリデーション ──────────────────────────────────────────

    [Fact]
    public void EmptyAlertName_Throws()
    {
        var payload = MakePayload(alert: "");
        Assert.Throws<InvalidValueObjectException>(() =>
            SignalInterpreter.Interpret(payload, TradeMode.Auto, TestSymbol));
    }

    [Fact]
    public void UnknownOrderAction_Throws()
    {
        var payload = MakePayload(action: "hold");
        Assert.Throws<InvalidValueObjectException>(() =>
            SignalInterpreter.Interpret(payload, TradeMode.Auto, TestSymbol));
    }

    [Fact]
    public void UnknownMarketPosition_Throws()
    {
        var payload = MakePayload(prev: "neutral");
        Assert.Throws<InvalidValueObjectException>(() =>
            SignalInterpreter.Interpret(payload, TradeMode.Auto, TestSymbol));
    }

    [Fact]
    public void ZeroOrderContracts_Throws()
    {
        var payload = MakePayload(orderContracts: 0);
        Assert.Throws<InvalidValueObjectException>(() =>
            SignalInterpreter.Interpret(payload, TradeMode.Auto, TestSymbol));
    }

    // ── TradeMode 反映 ─────────────────────────────────────────

    [Fact]
    public void TradeMode_Manual_IsCarriedThrough()
    {
        var intent = SignalInterpreter.Interpret(MakePayload(), TradeMode.Manual, TestSymbol);
        Assert.Equal(TradeMode.Manual, intent.TradeMode);
    }

    [Fact]
    public void TradeMode_Auto_IsCarriedThrough()
    {
        var intent = SignalInterpreter.Interpret(MakePayload(), TradeMode.Auto, TestSymbol);
        Assert.Equal(TradeMode.Auto, intent.TradeMode);
    }

    // ── 価格負値の正規化 ───────────────────────────────────────

    [Fact]
    public void NegativeOrderPrice_ClampedToZero()
    {
        // ※ Price は非負制約あり。OrderPrice が負の値で来た場合は 0 にする (成行扱い)
        var intent = SignalInterpreter.Interpret(MakePayload(orderPrice: -1m), TradeMode.Auto, TestSymbol);
        var newOrder = Assert.IsType<NewOrderIntent>(intent);
        Assert.Equal(Price.Zero, newOrder.OrderPrice);
    }

    // ── 銘柄注入 (TV ティッカーは無視、引数の SymbolCode を使う) ───

    [Fact]
    public void SymbolFromParameter_OverridesPayloadTicker()
    {
        // payload には TV ティッカー "NK225M1!" が入っているが、interpret には
        // 別の symbol (kabu の数値銘柄コード) を渡している。Intent はその symbol を使う。
        var symbol = new SymbolCode("161060023");
        var intent = SignalInterpreter.Interpret(
            MakePayload(ticker: "OSE:NK225M1!"),
            TradeMode.Auto, symbol);
        Assert.Equal(symbol, intent.Symbol);
    }
}
