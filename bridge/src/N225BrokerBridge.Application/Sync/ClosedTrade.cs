namespace N225BrokerBridge.Application.Sync;

/// <summary>
/// 決済済みの実現損益 1 件 = ポジション履歴の 1 レコード。
///
/// 1 決済約定 (部分決済を含む) ごとに記録する。分割決済 (3 枚建て → 1 枚ずつ決済) は
/// 同一 <see cref="EntryExecutionId"/> を持つ複数レコードになり、表示側で建玉単位に
/// グルーピングする。<see cref="RealizedPnl"/> は記録時に確定計算して保存し、履歴ファイルを
/// 自己完結させる (生価格も保持するので後から検算可能)。
///
/// 詳細: docs/position-history-spec.md §4-2。
/// </summary>
public sealed class ClosedTrade
{
    /// <summary>建玉識別子 (新規約定の ExecutionId / kabu HoldID)。グルーピング軸。</summary>
    public string EntryExecutionId { get; set; } = string.Empty;

    /// <summary>返済約定 ID。一意・冪等キー (同一 ExitExecutionId の二重記録を防ぐ)。</summary>
    public string ExitExecutionId { get; set; } = string.Empty;

    /// <summary>ブローカーコード ("kabu" 等)。</summary>
    public string BrokerCode { get; set; } = string.Empty;

    /// <summary>戦略名 ("Manual" 含む)。</summary>
    public string Strategy { get; set; } = string.Empty;

    /// <summary>戦略の足 (分)。手動は 0。</summary>
    public int Interval { get; set; }

    /// <summary>"Auto" / "Manual"。</summary>
    public string TradeMode { get; set; } = "Manual";

    /// <summary>銘柄コード (kabu の数値コード)。</summary>
    public string SymbolCode { get; set; } = string.Empty;

    /// <summary>建玉サイド "Buy" / "Sell" (買建 / 売建)。</summary>
    public string Side { get; set; } = string.Empty;

    /// <summary>建値 (取得価格)。</summary>
    public decimal EntryPrice { get; set; }

    /// <summary>返済値 (決済価格)。</summary>
    public decimal ExitPrice { get; set; }

    /// <summary>この決済の枚数。</summary>
    public int Quantity { get; set; }

    /// <summary>損益計算に用いた倍率 (日経225Micro=10 / Mini=100 / Large=1000)。</summary>
    public int ProfitMultiplier { get; set; }

    /// <summary>実現損益 = (返済値 − 建値) × 売買方向 × 枚数 × 倍率。</summary>
    public decimal RealizedPnl { get; set; }

    /// <summary>建玉成立時刻 (UTC)。</summary>
    public DateTime OpenedAt { get; set; }

    /// <summary>返済約定時刻 (UTC)。</summary>
    public DateTime ClosedAt { get; set; }
}

/// <summary>
/// ポジション履歴 (決済済み実現損益) の永続化抽象。追記型 (削除メソッドを持たない)。
/// </summary>
public interface IClosedTradeStore
{
    /// <summary><see cref="ClosedTrade.ExitExecutionId"/> をキーに upsert (冪等)。</summary>
    Task AppendAsync(ClosedTrade trade, CancellationToken ct = default);

    /// <summary>全履歴を読み込む (表示用)。</summary>
    Task<IReadOnlyList<ClosedTrade>> LoadAllAsync(CancellationToken ct = default);
}
