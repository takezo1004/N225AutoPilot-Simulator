using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Application.Sync;

namespace N225BrokerBridge.UI.ViewModels;

/// <summary>
/// ポジション履歴画面のビューモデル。<see cref="IClosedTradeStore"/> から決済履歴を読み込み、
/// モード/期間/戦略でフィルタし、建玉番号・累積損益・サマリーを計算して表示する。
/// 詳細: docs/position-history-spec.md §3-4。
/// </summary>
public sealed partial class PositionHistoryViewModel : ObservableObject
{
    private readonly IClosedTradeStore _store;
    private readonly ILogger<PositionHistoryViewModel> _logger;
    private IReadOnlyList<ClosedTrade> _all = Array.Empty<ClosedTrade>();

    public ObservableCollection<ClosedTradeRow> Rows { get; } = new();

    public ObservableCollection<string> ModeOptions { get; } = new() { "全部", "自動のみ", "手動のみ" };
    public ObservableCollection<string> PeriodOptions { get; } = new() { "全期間", "当日", "今週", "今月" };
    public ObservableCollection<string> StrategyOptions { get; } = new() { "全部" };

    [ObservableProperty] private string _selectedMode = "全部";
    [ObservableProperty] private string _selectedPeriod = "全期間";
    [ObservableProperty] private string _selectedStrategy = "全部";

    [ObservableProperty] private string _summaryText = "履歴を読み込み中...";
    [ObservableProperty] private Brush _summaryBrush = Neutral;

    private static readonly Brush Profit = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly Brush Loss = new SolidColorBrush(Color.FromRgb(0xE5, 0x73, 0x73));
    private static readonly Brush Neutral = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

    public PositionHistoryViewModel(IClosedTradeStore store, ILogger<PositionHistoryViewModel> logger)
    {
        _store = store;
        _logger = logger;
    }

    partial void OnSelectedModeChanged(string value) => Rebuild();
    partial void OnSelectedPeriodChanged(string value) => Rebuild();
    partial void OnSelectedStrategyChanged(string value) => Rebuild();

    /// <summary>ストアから全履歴を読み込み、戦略フィルタ候補を更新して再構築する。</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            _all = await _store.LoadAllAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ポジション履歴の読込に失敗");
            _all = Array.Empty<ClosedTrade>();
        }

        var keep = SelectedStrategy;
        StrategyOptions.Clear();
        StrategyOptions.Add("全部");
        foreach (var s in _all.Select(t => t.Strategy).Distinct().OrderBy(s => s, StringComparer.Ordinal))
            StrategyOptions.Add(s);
        SelectedStrategy = StrategyOptions.Contains(keep) ? keep : "全部";

        Rebuild();
    }

    private void Rebuild()
    {
        // 建玉番号: 全履歴から建玉 (EntryExecutionId) 単位で建玉時刻順に採番 (新しい建玉ほど大きい)
        var noMap = new Dictionary<string, int>(StringComparer.Ordinal);
        var ordered = _all
            .GroupBy(t => t.EntryExecutionId)
            .Select(g => (Id: g.Key, Opened: g.Min(x => x.OpenedAt)))
            .OrderBy(x => x.Opened)
            .ToList();
        for (var i = 0; i < ordered.Count; i++) noMap[ordered[i].Id] = i + 1;

        // フィルタ
        IEnumerable<ClosedTrade> q = _all;
        if (SelectedMode == "自動のみ") q = q.Where(t => t.TradeMode == "Auto");
        else if (SelectedMode == "手動のみ") q = q.Where(t => t.TradeMode == "Manual");
        if (SelectedStrategy != "全部") q = q.Where(t => t.Strategy == SelectedStrategy);
        var from = PeriodStart(SelectedPeriod);
        if (from is not null) q = q.Where(t => t.ClosedAt.ToLocalTime() >= from.Value);

        var filtered = q.OrderBy(t => t.ClosedAt).ToList();   // 累積計算のため時系列

        decimal cumulative = 0m;
        var rows = new List<ClosedTradeRow>(filtered.Count);
        foreach (var t in filtered)
        {
            cumulative += t.RealizedPnl;
            rows.Add(new ClosedTradeRow
            {
                TradeNo = noMap.TryGetValue(t.EntryExecutionId, out var no) ? no : 0,
                Strategy = t.Strategy,
                Mode = t.TradeMode == "Auto" ? "自動" : "手動",
                SideText = t.Side == "Buy" ? "買建" : "売建",
                Instrument = InstrumentLabel(t.ProfitMultiplier, t.SymbolCode),
                OpenedText = t.OpenedAt.ToLocalTime().ToString("MM/dd HH:mm"),
                EntryPrice = t.EntryPrice,
                ClosedText = t.ClosedAt.ToLocalTime().ToString("MM/dd HH:mm"),
                ExitPrice = t.ExitPrice,
                Quantity = t.Quantity,
                RealizedPnl = t.RealizedPnl,
                Cumulative = cumulative,
            });
        }
        rows.Reverse();   // 新しい順に表示

        Rows.Clear();
        foreach (var r in rows) Rows.Add(r);

        UpdateSummary(filtered);
    }

    private void UpdateSummary(IReadOnlyList<ClosedTrade> filtered)
    {
        if (filtered.Count == 0)
        {
            SummaryText = "該当する決済履歴はありません";
            SummaryBrush = Neutral;
            return;
        }

        // 勝率は建玉単位 (建玉ごとの実現損益合計の符号で勝敗)
        var perEntry = filtered
            .GroupBy(t => t.EntryExecutionId)
            .Select(g => g.Sum(x => x.RealizedPnl))
            .ToList();
        var tradeCount = perEntry.Count;
        var wins = perEntry.Count(p => p > 0);
        var losses = perEntry.Count(p => p < 0);
        var total = filtered.Sum(t => t.RealizedPnl);
        var winRate = tradeCount > 0 ? 100.0 * wins / tradeCount : 0;
        var avg = tradeCount > 0 ? total / tradeCount : 0m;

        SummaryText =
            $"合計 {total:+#,0;-#,0;0} 円　／　取引 {tradeCount} 件　／　勝 {wins}・負 {losses}・勝率 {winRate:0.#}%　／　平均 {avg:+#,0;-#,0;0} 円";
        SummaryBrush = total > 0 ? Profit : total < 0 ? Loss : Neutral;
    }

    private static DateTime? PeriodStart(string period)
    {
        var today = DateTime.Today;
        return period switch
        {
            "当日" => today,
            "今週" => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),   // 月曜始まり
            "今月" => new DateTime(today.Year, today.Month, 1),
            _ => null,   // 全期間
        };
    }

    private static string InstrumentLabel(int multiplier, string symbolCode) => multiplier switch
    {
        10 => "Micro",
        100 => "Mini",
        1000 => "Large",
        _ => string.IsNullOrEmpty(symbolCode) ? "-" : symbolCode,
    };
}
