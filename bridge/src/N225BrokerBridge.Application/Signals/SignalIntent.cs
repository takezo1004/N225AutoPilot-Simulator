using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Application.Signals;

/// <summary>
/// シグナル解釈の結果。判別共用体 (discriminated union) パターンで 4 種を表現する。
/// 呼び出し側は switch 式でパターンマッチして処理を分岐する。
/// </summary>
public abstract record SignalIntent
{
    public StrategyName Strategy { get; }
    public int Interval { get; }
    public TradeMode TradeMode { get; }
    public SymbolCode Symbol { get; }

    protected SignalIntent(StrategyName strategy, int interval, TradeMode tradeMode, SymbolCode symbol)
    {
        Strategy = strategy;
        Interval = interval;
        TradeMode = tradeMode;
        Symbol = symbol;
    }
}

/// <summary>
/// 新規建ての意図 (flat → long/short)。
/// </summary>
public sealed record NewOrderIntent(
    StrategyName Strategy,
    int Interval,
    TradeMode TradeMode,
    SymbolCode Symbol,
    Side Side,
    Quantity Quantity,
    Price OrderPrice,
    OrderType OrderType = OrderType.BestMarket,
    TimeInForce TimeInForce = TimeInForce.FAS,
    // 逆指値 (OrderType.Stop) 時のトリガー価格。それ以外は Price.Zero。
    Price? StopPrice = null)
    : SignalIntent(Strategy, Interval, TradeMode, Symbol);

/// <summary>
/// 返済の意図 (long → flat/long-reduced、short → flat/short-reduced)。
/// 部分返済時は Quantity が建玉合計より小さい。
/// </summary>
public sealed record ExitOrderIntent(
    StrategyName Strategy,
    int Interval,
    TradeMode TradeMode,
    SymbolCode Symbol,
    Side OriginalSide,             // 返済対象の建玉サイド (Long建玉なら Buy)
    Quantity Quantity,             // 返済要求枚数 (order_contracts)
    Price OrderPrice)
    : SignalIntent(Strategy, Interval, TradeMode, Symbol);

/// <summary>
/// ドテン (反対転換) の意図 (long → short、short → long)。
/// 旧建玉の全量返済 + 新建玉発注の 2 アクションに分解する。
/// </summary>
public sealed record DotenIntent(
    StrategyName Strategy,
    int Interval,
    TradeMode TradeMode,
    SymbolCode Symbol,
    Side OriginalSide,             // 旧建玉サイド (返済される側)
    Quantity ExitQuantity,         // 返済枚数 (prev_market_position_size)
    Quantity NewQuantity,          // 新規枚数 (market_position_size)
    Price OrderPrice)
    : SignalIntent(Strategy, Interval, TradeMode, Symbol);

/// <summary>
/// 未定義の遷移などで処理対象外と判定した場合 (理由付き)。
/// </summary>
public sealed record IgnoreIntent(
    StrategyName Strategy,
    int Interval,
    TradeMode TradeMode,
    SymbolCode Symbol,
    string Reason)
    : SignalIntent(Strategy, Interval, TradeMode, Symbol);
