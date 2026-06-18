namespace N225BrokerBridge.Application.Sync;

/// <summary>
/// 自動取引で発生した建玉のメタデータ。
/// kabu API は建玉に「どの戦略から発火されたか」を記憶しないため、
/// ブリッジ側で ExecutionID をキーに別途永続化する。
///
/// 旧 N225OrderBridge の PositionCsvItem 相当 (CSV → JSON 化)。
/// </summary>
public sealed class AutoPositionMetadata
{
    /// <summary>建玉識別子 (kabu の HoldID / ExecutionID)</summary>
    public string ExecutionId { get; set; } = string.Empty;
    public string BrokerCode { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string SymbolCode { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;          // "Buy" / "Sell"
    public DateTime OpenedAt { get; set; }
}

/// <summary>
/// 自動取引建玉メタデータの永続化抽象。
/// </summary>
public interface IAutoPositionMetadataStore
{
    Task<IReadOnlyList<AutoPositionMetadata>> LoadAllAsync(CancellationToken ct = default);
    Task UpsertAsync(AutoPositionMetadata metadata, CancellationToken ct = default);
    Task RemoveAsync(string executionId, CancellationToken ct = default);
    /// <summary>kabu の現状リストと突合して、不要なエントリを一括削除する (起動時整合用)。</summary>
    Task SyncToActiveSetAsync(IEnumerable<string> activeExecutionIds, CancellationToken ct = default);
}
