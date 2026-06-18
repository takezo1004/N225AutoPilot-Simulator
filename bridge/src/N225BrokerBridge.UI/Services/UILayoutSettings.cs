namespace N225BrokerBridge.UI.Services;

/// <summary>
/// UI レイアウトの永続化設定 (ウィンドウサイズ・各 Grid サイズ・各 DataGrid 列幅)。
/// %LOCALAPPDATA%\N225BrokerBridge\ui-layout.json に JSON で保存。
/// 起動時に <see cref="UILayoutStore.Load"/> で読み込み、終了時に <see cref="UILayoutStore.Save"/> で保存。
/// </summary>
public sealed class UILayoutSettings
{
    /// <summary>ウィンドウ自体のサイズ。</summary>
    public WindowLayoutEntry Window { get; set; } = new();

    /// <summary>左パネル (手動発注) の幅。</summary>
    public LeftPanelLayoutEntry LeftPanel { get; set; } = new();

    /// <summary>ログ領域の高さ。</summary>
    public LogLayoutEntry Log { get; set; } = new();

    /// <summary>建玉 / 注文の比率 (GridSplitter で調整した結果)。両方とも ActualHeight。</summary>
    public PositionsOrdersLayoutEntry PositionsOrders { get; set; } = new();

    /// <summary>
    /// 各 DataGrid の列幅。キー: "Strategies" / "Positions" / "Orders"、
    /// 値: 列順の幅配列 (px)。0 の列は復元時にスキップ。
    /// </summary>
    public Dictionary<string, double[]> DataGridColumnWidths { get; set; } = new();
}

public sealed class WindowLayoutEntry
{
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class LeftPanelLayoutEntry
{
    public double Width { get; set; }
}

public sealed class LogLayoutEntry
{
    public double Height { get; set; }
}

public sealed class PositionsOrdersLayoutEntry
{
    public double PositionsHeight { get; set; }
    public double OrdersHeight { get; set; }
}
