using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Brokers;

/// <summary>
/// 約定通知イベント (アダプタからのストリーム出力)。
/// 1 約定 (Fill) ごとに 1 イベント発火される。分割約定なら複数イベントが届く。
/// </summary>
/// <param name="BrokerCode">約定が発生したブローカー。</param>
/// <param name="BrokerOrderId">紐付く注文のブローカー OrderId。</param>
/// <param name="ExecutionId">この約定固有の ID (kabu の HoldID 相当、新規約定なら建玉 ID にもなる)。</param>
/// <param name="TradeType">新規 / 返済。</param>
/// <param name="Side">約定のサイド。</param>
/// <param name="Symbol">銘柄。</param>
/// <param name="Quantity">約定枚数。</param>
/// <param name="Price">約定価格。</param>
/// <param name="ExecutedAt">約定時刻 (UTC)。</param>
/// <param name="TargetPositionId">返済約定の場合、対象建玉の ExecutionId。新規は null。</param>
public sealed record ExecutionEvent(
    BrokerCode BrokerCode,
    OrderId BrokerOrderId,
    ExecutionId ExecutionId,
    TradeType TradeType,
    Side Side,
    SymbolCode Symbol,
    Quantity Quantity,
    Price Price,
    DateTime ExecutedAt,
    ExecutionId? TargetPositionId);
