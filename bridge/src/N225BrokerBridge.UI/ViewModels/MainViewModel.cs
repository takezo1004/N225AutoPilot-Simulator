using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Application.Orders;
using N225BrokerBridge.Application.Positions;
using N225BrokerBridge.Application.Signals;
using N225BrokerBridge.Application.Sync;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.Positions;
using N225BrokerBridge.Domain.ValueObjects;
using N225BrokerBridge.Infrastructure.Webhooks;
using N225BrokerBridge.UI.Services;

namespace N225BrokerBridge.UI.ViewModels;

/// <summary>
/// メインウィンドウのビューモデル。
/// 旧 N225OrderBridge の TradeView の機能を継承:
///   - 銘柄選択 (グローバル: n225Mini / n225Micro / 将来増設可)
///   - 手動発注フォーム + 買/売/返/取消ボタン (UseCase 経由で実発注)
///   - 価格 (現在値/BID/ASK) ← <see cref="IPriceUpdateNotifier"/> から実機更新
///   - 戦略一覧 / 建玉一覧 (グループ折りたたみ + 集計) / 注文一覧 ← 実機データ購読
///   - ログ + AUTO トグル
///
/// データソース:
///   - 建玉一覧: <see cref="IPositionChangeNotifier"/> (PositionReconciliationService が起動時投入 +
///     ExecutionApplier がライブ更新)
///   - 注文一覧: <see cref="IOrderSnapshotNotifier"/> (KabuOrderPollingService の毎秒スナップショット)
///   - 価格: <see cref="IPriceUpdateNotifier"/> (KabuBoardWebSocketService の push)
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IBrokerAdapter _broker;
    private readonly HttpWebhookListener _webhookListener;
    private readonly PlaceNewOrderUseCase _placeNewOrderUseCase;
    private readonly ManualClosePositionUseCase _manualCloseUseCase;
    private readonly IPositionRepository _positionRepo;
    private readonly IPositionChangeNotifier _positionNotifier;
    private readonly IOrderSnapshotNotifier _orderSnapshotNotifier;
    private readonly IPriceUpdateNotifier _priceNotifier;
    private readonly IStrategyRegistry _strategyRegistry;
    private readonly IOrderMetadataStore _orderMetaStore;
    private readonly LocalSettingsStore _localSettingsStore;
    private readonly IAutoTradeGate _autoTradeGate;
    private readonly IAutoTradeInstrumentProvider _autoTradeInstrumentProvider;
    private readonly ContractMultiplierRegistry _contractMultipliers;
    private readonly ILogger<MainViewModel> _logger;

    // ── ステータス ─────────────────────────────────────────────
    [ObservableProperty] private bool _isAutoTradeEnabled;
    [ObservableProperty] private string _brokerStatus = "未接続";
    [ObservableProperty] private string _webhookStatus = "停止中";
    [ObservableProperty] private string _stateMessage = "待機";

    // ── 銘柄一覧 (手動発注パネルで使用) ───────────────────────
    public ObservableCollection<InstrumentDefinition> AvailableInstruments { get; } = new();

    // ── 価格 ───────────────────────────────────────────────────
    [ObservableProperty] private decimal _currentPrice;
    [ObservableProperty] private decimal _bidPrice;
    [ObservableProperty] private int _bidQty;
    [ObservableProperty] private decimal _askPrice;
    [ObservableProperty] private int _askQty;

    // ── 手動発注フォーム ──────────────────────────────────────
    [ObservableProperty] private InstrumentDefinition? _manualOrderInstrument;
    // ui:NumberBox.Value が double? 型のため、ViewModel 側も double で保持する。
    // int で宣言すると double?→int の書き戻し変換が失敗し、UI で入力した枚数が反映されず
    // 初期値 1 のまま固定される (2026-05-28 修正)。発注時に int にキャストする。
    // ※ ただし WPF-UI NumberBox は Enter/フォーカス喪失でしか Value を確定しないため、
    //   「入力 → そのままボタン押下」での取りこぼし対策が別途必要。発注ボタン押下時に
    //   MainWindow.xaml.cs の CommitOrderInputs で表示テキストを確定 → OrderQty へ push する。
    //   詳細は docs/troubleshooting.md §7 (2026-06-01 恒久対策)。
    [ObservableProperty] private double _orderQty = 1;
    // ui:NumberBox.Value が double? 型のため、ViewModel 側も double で保持 (decimal→double 変換による
    // バインド失敗を回避)。発注時に decimal にキャストする。
    [ObservableProperty] private double _limitPrice;
    [ObservableProperty] private double _stopPrice;
    [ObservableProperty] private OrderTypeChoice _orderType = OrderTypeChoice.BestMarket;
    [ObservableProperty] private string _selectedTimeInForce = "FAS";   // 初期 OrderType=BestMarket に対応

    /// <summary>
    /// 注文タイプに応じて変化する時間条件 (TimeInForce) の選択肢。
    /// 旧 N225OrderBridge と同じ kabu API 制約:
    ///   - 成行 / 逆指値 → FAK / FOK のみ (FAS 不可)
    ///   - 対当 / 指値   → FAS / FAK / FOK 全部
    /// 注文タイプ変更で「新しいインスタンスに丸ごと置換」(Clear+Add だと ComboBox.SelectedItem
    /// が一時的に null になりタイミング問題が出るため)。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<string> _availableTimeInForces = new() { "FAS", "FAK", "FOK" };

    /// <summary>指値価格入力欄の活性化フラグ (指値選択時のみ true)。</summary>
    public bool IsLimitPriceEnabled => OrderType == OrderTypeChoice.Limit;

    /// <summary>逆指値価格入力欄の活性化フラグ (逆指値選択時のみ true)。</summary>
    public bool IsStopPriceEnabled => OrderType == OrderTypeChoice.Stop;

    /// <summary>指値価格入力の読み取り専用フラグ (指値以外で true、IsEnabled と二段重ねで効かせる)。</summary>
    public bool IsLimitPriceReadOnly => !IsLimitPriceEnabled;
    public bool IsStopPriceReadOnly => !IsStopPriceEnabled;

    /// <summary>視覚的なグレーアウト効果 (Opacity)。</summary>
    public double LimitPriceOpacity => IsLimitPriceEnabled ? 1.0 : 0.4;
    public double StopPriceOpacity => IsStopPriceEnabled ? 1.0 : 0.4;

    partial void OnLimitPriceChanged(double value)
        => _logger?.LogInformation("LimitPrice changed to {Value}", value);
    partial void OnStopPriceChanged(double value)
        => _logger?.LogInformation("StopPrice changed to {Value}", value);

    /// <summary>自動売買トグル変更時に SignalHandler のゲートへ伝播 (OFF の間は全 webhook シグナル無視)。</summary>
    partial void OnIsAutoTradeEnabledChanged(bool value)
    {
        if (_autoTradeGate is not null)
        {
            _autoTradeGate.IsEnabled = value;
            _logger?.LogInformation("自動売買トグル: {State}", value ? "ON" : "OFF");
        }
    }

    /// <summary>注文タイプ変更時に時間条件の選択肢・価格入力可否・自動価格セットを連動 (旧 TradeView の RadioButton_Click 相当)。</summary>
    partial void OnOrderTypeChanged(OrderTypeChoice value)
    {
        _logger?.LogInformation("OrderType changed to {OrderType}, updating TIF list", value);

        // 旧 N225OrderBridge の RadioButton_Click のデフォルト値準拠:
        //   対当・指値 → デフォルト FAS (最良気配/指値は当日中が自然)
        //   成行・逆指 → デフォルト FAK (FAS は kabu API で不可)
        (string[] options, string defaultTif) = value switch
        {
            OrderTypeChoice.Market => (new[] { "FAK", "FOK" }, "FAK"),
            OrderTypeChoice.BestMarket => (new[] { "FAS", "FAK", "FOK" }, "FAS"),
            OrderTypeChoice.Limit => (new[] { "FAS", "FAK", "FOK" }, "FAS"),
            OrderTypeChoice.Stop => (new[] { "FAK", "FOK" }, "FAK"),
            _ => (new[] { "FAS", "FAK", "FOK" }, "FAS")
        };

        AvailableTimeInForces = new ObservableCollection<string>(options);
        SelectedTimeInForce = defaultTif;

        // 価格自動セット / リセット (LimitPrice/StopPrice は double 型)
        switch (value)
        {
            case OrderTypeChoice.Limit:
                LimitPrice = (double)CurrentPrice;
                StopPrice = 0d;
                break;
            case OrderTypeChoice.Stop:
                StopPrice = (double)CurrentPrice;
                LimitPrice = 0d;
                break;
            case OrderTypeChoice.BestMarket:
            case OrderTypeChoice.Market:
                LimitPrice = 0d;
                StopPrice = 0d;
                break;
        }

        // 入力可否プロパティの変更通知 (IsEnabled / IsReadOnly / Opacity 全てを更新)
        OnPropertyChanged(nameof(IsLimitPriceEnabled));
        OnPropertyChanged(nameof(IsStopPriceEnabled));
        OnPropertyChanged(nameof(IsLimitPriceReadOnly));
        OnPropertyChanged(nameof(IsStopPriceReadOnly));
        OnPropertyChanged(nameof(LimitPriceOpacity));
        OnPropertyChanged(nameof(StopPriceOpacity));
    }

    // ── データグリッド ────────────────────────────────────────
    public ObservableCollection<StrategyRow> Strategies { get; } = new();
    public ObservableCollection<PositionRow> Positions { get; } = new();
    public ObservableCollection<OrderRow> Orders { get; } = new();
    public ObservableCollection<UiLogEntry> LogEntries { get; }

    /// <summary>建玉一覧で選択された行 (DataGrid.SelectedItem からバインド)。</summary>
    [ObservableProperty] private PositionRow? _selectedPosition;

    /// <summary>注文一覧で選択された行 (キャンセル対象)。</summary>
    [ObservableProperty] private OrderRow? _selectedOrder;

    public MainViewModel(
        IBrokerAdapter broker,
        HttpWebhookListener webhookListener,
        UiLogSink logSink,
        PlaceNewOrderUseCase placeNewOrderUseCase,
        ManualClosePositionUseCase manualCloseUseCase,
        IPositionRepository positionRepo,
        IPositionChangeNotifier positionNotifier,
        IOrderSnapshotNotifier orderSnapshotNotifier,
        IPriceUpdateNotifier priceNotifier,
        IStrategyRegistry strategyRegistry,
        IOrderMetadataStore orderMetaStore,
        LocalSettingsStore localSettingsStore,
        IAutoTradeGate autoTradeGate,
        IAutoTradeInstrumentProvider autoTradeInstrumentProvider,
        ContractMultiplierRegistry contractMultipliers,
        ILogger<MainViewModel> logger)
    {
        _broker = broker;
        _webhookListener = webhookListener;
        _placeNewOrderUseCase = placeNewOrderUseCase;
        _manualCloseUseCase = manualCloseUseCase;
        _positionRepo = positionRepo;
        _positionNotifier = positionNotifier;
        _orderSnapshotNotifier = orderSnapshotNotifier;
        _priceNotifier = priceNotifier;
        _strategyRegistry = strategyRegistry;
        _orderMetaStore = orderMetaStore;
        _localSettingsStore = localSettingsStore;
        _autoTradeGate = autoTradeGate;
        _autoTradeInstrumentProvider = autoTradeInstrumentProvider;
        _contractMultipliers = contractMultipliers;
        _logger = logger;
        LogEntries = logSink.Entries;

        // 自動売買トグル → ゲートへ初期同期 (ObservableProperty 既定値は false)
        _autoTradeGate.IsEnabled = IsAutoTradeEnabled;

        // 自動売買対象銘柄が変わったらログに残す (運用上、誤発注の検出を早めるため)
        _autoTradeInstrumentProvider.Changed += OnAutoTradeInstrumentChanged;

        LoadInstruments();
        LoadStrategies();
        RefreshStatus();

        // 通知購読 (UI スレッドへ Dispatcher 経由で marshal)
        _positionNotifier.Changed += OnPositionChanged;
        _orderSnapshotNotifier.SnapshotsUpdated += OnOrderSnapshotsUpdated;
        _priceNotifier.PriceUpdated += OnPriceUpdated;

        // 起動順の都合で MainViewModel 生成より前に Step 2 (InitialFetchOrdersAsync) が
        // SnapshotsUpdated を発火していて、ここで購読しても取りこぼしてしまうため、
        // KabuOrderPollingService が保持している最新スナップショットを取り込み直す。
        if (_orderSnapshotNotifier.LatestSnapshots.Count > 0)
        {
            OnOrderSnapshotsUpdated(this,
                new OrderSnapshotsEventArgs(_orderSnapshotNotifier.LatestSnapshots, DateTime.UtcNow));
        }

        // 戦略レジストリ変更時に一覧を自動再ロード (シグナル受信で LastSignalAt 等が更新されたら反映)
        _strategyRegistry.Changed += (_, _) => _ = OnUiAsync(() =>
        {
            Strategies.Clear();
            LoadStrategies();
        });

        // ★ デモモード分岐 ★
        // App.IsDemoMode は --demo CLI 引数で起動された時のみ true。
        // デモモードでは kabu/Webhook が起動していないため、kabu に問い合わせる
        // LoadInitialStateAsync / TryResolveInstrumentsAsync を呼ぶと例外/ハングする。
        // 代わりに SeedDemoData() で全パネル分の決め打ちデータを直接 ObservableCollection に
        // 突っ込んで画面表示する。
        if (N225BrokerBridge.UI.App.IsDemoMode)
        {
            SeedDemoData();
        }
        else
        {
            // ── 通常起動 ──
            // 起動時の初期状態を Repository から取り込む
            // (PositionReconciliationService が HostedService として既に kabu から取得済みの想定)
            _ = LoadInitialStateAsync();

            // 起動時の現月コード解決 (fire-and-forget、解決完了時に UI が自動更新される)
            _ = TryResolveInstrumentsAsync();

            // 旧 N225OrderBridge の WriteMessage("正常に初期化され起動しました。") 相当
            StateMessage = "正常に初期化されました";
        }

        _logger.LogInformation("MainViewModel 初期化完了。");
    }

    /// <summary>
    /// デモモード用のサンプルデータを画面に流し込む。
    /// <see cref="N225BrokerBridge.UI.App.IsDemoMode"/> が true (= <c>--demo</c> CLI 引数起動) の時だけ呼ばれる。
    ///
    /// === なぜこれが必要か ===
    /// ブログ記事/マニュアル用に「いかにも本番運用中のブリッジ画面」のスクショを撮りたい時、
    /// 本番口座をそのまま映すと建玉や損益等の個人情報が露出する。一方、検証モード (kabu:18081) は
    /// 価格 push が来ず銘柄解決もできないので画面が空のままになる。そこで、本番にも検証にも
    /// 繋がず、画面の各パネルだけハードコード値で埋める「決め打ち表示モード」を用意した。
    ///
    /// === このメソッドが書き換える表示要素 ===
    /// ・ステータスバー (ブローカー接続/Webhook 受信/直近発注状態/自動売買トグル)
    /// ・銘柄定義 (日経 225 Micro / Mini の解決済み銘柄コード + 限月ラベル + 現在値)
    /// ・現在値パネル (CurrentPrice/BidPrice/AskPrice)
    /// ・手動発注フォーム既定値 (注文タイプ/TIF/数量)
    /// ・戦略一覧 (V7_7_fixed_L 有効 + TestStrategy 無効 の 2 件)
    /// ・建玉一覧 (日経 225 Micro 買 1 枚 / 損益 +200 円 の 1 件)
    /// ・注文一覧 (上記建玉の元になった約定済オーダー 1 件)
    /// ・ログ一覧 (passphrase 拒否 + Webhook 受信→ポーリング→約定検出→建玉オープン の時系列)
    ///
    /// === 永続化されないこと ===
    /// IStrategyRegistry.UpsertAsync 等の書き込み系メソッドは一切呼ばないため、
    /// %LOCALAPPDATA%\N225BrokerBridge\strategies.json / auto-positions.json には
    /// デモのデータは絶対に残らない。デモ画面を閉じた瞬間に消える。
    ///
    /// === 関連ドキュメント ===
    /// 詳細は <c>N225BrokerBridge/docs/demo-mode.md</c> 参照。
    /// </summary>
    private void SeedDemoData()
    {
        _logger.LogInformation("==== デモモード: 決め打ちデータをセットします ====");

        // 既存の戦略/建玉/注文/ログ (起動時の Information 系) をクリア
        Strategies.Clear();
        Positions.Clear();
        Orders.Clear();
        LogEntries.Clear();

        // ── ステータスバー ────────────────────────────────────
        BrokerStatus = "接続中 (kabu)";
        WebhookStatus = "受信中";
        StateMessage = "売注文: Accepted OrderId=DEMO20260525001";
        IsAutoTradeEnabled = true;

        // ── 銘柄定義 (日経 225 Micro を解決済として設定) ──────
        var micro = AvailableInstruments.FirstOrDefault(i => i.FutureCode == "NK225micro");
        if (micro is not null)
        {
            micro.ResolvedSymbolCode = "161060023";
            micro.ContractMonth = "2026年6月限";
            _contractMultipliers.Set(micro.ResolvedSymbolCode, micro.ProfitMultiplier);
            micro.LastPrice = 65420m;
            micro.BidPrice = 65420m;
            micro.BidQty = 0;
            micro.AskPrice = 65415m;
            micro.AskQty = 0;
            ManualOrderInstrument = micro;
        }
        var mini = AvailableInstruments.FirstOrDefault(i => i.FutureCode == "NK225mini");
        if (mini is not null)
        {
            mini.ResolvedSymbolCode = "161060019";
            mini.ContractMonth = "2026年6月限";
            _contractMultipliers.Set(mini.ResolvedSymbolCode, mini.ProfitMultiplier);
            mini.LastPrice = 65420m;
            mini.BidPrice = 65420m;
            mini.AskPrice = 65415m;
        }

        // ── 現在値パネル ──────────────────────────────────────
        CurrentPrice = 65420m;
        BidPrice = 65420m;
        BidQty = 0;
        AskPrice = 65415m;
        AskQty = 0;

        // ── 手動発注フォーム既定値 ────────────────────────────
        OrderType = OrderTypeChoice.Market;
        SelectedTimeInForce = "FAK";
        OrderQty = 1;
        LimitPrice = 0d;
        StopPrice = 0d;

        // ── 戦略一覧 (2 件: 1 有効 + 1 無効) ──────────────────
        // registry を経由せず Strategies コレクションに直接追加するため、
        // PropertyChanged で UpsertAsync が走らないよう IsEnabled は最初から設定。
        Strategies.Add(new StrategyRow
        {
            IsEnabled = true,
            AlertName = "V7_7_fixed_L",
            Interval = 15,
            LastSignalAt = "11:25:33",
            LastTradeType = "新規",
            LastSide = "買",
            LastPrice = "65,400",
        });
        Strategies.Add(new StrategyRow
        {
            IsEnabled = false,
            AlertName = "TestStrategy",
            Interval = 5,
            LastSignalAt = "11:14:52",
            LastTradeType = string.Empty,
            LastSide = string.Empty,
            LastPrice = string.Empty,
        });

        // ── 建玉一覧 (1 件、損益は控えめなプラス) ────────────
        Positions.Add(new PositionRow
        {
            SymbolName = "日経225Micro",
            SymbolCode = "161060023",
            TradeMode = "自動",
            ExecutionDay = "20260525",
            Strategy = "V7_7_fixed_L",
            Interval = 15,
            Side = "買",
            LeaveQty = 1,
            HoldQty = 0,
            Price = 65400m,
            Profit = 200m,
            ExecutionId = "EDEMO001RS",
            OrderId = "DEMO20260525001",
        });

        // ── 注文一覧 (1 件、約定済) ───────────────────────────
        Orders.Add(new OrderRow
        {
            SymbolName = "日経225Micro",
            TradeMode = "自動",
            RecvTime = "11:25:33.420",
            Strategy = "V7_7_fixed_L",
            Interval = 15,
            CashMargin = "新規",
            Side = "買",
            State = "約定済",
            OrderQty = 1,
            CumQty = 1,
            Price = 65400m,
            OrderId = "DEMO20260525001",
            ExecutionId = "EDEMO001RS",
        });

        // ── ログ挿入 (Background priority で遅延実行) ──
        // SeedDemoData 直後にログを挿入しても、その後に来る起動完了ログ
        // (App.xaml.cs の "N225BrokerBridge 起動完了 (デモモード)" や
        // UILayoutStore の "UI レイアウト読み込み完了" 等) が割り込んでくるため、
        // 即時挿入だとデモログが下に押し下げられて画面で見えなくなる。
        //
        // Dispatcher.BeginInvoke を Background priority (= Normal priority より低い) で
        // 呼ぶと、起動ログがすべて出揃って Dispatcher が手すきになった時点で初めて
        // 実行されるため、最終的にログパネルには決め打ちデモログ 7 行だけが残る。
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.BeginInvoke(
                new Action(InsertDemoLogs),
                System.Windows.Threading.DispatcherPriority.Background);
        }
        else
        {
            // テスト環境等で Dispatcher が無い場合はインライン実行
            InsertDemoLogs();
        }
    }

    /// <summary>
    /// デモモードのログパネル表示用に、決め打ちログ 7 件を時系列でセットする。
    /// 既存ログを全クリアしてから挿入するので、Background priority で起動ログが
    /// 出揃った後に呼ばれることを前提とする (<see cref="SeedDemoData"/> 末尾参照)。
    /// </summary>
    private void InsertDemoLogs()
    {
        LogEntries.Clear();

        var today = DateTime.Today;
        UiLogEntry MakeEntry(int h, int m, int s, int ms, string level, string message)
            => new(today.AddHours(h).AddMinutes(m).AddSeconds(s).AddMilliseconds(ms), level, message, null);

        // 古い順に Insert(0, ...) を繰り返すと、最後に Insert したものが一番上に来る
        // → 画面では「新しい順 (上が直近)」になり、UiLogSink の通常挙動と一致する。
        //
        // 注: ポーリングログ (注文ポーリング: ... 件照会 / 注文ポーリング: 受信) は
        //     本番側でも Debug レベルに下げたため UI には出ないので、デモログにも含めない。
        LogEntries.Insert(0, MakeEntry(11, 14, 52, 103, "Warning", "Signal rejected: passphrase mismatch alert=\"oldstrategy\""));
        LogEntries.Insert(0, MakeEntry(11, 25, 33, 420, "Information", "Webhook 受信: alert=\"V7_7_fixed_L\" side=buy trade_type=new"));
        LogEntries.Insert(0, MakeEntry(11, 25, 33, 570, "Information", "約定待ちリストから削除: 注文ID=\"DEMO20260525001\"（約定待ち合計 0 件）"));
        LogEntries.Insert(0, MakeEntry(11, 25, 33, 575, "Information", "Position opened: id=EDEMO001RS side=Buy qty=\"1\" entry=65400"));
        LogEntries.Insert(0, MakeEntry(11, 25, 33, 580, "Information", "約定検出: 注文ID=\"DEMO20260525001\" 約定ID=\"EDEMO001RS\" 数量=1 価格=65400"));
    }

    /// <summary>
    /// 戦略管理ダイアログ等で変更があった後にメイン画面の戦略一覧を再読み込み。
    /// </summary>
    public void ReloadStrategiesFromRegistry()
    {
        Strategies.Clear();
        LoadStrategies();
    }

    /// <summary>
    /// IStrategyRegistry から戦略一覧を読み込み、UI に反映。
    /// StrategyRow.IsEnabled 変更時に registry.UpsertAsync で自動永続化。
    ///
    /// 注意: 「空なら V7-7-fixed を seed」する処理は撤去 (2026-05-18)。
    ///   - ユーザーが削除しても起動のたびに復活する「決め打ち」問題
    ///   - 削除 → Changed イベント → LoadStrategies → seed → UpsertAsync.GetResult() で UI デッドロック
    /// 戦略の初期登録はユーザーが「戦略管理」ダイアログから手動で行う方針。
    /// </summary>
    private void LoadStrategies()
    {
        var entries = _strategyRegistry.GetAll();

        foreach (var e in entries)
        {
            var row = new StrategyRow
            {
                AlertName = e.AlertName,
                Interval = e.Interval,
                IsEnabled = e.IsEnabled,
                LastSignalAt = e.LastSignalAt?.ToLocalTime().ToString("HH:mm:ss") ?? string.Empty,
                LastTradeType = e.LastTradeType ?? string.Empty,
                LastSide = e.LastSide ?? string.Empty,
                LastPrice = e.LastPrice is null
                    ? string.Empty
                    : (e.LastPrice == 0m ? "成行" : e.LastPrice.Value.ToString("N0")),
                Description = e.Description ?? string.Empty
            };
            row.PropertyChanged += async (_, args) =>
            {
                if (args.PropertyName != nameof(StrategyRow.IsEnabled)) return;
                try
                {
                    await _strategyRegistry.UpsertAsync(new StrategyEntry
                    {
                        AlertName = row.AlertName,
                        Interval = row.Interval,
                        IsEnabled = row.IsEnabled,
                        Description = e.Description,
                        LastSignalAt = e.LastSignalAt
                    });
                    _logger.LogInformation(
                        "Strategy {Name}/{Interval} → IsEnabled={Enabled}",
                        row.AlertName, row.Interval, row.IsEnabled);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save strategy {Name}", row.AlertName);
                }
            };
            Strategies.Add(row);
        }
    }

    /// <summary>
    /// 各銘柄について kabu /symbolname/future で現月コードを解決し
    /// /symbol/{symbol}@{exchange}?info=true で限月情報 (DerivMonth) を取得して
    /// InstrumentDefinition の ResolvedSymbolCode / ContractMonth を上書きする。
    /// 解決失敗時は ResolvedSymbolCode=null のまま (発注不可 / push 不可)。
    /// 最後に解決成功した銘柄を 1 リクエストで /register に登録 (board push 対象)。
    /// </summary>
    private async Task TryResolveInstrumentsAsync()
    {
        foreach (var instrument in AvailableInstruments.ToList())
        {
            try
            {
                var resolved = await _broker.ResolveFutureSymbolAsync(instrument.FutureCode, derivMonth: 0);
                if (resolved is not null)
                {
                    instrument.ResolvedSymbolCode = resolved.Symbol.Value;
                    instrument.ContractMonth = resolved.ContractMonthLabel;
                    // 損益倍率レジストリに登録 (ポジション履歴の実現損益計算に使われる)
                    _contractMultipliers.Set(resolved.Symbol.Value, instrument.ProfitMultiplier);
                }
                else
                {
                    instrument.ContractMonth = "解決失敗";
                    _logger.LogWarning(
                        "銘柄解決失敗 {Name}/{FutureCode} (kabu API 応答が空)",
                        instrument.DisplayName, instrument.FutureCode);
                }
            }
            catch (Exception ex)
            {
                instrument.ContractMonth = "解決エラー";
                _logger.LogWarning(ex,
                    "銘柄解決失敗 {Name}/{FutureCode}",
                    instrument.DisplayName, instrument.FutureCode);
            }
        }

        // 解決完了 → 現在選択中の銘柄を自動売買 provider に反映 (起動直後にここで初めて確定する)
        PushSelectedInstrumentToAutoTradeProvider();

        // 全銘柄を 1 リクエストで /register に一括登録
        // 旧 N225OrderBridge は 1 銘柄ずつ register していたが、API は配列対応のため最適化
        var allSymbols = AvailableInstruments
            .Where(i => !string.IsNullOrEmpty(i.ResolvedSymbolCode))
            .Select(i => new SymbolCode(i.ResolvedSymbolCode!))
            .ToList();
        if (allSymbols.Count > 0)
        {
            try
            {
                await _broker.SubscribePricesAsync(allSymbols);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "価格 push 一括購読登録失敗 ({Count} 件)", allSymbols.Count);
            }
        }

        // 解決完了 → 既に画面に乗っている建玉/注文の SymbolName を kabu コードから
        // "日経225Mini" 等の表示名に差し替える (LoadInitialStateAsync の方が先に走るため)。
        await OnUiAsync(() =>
        {
            foreach (var row in Positions)
                row.SymbolName = ResolveSymbolDisplay(row.SymbolCode);
            foreach (var row in Orders)
            {
                // 表示名がまだ kabu コードのまま残っている行を、現月コード照合で表示名に差し替える。
                var hit = AvailableInstruments.FirstOrDefault(
                    i => i.ResolvedSymbolCode == row.SymbolCode || i.ResolvedSymbolCode == row.SymbolName);
                if (hit is not null) row.SymbolName = hit.DisplayName;
            }

            // 限月解決が完了したので、起動時 (解決前) に投入された旧限月の建玉/注文を画面から除去する。
            // これでライブ一覧は現月 (例: 9月限) だけになる。履歴ストアには手を付けない。
            ApplyContractMonthScope();
        });

        // 場が閉まっていると WebSocket push が来ないため、起動時に /board を 1 回叩いて
        // 現在値/BID/ASK を取得し、損益表示を初期化する。
        foreach (var instrument in AvailableInstruments)
        {
            if (string.IsNullOrEmpty(instrument.ResolvedSymbolCode)) continue;
            try
            {
                var quote = await _broker.GetQuoteAsync(new SymbolCode(instrument.ResolvedSymbolCode));
                await OnUiAsync(() =>
                {
                    instrument.LastPrice = quote.LastPrice.Value;
                    instrument.BidPrice = quote.BidPrice.Value;
                    instrument.AskPrice = quote.AskPrice.Value;
                    instrument.BidQty = quote.BidQuantity.Value;
                    instrument.AskQty = quote.AskQuantity.Value;

                    if (ReferenceEquals(instrument, ManualOrderInstrument))
                    {
                        CurrentPrice = instrument.LastPrice;
                        BidPrice = instrument.BidPrice;
                        AskPrice = instrument.AskPrice;
                        BidQty = instrument.BidQty;
                        AskQty = instrument.AskQty;
                    }

                    RecalculateAllProfitsWithSharedPrice(quote.LastPrice.Value);
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "起動時の /board 取得失敗 {Symbol}", instrument.ResolvedSymbolCode);
            }
        }
    }

    /// <summary>
    /// 起動時に Repository に既に投入されている建玉 (PositionReconciliationService が
    /// kabu から取得した分) を Positions 一覧に反映する。
    /// </summary>
    private async Task LoadInitialStateAsync()
    {
        try
        {
            var active = await _positionRepo.FindActiveAsync();
            await OnUiAsync(() =>
            {
                Positions.Clear();
                foreach (var p in active.Where(p => IsCurrentMonth(p.Symbol.Value)))
                    Positions.Add(ToRow(p));
            });
            _logger.LogInformation("起動時の建玉ロード完了 ({Count} 件)。", active.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LoadInitialState failed");
        }
    }

    /// <summary>
    /// 銘柄ラインナップ。FutureCode のみ初期化、ResolvedSymbolCode/ContractMonth は
    /// 起動後 kabu /symbolname/future + /symbol/@/exchange で解決して上書きする
    /// (TryResolveInstrumentsAsync)。
    ///
    /// 決め打ちの銘柄コードは絶対に持たない (限月切替を取り違える事故の元になるため)。
    /// 解決完了までは ResolvedSymbolCode=null のため、発注不可。
    /// ProfitMultiplier は旧 N225OrderBridge と同じ値 (Mini=100、Micro=10)。
    ///
    /// ⚠️ 自動売買運用上の注意:
    ///   - AvailableInstruments[0] (Mini) が初期 ManualOrderInstrument に設定される。
    ///     ということは「起動直後の自動売買対象銘柄も Mini」が既定動作。
    ///     Micro で運用する利用者は、起動後に手動発注パネルで Micro を選択する必要がある。
    ///   - 将来 TOPIX 先物・グロース先物等を追加する場合も同様: 単に AvailableInstruments.Add するだけで
    ///     自動売買の発注先候補にもなる。「自動売買では Mini/Micro 以外を使わせない」制約が必要なら
    ///     IAutoTradeInstrumentProvider 側にホワイトリストを追加する形に拡張する。
    /// </summary>
    private void LoadInstruments()
    {
        AvailableInstruments.Add(new InstrumentDefinition
        {
            DisplayName = "日経225Mini",
            FutureCode = "NK225mini",
            ResolvedSymbolCode = null,
            ContractMonth = "解決中...",
            ProfitMultiplier = 100
        });
        AvailableInstruments.Add(new InstrumentDefinition
        {
            DisplayName = "日経225Micro",
            FutureCode = "NK225micro",
            ResolvedSymbolCode = null,
            ContractMonth = "解決中...",
            ProfitMultiplier = 10
        });

        ManualOrderInstrument = AvailableInstruments[0];
    }

    [RelayCommand]
    private void RefreshStatus()
    {
        BrokerStatus = _broker.IsConnected ? $"接続中 ({_broker.BrokerCode})" : "未接続";
        WebhookStatus = _webhookListener.IsRunning ? "受信中" : "停止中";
    }

    // ── 限月スコープ (現月管理) ─────────────────────────────────
    // ライブの建玉一覧・注文一覧は「現在解決済みの現月銘柄」だけを表示する。
    // kabu /positions・/orders は全限月を返すため、旧限月 (前月・SQ 決済待ち等) が混ざる。
    // 履歴 (position-history.json / orders-metadata.json) は消さず、表示だけ現月に絞る。

    /// <summary>現在解決済みの現月銘柄コード集合 (Mini/Micro 等)。限月未解決時は空。</summary>
    private HashSet<string> CurrentMonthSymbolCodes()
        => AvailableInstruments
            .Where(i => !string.IsNullOrEmpty(i.ResolvedSymbolCode))
            .Select(i => i.ResolvedSymbolCode!)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// 指定銘柄コードを現月としてライブ表示してよいか。
    /// 限月未解決の間 (集合が空) は true (まだ絞らない。解決後に <see cref="ApplyContractMonthScope"/> で除去)。
    /// </summary>
    private bool IsCurrentMonth(string? symbolCode)
    {
        var codes = CurrentMonthSymbolCodes();
        if (codes.Count == 0) return true;
        return symbolCode is not null && codes.Contains(symbolCode);
    }

    /// <summary>
    /// 限月解決の完了後に呼ぶ。建玉一覧・注文一覧から現月以外 (旧限月) の行を除去する。
    /// 起動時 (解決前) に投入された旧限月の建玉/注文を画面から落とすため。UI スレッドで呼ぶこと。
    /// </summary>
    private void ApplyContractMonthScope()
    {
        var codes = CurrentMonthSymbolCodes();
        if (codes.Count == 0) return;   // 未解決なら絞らない (誤って全消ししない)

        foreach (var row in Positions.Where(p => !codes.Contains(p.SymbolCode)).ToList())
            Positions.Remove(row);
        foreach (var row in Orders.Where(o => !codes.Contains(o.SymbolCode)).ToList())
            Orders.Remove(row);
    }

    // ── 通知ハンドラ ────────────────────────────────────────────

    private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
    {
        _ = OnUiAsync(() =>
        {
            switch (e.Kind)
            {
                case PositionChangeKind.Added when e.Position is not null:
                    if (IsCurrentMonth(e.Position.Symbol.Value))
                        Positions.Add(ToRow(e.Position));
                    break;
                case PositionChangeKind.Updated when e.Position is not null:
                {
                    var existing = Positions.FirstOrDefault(p => p.ExecutionId == e.Position.Id.Value);
                    if (existing is null)
                    {
                        if (IsCurrentMonth(e.Position.Symbol.Value))
                            Positions.Add(ToRow(e.Position));
                    }
                    else UpdateRow(existing, e.Position);
                    break;
                }
                case PositionChangeKind.Removed when e.RemovedId is not null:
                {
                    var existing = Positions.FirstOrDefault(p => p.ExecutionId == e.RemovedId.Value);
                    if (existing is not null) Positions.Remove(existing);
                    break;
                }
            }
        });
    }

    private void OnOrderSnapshotsUpdated(object? sender, OrderSnapshotsEventArgs e)
    {
        _ = OnUiAsync(() =>
        {
            // Clear 禁止: ポーリングは「追跡中の注文のみ」を送ってくるので、Clear すると
            // 既に表示済みの過去注文が消える (起動時 InitialFetch 7件 → ポーリング 1件 で 6件消失バグ)。
            // OrderId をキーに差分マージする。
            // 既存行は「中身だけ更新」する (オブジェクト差し替え禁止: DataGrid の SelectedItem
            // が無効化されてユーザー選択が外れる)。
            foreach (var s in e.Snapshots)
            {
                var meta = _orderMetaStore.TryGet(s.BrokerOrderId.Value);
                var newRow = ToRow(s, meta);
                var existing = Orders.FirstOrDefault(o => o.OrderId == newRow.OrderId);
                if (existing is null)
                {
                    if (IsCurrentMonth(newRow.SymbolCode))
                        Orders.Add(newRow);
                }
                else
                {
                    UpdateOrderRowInPlace(existing, newRow);
                }
            }
        });
    }

    /// <summary>
    /// 既存の <see cref="OrderRow"/> をオブジェクト差し替えせずに in-place 更新する。
    /// DataGrid の選択状態 (SelectedItem) を維持するために必要。
    /// </summary>
    private static void UpdateOrderRowInPlace(OrderRow target, OrderRow source)
    {
        target.SymbolName = source.SymbolName;
        target.SymbolCode = source.SymbolCode;
        target.TradeMode = source.TradeMode;
        target.RecvTime = source.RecvTime;
        target.Strategy = source.Strategy;
        target.Interval = source.Interval;
        target.CashMargin = source.CashMargin;
        target.Side = source.Side;
        target.State = source.State;
        target.OrderQty = source.OrderQty;
        target.CumQty = source.CumQty;
        target.Price = source.Price;
        target.OrderId = source.OrderId;
        target.ExecutionId = source.ExecutionId;
    }

    /// <summary>
    /// 板情報 push を受けて、該当 InstrumentDefinition の価格を更新し、
    /// その銘柄を保有する全 Position の損益を再計算する。
    /// </summary>
    private void OnPriceUpdated(object? sender, PriceTick tick)
    {
        var instrument = AvailableInstruments
            .FirstOrDefault(i => i.ResolvedSymbolCode == tick.Symbol.Value);
        if (instrument is null) return;   // 登録していない銘柄のティックは無視

        _ = OnUiAsync(() =>
        {
            // 1. 銘柄別の最新価格を更新
            instrument.LastPrice = tick.LastPrice.Value;
            instrument.BidPrice = tick.BidPrice.Value;
            instrument.AskPrice = tick.AskPrice.Value;

            // 2. ManualOrderInstrument の表示 (互換のため MainViewModel 直下のプロパティも同期更新)
            if (ReferenceEquals(instrument, ManualOrderInstrument))
            {
                CurrentPrice = instrument.LastPrice;
                BidPrice = instrument.BidPrice;
                AskPrice = instrument.AskPrice;
                BidQty = instrument.BidQty;
                AskQty = instrument.AskQty;
            }

            // 3. 損益再計算: Mini と Micro は同じ原資産 (日経225) でほぼ同価格のため、
            // どちらかの push が来たら登録済み全銘柄の建玉を当該価格で再計算する。
            // (ProfitMultiplier は建玉ごとの銘柄から取得するので Mini=100 / Micro=10 で正しく出る)
            RecalculateAllProfitsWithSharedPrice(tick.LastPrice.Value);
        });
    }

    /// <summary>
    /// 受信した tick LastPrice を全建玉に適用して損益再計算する。
    /// 旧 N225OrderBridge は Mini のみ対応だったが新ブリッジは Mini + Micro 両方対応するため、
    /// どちらか片方の push を両方の建玉に流用する (Mini ↔ Micro は基本同価格)。
    /// </summary>
    private void RecalculateAllProfitsWithSharedPrice(decimal lastPrice)
    {
        if (lastPrice <= 0) return;
        foreach (var row in Positions)
        {
            var inst = AvailableInstruments.FirstOrDefault(i => i.ResolvedSymbolCode == row.SymbolCode);
            if (inst is null) continue;   // 解決不能 (旧月限の建玉等) はスキップ
            var sideSign = row.Side == "買" ? 1m : -1m;
            row.Profit = (lastPrice - row.Price) * sideSign * row.LeaveQty * inst.ProfitMultiplier;
        }
    }

    /// <summary>
    /// 指定銘柄の Positions 各行の損益を再計算。
    /// 計算式: (Current - Entry) × Side係数 × LeaveQty × ProfitMultiplier
    ///   Buy  → Side係数 = +1 (上がれば +)
    ///   Sell → Side係数 = -1 (下がれば +)
    /// </summary>
    private void RecalculateProfitsForSymbol(string symbolCode)
    {
        var instrument = AvailableInstruments.FirstOrDefault(i => i.ResolvedSymbolCode == symbolCode);
        if (instrument is null || instrument.LastPrice == 0m) return;

        var multiplier = instrument.ProfitMultiplier;
        var currentPrice = instrument.LastPrice;
        var matched = 0;

        foreach (var row in Positions.Where(p => p.SymbolCode == symbolCode))
        {
            var sideSign = row.Side == "買" ? 1m : -1m;
            row.Profit = (currentPrice - row.Price) * sideSign * row.LeaveQty * multiplier;
            matched++;
        }

        if (matched == 0)
        {
            // 既存建玉のSymbolCodeと、subscribe済み銘柄が一致しないケース (旧月限の建玉等)
            _logger.LogDebug(
                "損益再計算 (旧 API): {Symbol} 価格={Price} → 該当建玉なし", symbolCode, currentPrice);
        }
    }

    /// <summary>選択中銘柄が変わったとき、表示価格を切り替える + 自動売買 provider を更新する。</summary>
    partial void OnManualOrderInstrumentChanged(InstrumentDefinition? value)
    {
        if (value is null) return;
        _ = OnUiAsync(() =>
        {
            CurrentPrice = value.LastPrice;
            BidPrice = value.BidPrice;
            AskPrice = value.AskPrice;
            BidQty = value.BidQty;
            AskQty = value.AskQty;
        });
        PushSelectedInstrumentToAutoTradeProvider();
    }

    /// <summary>
    /// 現在の <see cref="ManualOrderInstrument"/> を自動売買 provider に反映する。
    /// 銘柄解決完了時 / 選択変更時に呼ぶ。ResolvedSymbolCode が null なら provider も null に戻る (発注経路停止)。
    ///
    /// ⚠️ 運用上の注意 (兼用設計):
    ///   - 本ブリッジは「手動発注パネルで選択中の銘柄 = 自動売買の発注先銘柄」を兼用している。
    ///     ManualOrderInstrument を Mini → Micro に切り替えると、同時に自動売買の発注先も Micro になる。
    ///   - これは「手動発注で Mini を試し打ちしながら自動売買は Micro 継続」のような **分離運用ができない**ことを意味する。
    ///     分離が必要になったら ManualOrderInstrument と AutoTradeInstrument を独立プロパティに分けて
    ///     UI に「自動売買対象」専用 ComboBox を追加する (拡張余地)。
    ///   - 自動売買稼働中 (トグル ON) に銘柄を切り替えるのは事故の元なので、利用者にはトグル OFF を促す運用がよい。
    /// </summary>
    private void PushSelectedInstrumentToAutoTradeProvider()
    {
        var inst = ManualOrderInstrument;
        _autoTradeInstrumentProvider.SetInstrument(
            inst?.ResolvedSymbolCode,
            inst?.DisplayName,
            inst?.ContractMonth);
    }

    /// <summary>
    /// 自動売買対象銘柄の変更通知。ログに残す (運用上の誤発注検出を早めるため)。
    /// </summary>
    private void OnAutoTradeInstrumentChanged(object? sender, EventArgs e)
    {
        var code = _autoTradeInstrumentProvider.ResolvedSymbolCode;
        var name = _autoTradeInstrumentProvider.DisplayName;
        var month = _autoTradeInstrumentProvider.ContractMonth;
        if (string.IsNullOrEmpty(code))
        {
            _logger.LogWarning("自動売買対象銘柄: 未確定 (シンボルコード未解決のため発注経路は停止)");
        }
        else
        {
            _logger.LogInformation(
                "自動売買対象銘柄: {Name} {Month} (シンボルコード={Code})",
                name ?? "(名称不明)", month ?? "(限月不明)", code);
        }
    }

    // ── ドメイン → UI 行変換 ───────────────────────────────────

    /// <summary>
    /// kabu の銘柄コードを AvailableInstruments の DisplayName ("日経225Mini"/"日経225Micro") に変換。
    /// 未解決 (起動直後) / マッチ無しの場合は kabu コードのまま返す。
    /// </summary>
    private string ResolveSymbolDisplay(string symbolCode)
    {
        var hit = AvailableInstruments.FirstOrDefault(i => i.ResolvedSymbolCode == symbolCode);
        return hit?.DisplayName ?? symbolCode;
    }

    private PositionRow ToRow(Position p) => new()
    {
        SymbolName = ResolveSymbolDisplay(p.Symbol.Value),
        SymbolCode = p.Symbol.Value,    // 損益計算で InstrumentDefinition との突合に使う
        TradeMode = p.TradeMode == TradeMode.Auto ? "自動" : "手動",
        ExecutionDay = p.OpenedAt.ToLocalTime().ToString("yyyyMMdd"),
        Strategy = p.Strategy.Value,
        Interval = p.Interval,
        Side = p.Side.ToDisplay(),
        LeaveQty = p.LeaveQuantity.Value,
        HoldQty = p.HoldQuantity.Value,
        Price = p.EntryPrice.Value,
        Profit = 0m,    // 価格 push が来た時に RecalculateProfits() で更新
        ExecutionId = p.Id.Value,
        OrderId = string.Empty
    };

    private static void UpdateRow(PositionRow row, Position p)
    {
        row.LeaveQty = p.LeaveQuantity.Value;
        row.HoldQty = p.HoldQuantity.Value;
    }

    /// <summary>
    /// Accept 直後の <see cref="Order"/> 集約から OrderRow を組み立て、注文一覧へ即時追加する。
    /// ポーリング (1 秒間隔) の取りこぼし対策。既存行があれば更新で済ませる。
    /// </summary>
    private void AddOrUpdateOrderRowFromOrder(Order order)
    {
        if (order.BrokerOrderId is null) return;
        var brokerOrderId = order.BrokerOrderId.Value;
        var newRow = new OrderRow
        {
            SymbolName = ResolveSymbolDisplay(order.Symbol.Value),
            SymbolCode = order.Symbol.Value,
            TradeMode = order.TradeMode == TradeMode.Auto ? "自動" : "手動",
            RecvTime = order.CreatedAt.ToLocalTime().ToString("HH:mm:ss.fff"),
            Strategy = order.Strategy.Value,
            Interval = order.Interval,
            CashMargin = order.TradeType == TradeType.ExitOrder ? "返済" : "新規",
            Side = order.Side.ToDisplay(),
            State = MapState(order.State),
            OrderQty = order.RequestedQuantity.Value,
            CumQty = order.CumulativeExecutedQuantity.Value,
            Price = order.LimitPrice.Value,
            OrderId = brokerOrderId,
            ExecutionId = order.TargetExecutionId?.Value ?? string.Empty
        };
        _ = OnUiAsync(() =>
        {
            var existing = Orders.FirstOrDefault(o => o.OrderId == brokerOrderId);
            if (existing is null)
            {
                if (IsCurrentMonth(newRow.SymbolCode)) Orders.Add(newRow);
            }
            else UpdateOrderRowInPlace(existing, newRow);   // 差し替え禁止 (選択維持のため)
        });
    }

    /// <summary>
    /// kabu の OrderSnapshot + (このブリッジ発注なら) OrderMetadata で 1 行を組み立てる。
    /// メタが無ければ「手動 / 戦略不明」(外部発注 = 旧 N225OrderBridge や手動証券画面からの発注)。
    /// </summary>
    private OrderRow ToRow(OrderSnapshot s, OrderMetadata? meta) => new()
    {
        SymbolName = ResolveSymbolDisplay(s.Symbol.Value),
        SymbolCode = s.Symbol.Value,
        TradeMode = meta is null
            ? "手動"
            : (meta.TradeMode == "Auto" ? "自動" : "手動"),
        RecvTime = s.CreatedAt.ToLocalTime().ToString("HH:mm:ss.fff"),
        Strategy = meta?.Strategy ?? string.Empty,
        Interval = meta?.Interval ?? 0,
        CashMargin = s.TradeType == TradeType.ExitOrder ? "返済" : "新規",
        Side = s.Side.ToDisplay(),
        State = MapState(s.State),
        OrderQty = s.RequestedQuantity.Value,
        CumQty = s.ExecutedQuantity.Value,
        Price = s.Price.Value,
        OrderId = s.BrokerOrderId.Value,
        ExecutionId = meta?.TargetExecutionId ?? string.Empty
    };

    private static string MapState(OrderState state) => state switch
    {
        OrderState.Created => "作成",
        OrderState.Submitted => "照会中",   // kabu State=1/2/3+cumQty=0 (受付済み・約定待ち)
        OrderState.PartiallyFilled => "一部約定",
        OrderState.Filled => "約定済",
        OrderState.Cancelled => "取消",
        OrderState.Expired => "失効",
        OrderState.Rejected => "拒否",
        _ => state.ToString()
    };

    private static N225BrokerBridge.Domain.Orders.TimeInForce ParseTif(string s) => s switch
    {
        "FAK" => N225BrokerBridge.Domain.Orders.TimeInForce.FAK,
        "FOK" => N225BrokerBridge.Domain.Orders.TimeInForce.FOK,
        _ => N225BrokerBridge.Domain.Orders.TimeInForce.FAS
    };

    /// <summary>WPF UI スレッドに marshal して action を実行 (Dispatcher 不在テスト時はインライン)。</summary>
    private static System.Threading.Tasks.Task OnUiAsync(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            return dispatcher.InvokeAsync(action).Task;
        action();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    // ── 発注コマンド (UseCase 経由で実発注) ─────────────────────

    [RelayCommand]
    private Task PlaceBuyOrder() => PlaceManualNewOrderAsync(Side.Buy);

    [RelayCommand]
    private Task PlaceSellOrder() => PlaceManualNewOrderAsync(Side.Sell);

    private async Task PlaceManualNewOrderAsync(Side side)
    {
        _logger.LogInformation(
            "PlaceManualNewOrder invoked side={Side} qty={Qty} type={Type} symbol={Symbol}",
            side, OrderQty, OrderType, ManualOrderInstrument?.ResolvedSymbolCode ?? "(null)");

        if (ManualOrderInstrument?.ResolvedSymbolCode is null)
        {
            StateMessage = "⚠ 銘柄が解決されていません (検証ポートでは銘柄解決不可)";
            return;
        }
        if (OrderQty <= 0)
        {
            StateMessage = "⚠ 数量は 1 以上を指定してください";
            return;
        }

        try
        {
            // UI の OrderType (Market/BestMarket/Limit/Stop) を Domain の OrderType + 価格に対応付ける。
            // 旧 N225OrderBridge MarketOrder/BestMarketOder/LimitOrder/StopOrder のロジック踏襲:
            //   Market    → FrontOrderType=120 (成行)、Price=0、TIF=FAK
            //   BestMarket→ FrontOrderType=20  (指値)、Price=現在 Bid(Buy)/Ask(Sell)、TIF=FAS
            //   Limit     → FrontOrderType=20  (指値)、Price=ユーザー入力 LimitPrice、TIF=FAS
            //   Stop      → FrontOrderType=30  (逆指値)、Price=0、AfterHitPrice=StopPrice、TIF=FAK
            //              (※ Stop は現状 ReverseLimitOrder ブロック未対応で API は受け付けない見込み)
            OrderType domainType;
            TimeInForce domainTif;
            Price price;
            switch (OrderType)
            {
                case OrderTypeChoice.Market:
                    domainType = N225BrokerBridge.Domain.Orders.OrderType.Market;
                    domainTif = N225BrokerBridge.Domain.Orders.TimeInForce.FAK;
                    price = Price.Zero;
                    break;
                case OrderTypeChoice.BestMarket:
                    domainType = N225BrokerBridge.Domain.Orders.OrderType.Limit;   // 仕様上 20=指値
                    domainTif = N225BrokerBridge.Domain.Orders.TimeInForce.FAS;
                    var bestPrice = side == Side.Buy
                        ? ManualOrderInstrument.BidPrice
                        : ManualOrderInstrument.AskPrice;
                    if (bestPrice <= 0)
                    {
                        StateMessage = "⚠ 対当発注に必要な BID/ASK 価格が未取得です (板情報を待ってから再試行してください)";
                        return;
                    }
                    price = new Price(bestPrice);
                    break;
                case OrderTypeChoice.Limit:
                    domainType = N225BrokerBridge.Domain.Orders.OrderType.Limit;
                    domainTif = ParseTif(SelectedTimeInForce);
                    if (LimitPrice <= 0)
                    {
                        StateMessage = "⚠ 指値価格を 1 以上で入力してください";
                        return;
                    }
                    price = new Price((decimal)LimitPrice);
                    break;
                case OrderTypeChoice.Stop:
                    domainType = N225BrokerBridge.Domain.Orders.OrderType.Stop;
                    domainTif = N225BrokerBridge.Domain.Orders.TimeInForce.FAK;
                    price = Price.Zero;
                    if (StopPrice <= 0)
                    {
                        StateMessage = "⚠ 逆指値価格を 1 以上で入力してください";
                        return;
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unknown OrderType: {OrderType}");
            }

            // 逆指値発注時のみトリガー価格を Domain に伝搬する (それ以外は null = 既定値 Price.Zero)
            Price? stopPriceArg = OrderType == OrderTypeChoice.Stop
                ? new Price((decimal)StopPrice)
                : null;

            // 確認ダイアログ (手動操作専用、設定で OFF にできる。デフォルト ON)
            // ※ Webhook 経由の自動発注は SignalHandler → PlaceNewOrderUseCase 直呼びで
            //   この MainViewModel を通らないため、自動発注では確認ダイアログは出ない。
            if (_localSettingsStore.Load().RequireConfirmBeforeOrder)
            {
                var sideLabel = side == Side.Buy ? "買 (新規買)" : "売 (新規売)";
                var orderTypeLabel = OrderType switch
                {
                    OrderTypeChoice.Market => "成行",
                    OrderTypeChoice.BestMarket => $"対当（指値 {price.Value:0}）",
                    OrderTypeChoice.Limit => $"指値 {price.Value:0}",
                    OrderTypeChoice.Stop => $"逆指値 {stopPriceArg?.Value:0}",
                    _ => OrderType.ToString()
                };
                var symbolLabel = !string.IsNullOrWhiteSpace(ManualOrderInstrument.DisplayName)
                    ? $"{ManualOrderInstrument.DisplayName}（{ManualOrderInstrument.ResolvedSymbolCode}）"
                    : ManualOrderInstrument.ResolvedSymbolCode;
                var simulatorPrefix = N225BrokerBridge.UI.App.IsSimulatorMode
                    ? "【シミュレータ】Mock ブローカーへ発注します (実発注ではありません)。\n\n"
                    : string.Empty;
                var message =
                    $"{simulatorPrefix}以下の内容で新規発注します。よろしいですか？\n\n" +
                    $"  サイド       : {sideLabel}\n" +
                    $"  銘柄         : {symbolLabel}\n" +
                    $"  数量         : {(int)OrderQty} 枚\n" +
                    $"  注文タイプ   : {orderTypeLabel}\n" +
                    $"  時間条件     : {SelectedTimeInForce}\n\n" +
                    $"※確認ダイアログは [設定 → 動作] でオフにできます。";
                var dialogTitle = N225BrokerBridge.UI.App.IsSimulatorMode ? "新規発注の確認 (シミュレータ)" : "新規発注の確認";
                var dlg = MessageBox.Show(
                    message,
                    dialogTitle,
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question,
                    MessageBoxResult.Cancel);
                if (dlg != MessageBoxResult.OK)
                {
                    StateMessage = $"{side.ToDisplay()}注文をキャンセルしました。";
                    return;
                }
            }

            var intent = new NewOrderIntent(
                Strategy: new StrategyName("Manual"),    // 手動発注は固定戦略名
                Interval: 0,                              // 手動発注は時間足無関係 (TradeMode.Manual のとき Order 不変条件で許容)
                TradeMode: TradeMode.Manual,
                Symbol: new SymbolCode(ManualOrderInstrument.ResolvedSymbolCode),
                Side: side,
                Quantity: new Quantity((int)OrderQty),
                OrderPrice: price,
                OrderType: domainType,
                TimeInForce: domainTif,
                StopPrice: stopPriceArg);

            var result = await _placeNewOrderUseCase.ExecuteAsync(intent);

            // Accept 直後にUIへ追加 (ポーリング取りこぼし対策)
            if (result.Status == OrderResultStatus.Accepted)
                AddOrUpdateOrderRowFromOrder(result.Order);

            StateMessage = $"{side.ToDisplay()}注文: {result.Status} {(result.Order.BrokerOrderId is not null ? $"OrderId={result.Order.BrokerOrderId}" : result.ErrorMessage)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual new order failed side={Side}", side);
            StateMessage = $"{side.ToDisplay()}注文エラー: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExitPosition()
    {
        // 旧 TradeView.ExitOrderButton_Click と同じ:
        // 1. 建玉一覧で選択された行の ExecutionID を取り出す
        // 2. UI の OrderQty を返済数量として使用 (UI の発注枚数フィールドと連動)
        // 3. ManualClosePositionUseCase で当該建玉を返済
        if (SelectedPosition is null)
        {
            StateMessage = "返済するポジションを選択してください。";
            return;
        }
        if (string.IsNullOrEmpty(SelectedPosition.ExecutionId))
        {
            StateMessage = "選択された建玉に ExecutionID がありません";
            return;
        }
        if (OrderQty < 1)
        {
            StateMessage = "⚠ 返済数量は 1 以上を指定してください";
            return;
        }

        try
        {
            var execId = new ExecutionId(SelectedPosition.ExecutionId);
            var closeQty = new Quantity((int)OrderQty);

            // 新規発注と同じく、UI で選択中の OrderType を返済にも適用する。
            // 返済の Side は建玉と反対 (建玉 売 → 返済 買)。
            // 対当 (BestMarket) の場合は close 側に応じて kabu API の Bid/Ask を選択。
            // ※注意: kabu API は「BidPrice=Sell1価格(=通常のASK), AskPrice=Buy1価格(=通常のBID)」と
            //   トレーダー目線で命名されている (kabu API 仕様より)。よって:
            //     close=Buy   → kabu BidPrice (= 通常 ASK) を hit
            //     close=Sell  → kabu AskPrice (= 通常 BID) を hit
            //   これは BestMarketOder.cs (旧 N225OrderBridge) と同じ。
            var closeSideIsBuy = SelectedPosition.Side == "売";   // 売建玉 → 返済は買い
            OrderType domainType;
            TimeInForce domainTif;
            Price? limitPriceArg = null;
            Price? stopPriceArg = null;

            // 返済する建玉の銘柄に対応する InstrumentDefinition を取得 (価格参照用)
            var instrument = AvailableInstruments.FirstOrDefault(i =>
                i.ResolvedSymbolCode == SelectedPosition.SymbolCode)
                ?? ManualOrderInstrument;

            switch (OrderType)
            {
                case OrderTypeChoice.Market:
                    domainType = N225BrokerBridge.Domain.Orders.OrderType.Market;
                    domainTif = N225BrokerBridge.Domain.Orders.TimeInForce.FAK;
                    break;
                case OrderTypeChoice.BestMarket:
                    domainType = N225BrokerBridge.Domain.Orders.OrderType.Limit;   // 仕様上 20=指値
                    domainTif = N225BrokerBridge.Domain.Orders.TimeInForce.FAS;
                    var bestPrice = closeSideIsBuy
                        ? (instrument?.BidPrice ?? 0m)
                        : (instrument?.AskPrice ?? 0m);
                    if (bestPrice <= 0)
                    {
                        StateMessage = "⚠ 対当返済に必要な BID/ASK 価格が未取得です (板情報を待ってから再試行)";
                        return;
                    }
                    limitPriceArg = new Price(bestPrice);
                    break;
                case OrderTypeChoice.Limit:
                    domainType = N225BrokerBridge.Domain.Orders.OrderType.Limit;
                    domainTif = ParseTif(SelectedTimeInForce);
                    if (LimitPrice <= 0)
                    {
                        StateMessage = "⚠ 指値価格を 1 以上で入力してください";
                        return;
                    }
                    limitPriceArg = new Price((decimal)LimitPrice);
                    break;
                case OrderTypeChoice.Stop:
                    domainType = N225BrokerBridge.Domain.Orders.OrderType.Stop;
                    domainTif = N225BrokerBridge.Domain.Orders.TimeInForce.FAK;
                    if (StopPrice <= 0)
                    {
                        StateMessage = "⚠ 逆指値価格を 1 以上で入力してください";
                        return;
                    }
                    stopPriceArg = new Price((decimal)StopPrice);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown OrderType: {OrderType}");
            }

            // 確認ダイアログ (手動操作専用、設定で OFF にできる。デフォルト ON)
            // ※ Webhook 経由の自動返済は SignalHandler → ClosePositionUseCase 直呼びで
            //   この MainViewModel を通らないため、自動発注では確認ダイアログは出ない。
            // バグ収束後に「設定 → 動作 → 手動操作の前に確認ダイアログを表示する」を
            // OFF にすればワンクリック発注になる。
            var requireConfirm = _localSettingsStore.Load().RequireConfirmBeforeOrder;
            if (requireConfirm)
            {
                var closeSideLabel = closeSideIsBuy ? "買戻し" : "売返し";

                // 注文タイプ表示:
                //   成行     : 価格なし
                //   対当     : kabu API 仕様上 OrderType=Limit + 最良気配の指値発注 → 価格表示
                //   指値     : ユーザー入力の指値価格
                //   逆指値   : ユーザー入力のトリガー価格
                var orderTypeLabel = OrderType switch
                {
                    OrderTypeChoice.Market => "成行",
                    OrderTypeChoice.BestMarket => $"対当（指値 {limitPriceArg?.Value:0}）",
                    OrderTypeChoice.Limit => $"指値 {limitPriceArg?.Value:0}",
                    OrderTypeChoice.Stop => $"逆指値 {stopPriceArg?.Value:0}",
                    _ => OrderType.ToString()
                };

                // 銘柄名: SymbolName (日経225ミニ / 日経225マイクロ等) + コードを併記
                var symbolLabel = !string.IsNullOrWhiteSpace(SelectedPosition.SymbolName)
                    ? $"{SelectedPosition.SymbolName}（{SelectedPosition.SymbolCode}）"
                    : SelectedPosition.SymbolCode;

                var simulatorPrefix = N225BrokerBridge.UI.App.IsSimulatorMode
                    ? "【シミュレータ】Mock ブローカーへ返済発注します (実発注ではありません)。\n\n"
                    : string.Empty;
                var message =
                    $"{simulatorPrefix}以下の内容で返済発注します。よろしいですか？\n\n" +
                    $"  建玉サイド   : {SelectedPosition.Side}建\n" +
                    $"  銘柄         : {symbolLabel}\n" +
                    $"  建玉残       : {SelectedPosition.LeaveQty} 枚\n" +
                    $"  返済数量     : {closeQty.Value} 枚 ({closeSideLabel})\n" +
                    $"  注文タイプ   : {orderTypeLabel}\n" +
                    $"  時間条件     : {SelectedTimeInForce}\n\n" +
                    $"※確認ダイアログは [設定 → 動作] でオフにできます。";
                var dialogTitle = N225BrokerBridge.UI.App.IsSimulatorMode ? "返済発注の確認 (シミュレータ)" : "返済発注の確認";
                var dlg = MessageBox.Show(
                    message,
                    dialogTitle,
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question,
                    MessageBoxResult.Cancel);   // 既定はキャンセル (Enter 連打事故防止)
                if (dlg != MessageBoxResult.OK)
                {
                    StateMessage = "返済発注をキャンセルしました。";
                    return;
                }
            }

            var result = await _manualCloseUseCase.ExecuteAsync(
                execId,
                quantity: closeQty,
                orderType: domainType,
                limitPrice: limitPriceArg,
                stopPrice: stopPriceArg,
                timeInForce: domainTif);

            // Accept 直後にUIへ追加 (ポーリング取りこぼし対策)
            if (result.Status == OrderResultStatus.Accepted && result.ExitOrder is not null)
                AddOrUpdateOrderRowFromOrder(result.ExitOrder);

            StateMessage = $"返済: {result.Status} target={execId}" +
                (result.ErrorMessage is not null ? $" ({result.ErrorMessage})" : "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual close failed execId={Id}", SelectedPosition.ExecutionId);
            StateMessage = $"返済エラー: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CancelOrder()
    {
        if (SelectedOrder is null)
        {
            StateMessage = "キャンセルする注文を選択してください";
            return;
        }
        if (string.IsNullOrEmpty(SelectedOrder.OrderId))
        {
            StateMessage = "選択された注文に OrderID がありません";
            return;
        }

        try
        {
            var orderId = new OrderId(SelectedOrder.OrderId);

            // 確認ダイアログ (手動操作専用、設定で OFF にできる。デフォルト ON)
            // ※ Webhook 経由ではキャンセルが発生する経路はないが、手動キャンセルのみ確認対象。
            if (_localSettingsStore.Load().RequireConfirmBeforeOrder)
            {
                var symbolLabel = !string.IsNullOrWhiteSpace(SelectedOrder.SymbolName)
                    ? SelectedOrder.SymbolName
                    : "(不明)";
                var priceLabel = SelectedOrder.Price > 0
                    ? $"{SelectedOrder.Price:0}"
                    : "成行";
                var message =
                    $"以下の注文をキャンセルします。よろしいですか？\n\n" +
                    $"  注文ID       : {SelectedOrder.OrderId}\n" +
                    $"  銘柄         : {symbolLabel}\n" +
                    $"  サイド       : {SelectedOrder.Side}\n" +
                    $"  数量         : {SelectedOrder.OrderQty} 枚 (うち約定済 {SelectedOrder.CumQty} 枚)\n" +
                    $"  価格         : {priceLabel}\n" +
                    $"  状態         : {SelectedOrder.State}\n\n" +
                    $"※確認ダイアログは [設定 → 動作] でオフにできます。";
                var dlg = MessageBox.Show(
                    message,
                    "注文キャンセルの確認",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question,
                    MessageBoxResult.Cancel);
                if (dlg != MessageBoxResult.OK)
                {
                    StateMessage = "キャンセルを取りやめました。";
                    return;
                }
            }

            var result = await _broker.CancelOrderAsync(orderId);
            StateMessage = $"キャンセル: {result.Status} OrderId={orderId}" +
                (result.ErrorMessage is not null ? $" ({result.ErrorMessage})" : "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cancel order failed orderId={Id}", SelectedOrder.OrderId);
            StateMessage = $"キャンセルエラー: {ex.Message}";
        }
    }
}

public enum OrderTypeChoice
{
    BestMarket,
    Market,
    Limit,
    Stop
}

public partial class StrategyRow : ObservableObject
{
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _alertName = string.Empty;
    [ObservableProperty] private int _interval;

    // ── 最終受信シグナルのコンテキスト (旧 StrategyViewList 相当) ──
    [ObservableProperty] private string _lastSignalAt = string.Empty;   // 受信日時 (HH:mm:ss 表示)
    [ObservableProperty] private string _lastTradeType = string.Empty;  // 新規/返済/ドテン
    [ObservableProperty] private string _lastSide = string.Empty;       // 買/売
    [ObservableProperty] private string _lastPrice = string.Empty;      // 価格 (0 なら "成行")
    [ObservableProperty] private string _description = string.Empty;
}

/// <summary>
/// 建玉一覧 1 行。
/// 旧 N225OrderBridge の PositionListView と同じ列構成を維持する。
/// </summary>
public partial class PositionRow : ObservableObject
{
    [ObservableProperty] private string _symbolName = string.Empty;   // 銘柄表示名
    /// <summary>kabu の銘柄コード (損益計算で InstrumentDefinition と突合するキー)。</summary>
    public string SymbolCode { get; set; } = string.Empty;
    [ObservableProperty] private string _tradeMode = string.Empty;    // モード (自動/手動)
    [ObservableProperty] private string _executionDay = string.Empty; // 約定日
    [ObservableProperty] private string _strategy = string.Empty;     // 戦略
    [ObservableProperty] private int _interval;                       // 足
    [ObservableProperty] private string _side = string.Empty;         // 売買
    [ObservableProperty] private int _leaveQty;                       // 残数量
    [ObservableProperty] private int _holdQty;                        // 注文中
    [ObservableProperty] private decimal _price;                      // 建値
    [ObservableProperty] private decimal _profit;                     // 損益
    [ObservableProperty] private string _executionId = string.Empty;  // 約定ID
    [ObservableProperty] private string _orderId = string.Empty;      // 注文ID
}

/// <summary>
/// 注文一覧 1 行。旧 OrderListView と同じ列構成。
/// </summary>
public partial class OrderRow : ObservableObject
{
    [ObservableProperty] private string _symbolName = string.Empty;
    /// <summary>kabu の銘柄コード (限月スコープのフィルタキー)。表示名には使わない。</summary>
    public string SymbolCode { get; set; } = string.Empty;
    [ObservableProperty] private string _tradeMode = string.Empty;
    [ObservableProperty] private string _recvTime = string.Empty;
    [ObservableProperty] private string _strategy = string.Empty;
    [ObservableProperty] private int _interval;
    [ObservableProperty] private string _cashMargin = string.Empty;
    [ObservableProperty] private string _side = string.Empty;
    [ObservableProperty] private string _state = string.Empty;
    [ObservableProperty] private int _orderQty;
    [ObservableProperty] private int _cumQty;
    [ObservableProperty] private decimal _price;
    [ObservableProperty] private string _orderId = string.Empty;
    [ObservableProperty] private string _executionId = string.Empty;
}
