namespace N225BrokerBridge.Application.Sync;

/// <summary>
/// このブリッジから発注した注文のメタデータ。
///
/// kabu API は注文 (/orders) に「どの戦略から / 手動か自動か」を持たないため、
/// ブリッジ側で BrokerOrderId をキーに別途永続化する。
///
/// 旧 N225OrderBridge の OrderCsvItem (order.csv) 相当 (CSV → JSON 化)。
/// 用途:
///   - 起動時に kabu /orders と突合 → 注文一覧の TradeMode / Strategy / Interval を復元
///   - 約定通知時に内部 Order と紐付けて建玉メタデータ生成 (OrderMetadata は履歴的に残す)
/// </summary>
public sealed class OrderMetadata
{
    /// <summary>kabu のブローカー OrderID (kabusapi/orders の ID フィールド)。</summary>
    public string BrokerOrderId { get; set; } = string.Empty;

    /// <summary>ブローカーコード ("kabu" 等)。</summary>
    public string BrokerCode { get; set; } = string.Empty;

    /// <summary>戦略名 ("Manual" / "V7-7-fixed" 等)。</summary>
    public string Strategy { get; set; } = string.Empty;

    /// <summary>戦略の時間足 (分)。手動は 0。</summary>
    public int Interval { get; set; }

    /// <summary>"Auto" / "Manual"。</summary>
    public string TradeMode { get; set; } = "Manual";

    /// <summary>銘柄コード (kabu の Symbol)。</summary>
    public string SymbolCode { get; set; } = string.Empty;

    /// <summary>"Buy" / "Sell"。</summary>
    public string Side { get; set; } = string.Empty;

    /// <summary>"NewOrder" / "ExitOrder"。</summary>
    public string TradeType { get; set; } = string.Empty;

    /// <summary>返済注文の場合、対象建玉の ExecutionID。新規は null。</summary>
    public string? TargetExecutionId { get; set; }

    /// <summary>発注時刻 (UTC)。古いエントリのクリーンアップ判定にも使う。</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 注文メタデータの永続化抽象。
/// 旧 N225OrderBridge の order.csv (OrderManager.ToCsv / CsvRead) 相当。
/// </summary>
public interface IOrderMetadataStore
{
    Task<IReadOnlyList<OrderMetadata>> LoadAllAsync(CancellationToken ct = default);
    Task UpsertAsync(OrderMetadata metadata, CancellationToken ct = default);
    Task RemoveAsync(string brokerOrderId, CancellationToken ct = default);

    /// <summary>同期取得 (UI 表示用、毎秒呼ばれる /orders 突合時のホットパス)。</summary>
    OrderMetadata? TryGet(string brokerOrderId);
}
