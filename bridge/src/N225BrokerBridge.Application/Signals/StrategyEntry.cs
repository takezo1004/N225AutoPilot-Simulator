namespace N225BrokerBridge.Application.Signals;

/// <summary>
/// 戦略エントリー。alert_name + interval で一意。
/// 旧 N225OrderBridge の StrategyViewEntity 相当。
/// IsEnabled が false なら SignalHandler はシグナルを破棄する (旧 IsTrade)。
/// </summary>
public sealed class StrategyEntry
{
    public string AlertName { get; set; } = string.Empty;
    public int Interval { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? LastSignalAt { get; set; }
    public string? Description { get; set; }

    // ── 最終受信シグナルのコンテキスト (旧 StrategyViewEntity の DateTime/TradeType/Side/Price 相当) ──
    /// <summary>最終シグナルの種別表示: "新規" / "返済" / "ドテン" / "—"。</summary>
    public string? LastTradeType { get; set; }
    /// <summary>最終シグナルの売買: "買" / "売"。</summary>
    public string? LastSide { get; set; }
    /// <summary>最終シグナルの価格 (0 は成行)。</summary>
    public decimal? LastPrice { get; set; }
}

/// <summary>
/// 戦略レジストリ抽象。Infrastructure 層で JSON 等に永続化する。
/// </summary>
public interface IStrategyRegistry
{
    /// <summary>現在登録されている戦略一覧。</summary>
    IReadOnlyList<StrategyEntry> GetAll();

    /// <summary>(alert_name, interval) で戦略が有効化されているか判定。</summary>
    bool IsEnabled(string alertName, int interval);

    /// <summary>戦略を追加または更新。永続化する。</summary>
    Task UpsertAsync(StrategyEntry entry, CancellationToken ct = default);

    /// <summary>シグナル受信時刻をマーク (永続化はオプション)。</summary>
    Task MarkSignalReceivedAsync(string alertName, int interval, DateTime atUtc, CancellationToken ct = default);

    /// <summary>
    /// 最終シグナルのコンテキスト (種別 / 売買 / 価格 / 受信時刻) を上書き。
    /// UI の戦略一覧で「最後に何が来たか」を表示するため。
    /// 該当戦略が未登録なら何もしない (旧 MarkSignalReceivedAsync 準拠)。
    /// </summary>
    Task UpdateLastSignalAsync(
        string alertName,
        int interval,
        DateTime atUtc,
        string tradeType,
        string side,
        decimal price,
        CancellationToken ct = default);

    /// <summary>戦略削除。</summary>
    Task RemoveAsync(string alertName, int interval, CancellationToken ct = default);

    /// <summary>レジストリに変更があった時に発火 (UI の自動更新用)。</summary>
    event EventHandler? Changed;
}
