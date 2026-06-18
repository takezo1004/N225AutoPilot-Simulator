using N225BrokerBridge.UI.ViewModels;
using Wpf.Ui.Controls;

namespace N225BrokerBridge.UI.Views;

/// <summary>
/// ポジション履歴 (決済済み実現損益) 表示ウィンドウ。メニュー「表示 → ポジション履歴」から開く。
/// 詳細: docs/position-history-spec.md。
/// </summary>
public partial class PositionHistoryWindow : FluentWindow
{
    public PositionHistoryWindow(PositionHistoryViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // 表示のたびに最新の履歴を読み込む
        Loaded += async (_, _) => await vm.LoadAsync();
    }
}
