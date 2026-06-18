using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Application.Signals;

/// <summary>
/// <see cref="SignalPayload"/> を <see cref="SignalIntent"/> に解釈するドメインロジック。
///
/// 現 N225OrderBridge の AutoOrderfiledFactory.cs に相当する routing ロジックをここに集約する。
/// 純粋関数として作る (副作用なし、I/O なし)。
///
/// 遷移パターン:
///   (prev, current, action) → Intent
///   (flat, long,  buy ) → NewOrder Buy
///   (flat, short, sell) → NewOrder Sell
///   (long, flat,  sell) → ExitOrder (Long 全量返済)
///   (short,flat,  buy ) → ExitOrder (Short 全量返済)
///   (long, long,  sell) → ExitOrder (Long 部分返済)
///   (short,short, buy ) → ExitOrder (Short 部分返済)
///   (short,long,  buy ) → Doten     (Short → Long)
///   (long, short, sell) → Doten     (Long → Short)
///   その他              → Ignore (未定義 / 反対側のアクション等)
/// </summary>
public static class SignalInterpreter
{
    /// <summary>
    /// payload と「発注先銘柄コード」を組み合わせて SignalIntent を生成する。
    ///
    /// ⚠️ 運用上の注意 (絶対遵守):
    ///   - 銘柄は payload.SymbolTicker (TV ティッカー、例: "OSE:NK225M1!") を使わない。
    ///     呼び出し側 (SignalHandler) が IAutoTradeInstrumentProvider から取得した
    ///     **kabu の数値銘柄コード** (例: "161060023") を symbol パラメータで渡す。
    ///   - これは TV のチャート銘柄と発注先銘柄が異なる運用 (TV=Mini / 発注=Micro 等) を
    ///     許容するための設計判断。Pine 戦略の symbol を変えずに kabu 側の発注銘柄だけ切り替えたい
    ///     利用者がいるため (TV プラン制約や口座資金の都合)。
    ///   - 将来 "戦略ごとに別銘柄に発注したい" 要求が出たら、本シグネチャ自体は変えず、
    ///     呼び出し側で「戦略 → 銘柄」マップを引いた結果を symbol に渡す形にすれば拡張できる。
    /// </summary>
    /// <param name="payload">外部 (TV Webhook 等) のシグナル素データ。SymbolTicker は読まない。</param>
    /// <param name="tradeMode">取引モード (Auto/Manual)。</param>
    /// <param name="symbol">発注先銘柄コード (ブリッジで選択中の Resolved Symbol Code = kabu 数値コード)。</param>
    public static SignalIntent Interpret(SignalPayload payload, TradeMode tradeMode, SymbolCode symbol)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(symbol);

        if (string.IsNullOrWhiteSpace(payload.AlertName))
            throw new InvalidValueObjectException("AlertName must not be empty.");

        var strategy = new StrategyName(payload.AlertName);
        var prev = MarketPositionStateExtensions.Parse(payload.PrevMarketPosition);
        var current = MarketPositionStateExtensions.Parse(payload.MarketPosition);
        var action = ParseAction(payload.OrderAction);
        var price = new Price(payload.OrderPrice < 0 ? 0m : payload.OrderPrice);

        // ── 新規 ─────────────────────────────────────────
        if (prev == MarketPositionState.Flat && current == MarketPositionState.Long && action == Side.Buy)
        {
            return new NewOrderIntent(strategy, payload.Interval, tradeMode, symbol,
                Side.Buy, RequireQty(payload.OrderContracts, "OrderContracts"), price);
        }
        if (prev == MarketPositionState.Flat && current == MarketPositionState.Short && action == Side.Sell)
        {
            return new NewOrderIntent(strategy, payload.Interval, tradeMode, symbol,
                Side.Sell, RequireQty(payload.OrderContracts, "OrderContracts"), price);
        }

        // ── 返済 (全量 or 部分) ──────────────────────────
        if (prev == MarketPositionState.Long && current == MarketPositionState.Flat && action == Side.Sell)
        {
            return new ExitOrderIntent(strategy, payload.Interval, tradeMode, symbol,
                OriginalSide: Side.Buy,
                Quantity: RequireQty(payload.OrderContracts, "OrderContracts"), price);
        }
        if (prev == MarketPositionState.Short && current == MarketPositionState.Flat && action == Side.Buy)
        {
            return new ExitOrderIntent(strategy, payload.Interval, tradeMode, symbol,
                OriginalSide: Side.Sell,
                Quantity: RequireQty(payload.OrderContracts, "OrderContracts"), price);
        }
        if (prev == MarketPositionState.Long && current == MarketPositionState.Long && action == Side.Sell)
        {
            // 部分返済 (long → long、サイズ減)
            return new ExitOrderIntent(strategy, payload.Interval, tradeMode, symbol,
                OriginalSide: Side.Buy,
                Quantity: RequireQty(payload.OrderContracts, "OrderContracts"), price);
        }
        if (prev == MarketPositionState.Short && current == MarketPositionState.Short && action == Side.Buy)
        {
            // 部分返済 (short → short、サイズ減)
            return new ExitOrderIntent(strategy, payload.Interval, tradeMode, symbol,
                OriginalSide: Side.Sell,
                Quantity: RequireQty(payload.OrderContracts, "OrderContracts"), price);
        }

        // ── ドテン ───────────────────────────────────────
        if (prev == MarketPositionState.Short && current == MarketPositionState.Long && action == Side.Buy)
        {
            return new DotenIntent(strategy, payload.Interval, tradeMode, symbol,
                OriginalSide: Side.Sell,
                ExitQuantity: RequireQty(payload.PrevMarketPositionSize, "PrevMarketPositionSize"),
                NewQuantity: RequireQty(payload.MarketPositionSize, "MarketPositionSize"),
                price);
        }
        if (prev == MarketPositionState.Long && current == MarketPositionState.Short && action == Side.Sell)
        {
            return new DotenIntent(strategy, payload.Interval, tradeMode, symbol,
                OriginalSide: Side.Buy,
                ExitQuantity: RequireQty(payload.PrevMarketPositionSize, "PrevMarketPositionSize"),
                NewQuantity: RequireQty(payload.MarketPositionSize, "MarketPositionSize"),
                price);
        }

        // ── その他 → Ignore ─────────────────────────────
        return new IgnoreIntent(strategy, payload.Interval, tradeMode, symbol,
            Reason: $"Unhandled transition: prev={prev}, current={current}, action={action}");
    }

    private static Side ParseAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new InvalidValueObjectException("OrderAction must not be empty.");

        return action.Trim().ToLowerInvariant() switch
        {
            "buy" => Side.Buy,
            "sell" => Side.Sell,
            _ => throw new InvalidValueObjectException($"Unknown OrderAction: '{action}'")
        };
    }

    private static Quantity RequireQty(int raw, string fieldName)
    {
        if (raw <= 0)
            throw new InvalidValueObjectException($"{fieldName} must be positive. Got: {raw}");
        return new Quantity(raw);
    }
}
