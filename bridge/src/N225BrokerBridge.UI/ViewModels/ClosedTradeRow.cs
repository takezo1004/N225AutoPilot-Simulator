using System.Windows.Media;

namespace N225BrokerBridge.UI.ViewModels;

/// <summary>
/// ポジション履歴画面の 1 行 = 決済 1 件 (分割決済は同一 <see cref="TradeNo"/> の複数行)。
/// 表示用の整形済みプロパティを持つ読み取り専用ビューモデル。
/// 詳細: docs/position-history-spec.md §3。
/// </summary>
public sealed class ClosedTradeRow
{
    /// <summary>建玉番号 (同一建玉の分割決済は同じ番号。新しい建玉ほど大きい)。</summary>
    public int TradeNo { get; init; }

    public string Strategy { get; init; } = string.Empty;

    /// <summary>"自動" / "手動"。</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>"買建" / "売建"。</summary>
    public string SideText { get; init; } = string.Empty;

    /// <summary>"Micro" / "Mini" / "Large" / コード (倍率から導出)。</summary>
    public string Instrument { get; init; } = string.Empty;

    /// <summary>建玉日時 (ローカル, "MM/dd HH:mm")。</summary>
    public string OpenedText { get; init; } = string.Empty;

    public decimal EntryPrice { get; init; }

    /// <summary>決済日時 (ローカル, "MM/dd HH:mm")。</summary>
    public string ClosedText { get; init; } = string.Empty;

    public decimal ExitPrice { get; init; }

    public int Quantity { get; init; }

    /// <summary>この決済の実現損益。</summary>
    public decimal RealizedPnl { get; init; }

    /// <summary>表示順 (時系列) の累積損益。</summary>
    public decimal Cumulative { get; init; }

    /// <summary>実現損益の表示色 (＋=緑 / −=赤 / 0=既定)。</summary>
    public Brush PnlBrush => BrushFor(RealizedPnl);

    public Brush CumulativeBrush => BrushFor(Cumulative);

    private static readonly Brush Profit = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly Brush Loss = new SolidColorBrush(Color.FromRgb(0xE5, 0x73, 0x73));
    private static readonly Brush Neutral = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

    private static Brush BrushFor(decimal v) => v > 0 ? Profit : v < 0 ? Loss : Neutral;
}
