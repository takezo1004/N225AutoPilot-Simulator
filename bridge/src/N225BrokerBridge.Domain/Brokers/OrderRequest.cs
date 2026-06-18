using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Brokers;

/// <summary>
/// 新規注文発注リクエスト (アダプタ入力)。
/// Order 集約からアダプタ呼び出し時に組み立てる。
/// </summary>
/// <param name="CorrelationId">Order 集約の内部 Id (応答との突合に使う)。</param>
/// <param name="Strategy">戦略名 (手動は "Manual")。</param>
/// <param name="Interval">足 (分)。Auto モード時のみ必須。</param>
/// <param name="TradeMode">自動 / 手動。</param>
/// <param name="Symbol">銘柄。</param>
/// <param name="Side">売買サイド。</param>
/// <param name="OrderType">注文タイプ (成行 / 指値 / 対当 / 逆指値)。</param>
/// <param name="TimeInForce">有効期間条件。</param>
/// <param name="Quantity">発注枚数。</param>
/// <param name="LimitPrice">指値価格 (成行では <see cref="Price.Zero"/>)。</param>
/// <param name="StopPrice">逆指値トリガー価格 (逆指値以外では <see cref="Price.Zero"/>)。</param>
public sealed record OrderRequest(
    Guid CorrelationId,           // Order 集約の内部 Id (応答との突合用)
    StrategyName Strategy,
    int Interval,
    TradeMode TradeMode,
    SymbolCode Symbol,
    Side Side,
    OrderType OrderType,
    TimeInForce TimeInForce,
    Quantity Quantity,
    Price LimitPrice,
    Price StopPrice);
