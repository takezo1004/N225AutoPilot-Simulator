using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Brokers;

/// <summary>
/// ブローカーから取得した建玉の現在状態 (照会用)。
/// 起動時の建玉再同期や、定期的なリコンサイル (建玉整合性チェック) で使う。
/// </summary>
/// <param name="BrokerCode">建玉を保有するブローカー。</param>
/// <param name="PositionId">建玉 ID (kabu では ExecutionID と同義)。</param>
/// <param name="Symbol">銘柄。</param>
/// <param name="Side">建玉サイド。</param>
/// <param name="LeaveQuantity">残保有枚数。</param>
/// <param name="HoldQuantity">返済注文中の拘束枚数。</param>
/// <param name="EntryPrice">建値 (取得価格)。</param>
/// <param name="OpenedAt">建玉成立時刻 (UTC)。</param>
public sealed record PositionSnapshot(
    BrokerCode BrokerCode,
    ExecutionId PositionId,
    SymbolCode Symbol,
    Side Side,
    Quantity LeaveQuantity,
    Quantity HoldQuantity,
    Price EntryPrice,
    DateTime OpenedAt);
