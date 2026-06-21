using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using N225BrokerBridge.UI.Services;
using N225BrokerBridge.UI.ViewModels;
using Wpf.Ui.Controls;
// DataGrid を System.Windows.Controls.DataGrid に固定 (XAML 自動生成フィールドの型と一致させる)。
// using Wpf.Ui.Controls; との曖昧さを排除。DataGridLength / DataGridColumn は引き続き System.Windows.Controls から取得。
using DataGrid = System.Windows.Controls.DataGrid;

namespace N225BrokerBridge.UI.Views;

public partial class MainWindow : FluentWindow
{
    private readonly IServiceProvider _services;
    private readonly UILayoutStore _layoutStore;

    public MainWindow(MainViewModel viewModel, IServiceProvider services, UILayoutStore layoutStore)
    {
        InitializeComponent();
        DataContext = viewModel;
        _services = services;
        _layoutStore = layoutStore;

        // シミュレータモードバッジ (--simulator 起動時のみ表示)
        if (App.IsSimulatorMode)
        {
            SimulatorBadge.Visibility = Visibility.Visible;
            Title = "N225AutoTrader Bridge — SIMULATOR";
        }

        // 最大化/復元アイコンを WindowState に応じて切替 (Windows 標準動作)
        //   通常時:  E922 (□ 最大化)
        //   最大化中: E923 (❐ 復元)
        StateChanged += (_, _) =>
            MaxRestoreButton.Content = WindowState == WindowState.Maximized
                ? ""
                : "";
    }

    private void OnSettingsMenuClick(object sender, RoutedEventArgs e)
    {
        var dlg = _services.GetRequiredService<SettingsWindow>();
        dlg.Owner = this;
        dlg.ShowDialog();
    }

    private void OnStrategyManagerMenuClick(object sender, RoutedEventArgs e)
    {
        var dlg = _services.GetRequiredService<StrategyManagerWindow>();
        dlg.Owner = this;
        dlg.ShowDialog();
        // ダイアログ閉じた後、メイン画面の戦略一覧をリフレッシュ
        if (DataContext is MainViewModel vm)
        {
            vm.ReloadStrategiesFromRegistry();
        }
    }

    private void OnPositionHistoryMenuClick(object sender, RoutedEventArgs e)
    {
        var dlg = _services.GetRequiredService<PositionHistoryWindow>();
        dlg.Owner = this;
        dlg.ShowDialog();
    }

    private void OnExitMenuClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnAboutMenuClick(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show(
            "N225AutoTrader Bridge\n\nバージョン: 0.1.0 (開発中)\n旧 N225 Broker Bridge / N225OrderBridge の DDD 流儀での再構築",
            "バージョン情報",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    // VS 2022 風カスタムタイトルバー: 最小化/最大化/閉じるボタンのハンドラ
    private void OnMinimizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // タイトルバーのドラッグでウィンドウ移動 (WindowChrome 単独では Wpf.Ui FluentWindow と
    // 競合してドラッグできない場合があるため、明示的に DragMove で対応)
    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            // ダブルクリックで最大化/復元
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                return;
            }
            DragMove();
        }
    }

    // ===== 発注入力の確定 (枚数バグの恒久対策) =====
    //
    // WPF-UI 4.3 の NumberBox は「Enter キー」または「フォーカス喪失」でしか
    // 入力テキストを Value プロパティに確定しない。そのため「2 と入力してそのまま
    // 買/売/返済ボタンを押す」と、未確定のまま OrderQty=初期値1 で発注され、
    // 2 枚以上を指定しても 1 枚しか発注されない不具合があった (2026-06-01 実機で再現)。
    //
    // ButtonBase.OnClick は Click イベントを Command 実行より「前」に発火させる仕様。
    // よってこの Click ハンドラで、数量・指値・逆指値の各 NumberBox の表示テキストを
    // 直接パースして Value を確定し、さらに BindingExpression.UpdateSource() で
    // ViewModel へ確実に push する。これにより確定タイミング(Enter/フォーカス喪失)に
    // 一切依存せず、表示されている値どおりに必ず発注される。
    private void CommitOrderInputs(object sender, RoutedEventArgs e)
    {
        CommitNumberBox(QtyNumberBox);
        CommitNumberBox(LimitPriceNumberBox);
        CommitNumberBox(StopPriceNumberBox);
    }

    /// <summary>
    /// NumberBox の表示テキストを直接パースして Value を確定し、バインド元へ即時反映する。
    /// パース不可・空欄の場合は現在の Value を維持 (ゼロ等で上書きしない)。
    /// 範囲外の値は NumberBox の Minimum/Maximum により別途クランプされる。
    /// </summary>
    private static void CommitNumberBox(Wpf.Ui.Controls.NumberBox? box)
    {
        if (box is null) return;

        if (double.TryParse(box.Text, out var typed))
        {
            box.Value = typed;
        }
        // Value が変化しないケース (既に確定済みだがバインドが push されていない等) も
        // 取りこぼさないよう、明示的に source へ push する。
        box.GetBindingExpression(Wpf.Ui.Controls.NumberBox.ValueProperty)?.UpdateSource();
    }

    // ===== UI レイアウト永続化 =====
    private bool _layoutApplied;
    private System.Windows.Threading.DispatcherTimer? _saveTimer;

    /// <summary>XAML で定義された各 DataGrid の列幅初期値 (Star 含む)。
    /// 保存済みレイアウト適用前にキャプチャし、「列幅を初期値に戻す」操作で参照する。</summary>
    private readonly Dictionary<string, DataGridLength[]> _defaultColumnWidths = new();

    /// <summary>最初の描画完了後に保存済み UI レイアウトを適用 (1 回のみ)。
    /// 適用後は 5 秒ごとに自動保存タイマーを起動 (ダッシュボードからの強制終了でも最新状態を維持するため)。</summary>
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_layoutApplied) return;
        _layoutApplied = true;

        // 保存済みレイアウトを適用する前に XAML 定義の初期列幅を必ず捕捉。
        // ApplyUILayout 後だと保存値で上書きされた状態が「初期値」と誤認されるため順序厳守。
        CaptureDefaultColumnWidths();

        ApplyUILayout();

        // 自動保存タイマー起動 (5 秒間隔)
        _saveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _saveTimer.Tick += (_, _) => SaveUILayout();
        _saveTimer.Start();
    }

    private void CaptureDefaultColumnWidths()
    {
        _defaultColumnWidths["Strategies"] = StrategiesGrid?.Columns.Select(c => c.Width).ToArray() ?? Array.Empty<DataGridLength>();
        _defaultColumnWidths["Positions"]  = PositionsGrid?.Columns.Select(c => c.Width).ToArray() ?? Array.Empty<DataGridLength>();
        _defaultColumnWidths["Orders"]     = OrdersGrid?.Columns.Select(c => c.Width).ToArray() ?? Array.Empty<DataGridLength>();
    }

    /// <summary>ウィンドウ終了時に現在の UI レイアウトを保存。</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveUILayout();
        base.OnClosing(e);
    }

    /// <summary>保存済み UI レイアウトを読み込み、各コントロールに適用。</summary>
    private void ApplyUILayout()
    {
        var settings = _layoutStore.Load();
        if (settings is null) return;

        if (IsValidSize(settings.Window.Width, 400, 4000)) Width = settings.Window.Width;
        if (IsValidSize(settings.Window.Height, 300, 3000)) Height = settings.Window.Height;

        if (LeftPanelColumn is not null && IsValidSize(settings.LeftPanel.Width, 200, 800))
            LeftPanelColumn.Width = new GridLength(settings.LeftPanel.Width);

        if (LogRow is not null && IsValidSize(settings.Log.Height, 80, 1500))
            LogRow.Height = new GridLength(settings.Log.Height);

        var p = settings.PositionsOrders.PositionsHeight;
        var o = settings.PositionsOrders.OrdersHeight;
        if (PositionsRow is not null && OrdersRow is not null
            && IsValidSize(p, 50, 3000) && IsValidSize(o, 50, 3000))
        {
            PositionsRow.Height = new GridLength(p, GridUnitType.Star);
            OrdersRow.Height = new GridLength(o, GridUnitType.Star);
        }

        ApplyColumnWidths(StrategiesGrid, "Strategies", settings.DataGridColumnWidths);
        ApplyColumnWidths(PositionsGrid, "Positions", settings.DataGridColumnWidths);
        ApplyColumnWidths(OrdersGrid, "Orders", settings.DataGridColumnWidths);
    }

    /// <summary>現在の UI レイアウトを <see cref="UILayoutStore"/> 経由で保存。</summary>
    private void SaveUILayout()
    {
        // 最小化中は実サイズが不定。前回の良い値を温存するため保存しない。
        if (WindowState == WindowState.Minimized) return;

        // ウィンドウサイズは ActualWidth/ActualHeight (実描画サイズ) を使う。
        // Width/Height の依存プロパティは MinWidth/MinHeight でクランプされず、
        // WindowStyle=None + WindowChrome のリサイズ経路でサブ最小値 (例 160x28) を
        // 保持してしまうことがあり、それを保存すると復元時に IsValidSize で弾かれて
        // 既定サイズへ戻ってしまう (実機で発生)。ActualWidth/Height は MinWidth でクランプ
        // されるためこの問題が起きない。最大化中は復元サイズ (RestoreBounds) を保存する。
        double w = WindowState == WindowState.Maximized ? RestoreBounds.Width : ActualWidth;
        double h = WindowState == WindowState.Maximized ? RestoreBounds.Height : ActualHeight;

        var settings = new UILayoutSettings
        {
            Window = new WindowLayoutEntry { Width = w, Height = h },
            LeftPanel = new LeftPanelLayoutEntry { Width = LeftPanelColumn?.ActualWidth ?? 0 },
            Log = new LogLayoutEntry { Height = LogRow?.ActualHeight ?? 0 },
            PositionsOrders = new PositionsOrdersLayoutEntry
            {
                PositionsHeight = PositionsRow?.ActualHeight ?? 0,
                OrdersHeight = OrdersRow?.ActualHeight ?? 0,
            },
            DataGridColumnWidths = new Dictionary<string, double[]>
            {
                ["Strategies"] = GetColumnWidths(StrategiesGrid),
                ["Positions"] = GetColumnWidths(PositionsGrid),
                ["Orders"] = GetColumnWidths(OrdersGrid),
            },
        };
        _layoutStore.Save(settings);
    }

    private static bool IsValidSize(double value, double min, double max)
        => !double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max;

    private static void ApplyColumnWidths(DataGrid? grid, string key, Dictionary<string, double[]> dict)
    {
        if (grid is null) return;
        if (!dict.TryGetValue(key, out var widths)) return;
        for (var i = 0; i < grid.Columns.Count && i < widths.Length; i++)
        {
            if (widths[i] > 0 && widths[i] < 2000)
            {
                grid.Columns[i].Width = new DataGridLength(widths[i]);
            }
        }
    }

    private static double[] GetColumnWidths(DataGrid? grid)
    {
        if (grid is null) return Array.Empty<double>();
        return grid.Columns.Select(c => c.ActualWidth).ToArray();
    }

    /// <summary>「表示」→「列幅を初期値に戻す」メニュー。
    /// 3 つの DataGrid を XAML 定義の初期列幅 (Star 含む) に戻し、
    /// 永続化ファイル (ui-layout.json) も即時上書きして次回起動時の復元値も合わせる。</summary>
    private void OnResetColumnWidthsMenuClick(object sender, RoutedEventArgs e)
    {
        ResetColumnWidthsToDefault(StrategiesGrid, "Strategies");
        ResetColumnWidthsToDefault(PositionsGrid, "Positions");
        ResetColumnWidthsToDefault(OrdersGrid, "Orders");

        // 即時保存 (5 秒タイマー待ちで古い列幅が json に残るのを防ぐ)
        SaveUILayout();
    }

    private void ResetColumnWidthsToDefault(DataGrid? grid, string key)
    {
        if (grid is null) return;
        if (!_defaultColumnWidths.TryGetValue(key, out var widths)) return;
        for (var i = 0; i < grid.Columns.Count && i < widths.Length; i++)
        {
            grid.Columns[i].Width = widths[i];
        }
    }
}
