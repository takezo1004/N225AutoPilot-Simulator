using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Brokers;

/// <summary>
/// ブローカーから取得した注文の現在状態 (照会用)。
/// 起動時の注文一覧取得・定期ポーリングで使う。
/// </summary>
/// <param name="BrokerCode">発注先ブローカー。</param>
/// <param name="BrokerOrderId">ブローカー採番の注文 ID。</param>
/// <param name="State">注文状態。</param>
/// <param name="Symbol">銘柄。</param>
/// <param name="Side">売買サイド。</param>
/// <param name="TradeType">新規 / 返済。</param>
/// <param name="RequestedQuantity">発注枚数。</param>
/// <param name="ExecutedQuantity">既約定枚数 (累計)。</param>
/// <param name="Price">約定価格 (約定済) または指値 (未約定)。成行は 0。</param>
/// <param name="CreatedAt">注文受付時刻。</param>
public sealed record OrderSnapshot(
    BrokerCode BrokerCode,
    OrderId BrokerOrderId,
    OrderState State,
    SymbolCode Symbol,
    Side Side,
    TradeType TradeType,
    Quantity RequestedQuantity,
    Quantity ExecutedQuantity,
    Price Price,                  // 約定価格 (約定済) または指値 (未約定)。成行は 0。
    DateTime CreatedAt);
