using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Brokers;

/// <summary>
/// 注文発注の同期応答 (アダプタ出力)。
/// 受付成功なら BrokerOrderId が必ず設定される。
/// 失敗時は Status と ErrorMessage を確認する。
/// </summary>
/// <param name="CorrelationId">発注リクエストの相関 Id (Order 集約 Id と同じ)。</param>
/// <param name="Status">受付結果 (Accepted / Rejected / NetworkError)。</param>
/// <param name="BrokerOrderId">ブローカー採番の注文 ID (Accepted 時のみ)。</param>
/// <param name="ErrorCode">エラーコード (Rejected/NetworkError 時、例: kabu の 4002017)。</param>
/// <param name="ErrorMessage">エラーメッセージ (kabu の Message 等)。</param>
/// <param name="ReceivedAt">アダプタが応答を受信した時刻 (UTC)。</param>
public sealed record OrderResult(
    Guid CorrelationId,
    OrderResultStatus Status,
    OrderId? BrokerOrderId,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime ReceivedAt);

/// <summary>
/// 注文発注応答のステータス分類。
/// </summary>
public enum OrderResultStatus
{
    /// <summary>正常受付。約定通知は別途 ExecutionStream から届く。</summary>
    Accepted,
    /// <summary>ブローカーが受付拒否。</summary>
    Rejected,
    /// <summary>通信失敗・タイムアウト等。発注済みか不明なケースを含む (要再照会)。</summary>
    NetworkError
}
