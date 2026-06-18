using System.Windows;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.UI.ViewModels;
using Wpf.Ui.Controls;

namespace N225BrokerBridge.UI.Views;

public partial class StrategyManagerWindow : FluentWindow
{
    private readonly ILogger<StrategyManagerWindow>? _logger;

    public StrategyManagerWindow(StrategyManagerViewModel vm, ILogger<StrategyManagerWindow> logger)
    {
        InitializeComponent();
        DataContext = vm;
        _logger = logger;
        _logger.LogInformation("StrategyManagerWindow opened");
    }

    // WPF-UI 4.3 の NumberBox は「Enter」または「フォーカス喪失」でしか Value を確定しない。
    // そのため Interval を入力してそのまま「追加」「更新」を押すと未確定のまま処理され、
    // 初期値 5 のまま登録される。ButtonBase.OnClick は Command 実行より前に Click を発火させる
    // 仕様なので、この Click ハンドラで表示テキストを確定 → BindingExpression.UpdateSource() で
    // ViewModel へ確実に push してから AddCommand/UpdateCommand を走らせる
    // (MainWindow.CommitNumberBox と同一の恒久対策。2026-06-08)。
    private void OnCommitIntervalBeforeCommand(object sender, RoutedEventArgs e)
    {
        if (IntervalNumberBox is null) return;
        if (double.TryParse(IntervalNumberBox.Text, out var typed))
        {
            IntervalNumberBox.Value = typed;
        }
        IntervalNumberBox.GetBindingExpression(NumberBox.ValueProperty)?.UpdateSource();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        _logger?.LogInformation("StrategyManagerWindow: Close button clicked");
        // ShowDialog 経由で開かれているため DialogResult を明示してから Close する。
        // これがないと一部の FluentWindow 派生で Close() が無視されることがある。
        try { DialogResult = false; } catch { /* not modal */ }
        Close();
    }
}
