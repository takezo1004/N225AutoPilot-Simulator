using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Brokers;

/// <summary>
/// 返済発注リクエスト (アダプタ入力)。
/// 対象建玉 (TargetExecutionId) と返済枚数 (Quantity) を必ず指定する。
/// 跨ぎ消化で複数建玉から返済する場合は、Allocation ごとに本リクエストを 1 つ作成する
/// (もしくはアダプタが BulkClosePositionRequest に集約する。将来検討)。
/// </summary>
/// <param name="CorrelationId">Order 集約の内部 Id (応答との突合用)。</param>
/// <param name="Strategy">戦略名 (建玉の Strategy を引き継ぐ)。</param>
/// <param name="Interval">足 (分)。Auto モード時のみ意味を持つ。</param>
/// <param name="TradeMode">自動 / 手動 (返済操作元の区分)。</param>
/// <param name="Symbol">銘柄。</param>
/// <param name="OriginalSide">建玉サイド。実際の発注 Side はアダプタ側で <see cref="SideExtensions.Opposite"/> される。</param>
/// <param name="TargetExecutionId">返済対象の建玉 ID (kabu の HoldID 相当)。</param>
/// <param name="Quantity">返済枚数 (建玉の残量を超えてはならない)。</param>
/// <param name="OrderType">注文タイプ (成行 / 指値 / 対当 / 逆指値)。</param>
/// <param name="TimeInForce">有効期間条件。</param>
/// <param name="LimitPrice">指値価格 (成行では <see cref="Price.Zero"/>)。</param>
/// <param name="StopPrice">逆指値トリガー価格 (逆指値以外では <see cref="Price.Zero"/>)。</param>
public sealed record ClosePositionRequest(
    Guid CorrelationId,
    StrategyName Strategy,
    int Interval,
    TradeMode TradeMode,
    SymbolCode Symbol,
    Side OriginalSide,            // 建玉サイド (返済発注では反対サイドが使われる)
    ExecutionId TargetExecutionId, // 対象建玉
    Quantity Quantity,            // 返済枚数 (建玉残量を超えない)
    OrderType OrderType,
    TimeInForce TimeInForce,
    Price LimitPrice,
    Price StopPrice);
