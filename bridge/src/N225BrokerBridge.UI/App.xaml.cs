using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Application.DI;
using N225BrokerBridge.Infrastructure.Brokers.Kabu;
using N225BrokerBridge.Infrastructure.Brokers.Mock;
using N225BrokerBridge.Infrastructure.DI;
using N225BrokerBridge.Infrastructure.Webhooks;
using N225BrokerBridge.UI.Services;
using N225BrokerBridge.UI.ViewModels;
using N225BrokerBridge.UI.Views;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace N225BrokerBridge.UI;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    /// <summary>
    /// デモモードフラグ。<c>N225BrokerBridge.UI.exe --demo</c> 形式で起動した時のみ true。
    ///
    /// === デモモードとは ===
    /// 「kabu API にも Webhook にも一切繋がない状態でブリッジ UI を立ち上げ、
    /// 画面の各パネル (戦略一覧/建玉/注文/ログ/ステータス/現在値) に
    /// あらかじめハードコードしたサンプルデータを表示する」専用モード。
    ///
    /// === 用途 ===
    /// ・ブログ記事 / 利用マニュアル用のスクリーンショット撮影
    /// ・UI レイアウト調整時の動作確認 (本番口座を一切触らない)
    /// ・新規参加者へのデモ説明 (誤発注リスクなし)
    ///
    /// === 本番への影響 ===
    /// このフラグが false (= 通常起動) のときは、追加した分岐 (if 文) は
    /// 全て従来パスを通るので、挙動は本機能追加前と完全に同一。
    /// デモモードで触ったデータは <c>strategies.json</c> や <c>auto-positions.json</c> 等の
    /// 永続化ファイルには一切書き戻されない (画面の ObservableCollection を直接書き換えるだけ)。
    ///
    /// === 関連実装 ===
    /// ・本フラグの設定: <see cref="OnStartup"/> 冒頭
    /// ・バックグラウンドサービス起動の抑止: <see cref="BootstrapAsync"/> 末尾の if 分岐
    /// ・サンプルデータの流し込み: <see cref="ViewModels.MainViewModel.SeedDemoData"/>
    /// ・ドキュメント: <c>docs/demo-mode.md</c>
    /// </summary>
    public static bool IsDemoMode { get; private set; }

    /// <summary>
    /// シミュレータモードフラグ。<c>N225BrokerBridge.UI.exe --simulator</c> 形式で起動した時のみ true。
    ///
    /// === シミュレータモードとは ===
    /// kabu Station にも本物の TradingView にも繋がず、ブリッジの全フロー
    /// (Webhook 受信 → 発注 → 約定 → 建玉計上 → 返済) を実際に動かして体験できるモード。
    /// IBrokerAdapter 実装を KabuAdapter から MockBrokerAdapter に DI で差し替える。
    ///
    /// 詳細仕様は docs/simulator-mode.md。
    ///
    /// === デモモードとの違い ===
    /// - --demo: バックグラウンドサービスを起動しない (UI 表示のみ、Webhook 受信不可)
    /// - --simulator: バックグラウンドサービスは起動 + 外部接続のみ Mock 化
    ///
    /// === 同時指定時の挙動 ===
    /// --demo と --simulator を両方指定したら --simulator を優先 (IsDemoMode を false に上書き)。
    /// </summary>
    public static bool IsSimulatorMode { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ★ モード判定 ★
        // exe 起動引数に "--demo" / "--simulator" (大文字小文字問わず) が含まれているか確認。
        // 通常起動 (引数なし) では両方 false → 既存の本番挙動を維持。
        IsDemoMode      = e.Args.Any(a => string.Equals(a, "--demo",      StringComparison.OrdinalIgnoreCase));
        IsSimulatorMode = e.Args.Any(a => string.Equals(a, "--simulator", StringComparison.OrdinalIgnoreCase));
        // 同時指定は --simulator 優先 (docs/simulator-mode.md §15 #7 で確定)
        if (IsDemoMode && IsSimulatorMode)
        {
            IsDemoMode = false;
        }

        // 例外を握りつぶさないグローバルハンドラ
        DispatcherUnhandledException += (_, ev) =>
        {
            ShowFatal(ev.Exception, "DispatcherUnhandledException");
            ev.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            ShowFatal(ev.ExceptionObject as Exception, "UnhandledException");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            ShowFatal(ev.Exception, "UnobservedTaskException");
            ev.SetObserved();
        };

        try
        {
            await BootstrapAsync();
        }
        catch (Exception ex)
        {
            ShowFatal(ex, "OnStartup");
        }
    }

    private async Task BootstrapAsync()
    {
        var uiLogSink = new UiLogSink();

        // ログ保存先も LOCALAPPDATA に統一
        var localAppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "N225BrokerBridge");
        var logDir = Path.Combine(localAppDataDir, "logs");

        // Serilog: Console + File + UI Sink
        // Microsoft.Extensions.Http / System.Net.Http の HTTP ハンドラ INF ログ
        // (毎リクエスト 4 行) はノイズなので Warning 以上に抑制する。
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logDir, "n225brokerbridge-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.Sink(uiLogSink)
            .CreateLogger();

        // 旧 N225OrderBridge の "Application Start" に相当する総括ログ
        Log.Information("====================================================");
        Log.Information("N225BrokerBridge 起動を開始します。 ログ保存先={LogDir}", logDir);
        Log.Information("====================================================");

        if (IsSimulatorMode)
        {
            Log.Information("####################################################");
            Log.Information("# シミュレータモード (--simulator) で起動します。       ");
            Log.Information("# kabu API には接続しません (MockBrokerAdapter 使用)。 ");
            Log.Information("# 永続化先は *.simulator.json で本番と分離されます。   ");
            Log.Information("# 詳細仕様: docs/simulator-mode.md                    ");
            Log.Information("####################################################");
        }

        // 機密設定は %LOCALAPPDATA% から DPAPI 復号して読み込み
        // シミュレータモードでは appsettings.Local.simulator.json から読み込み (本番と分離)
        Log.Information("ローカル設定を読み込み中 (DPAPI 暗号化)...");
        var localSettingsStore = new LocalSettingsStore(BrokerBridgePersistencePaths.LocalSettings(IsSimulatorMode));
        var localSettings = localSettingsStore.Load();
        var kabuEnv = localSettings.KabuEnvironment ?? KabuEnvironments.Production;
        var envLabel = kabuEnv == KabuEnvironments.Verification ? "検証 (18081)" : "本番 (18080)";
        Log.Information(
            "ローカル設定読み込み完了: kabu 環境={Env} / Webhook ポート={Port} / 保存済みパスワード=[{PassStatus}]",
            envLabel,
            localSettings.WebhookPort,
            (!string.IsNullOrEmpty(localSettings.WebhookPassphrase) ? "Webhookパスフレーズ " : "") +
            (!string.IsNullOrEmpty(localSettings.KabuApiPassword) ? "本番API " : "") +
            (!string.IsNullOrEmpty(localSettings.KabuApiPasswordTest) ? "検証API " : "") +
            (!string.IsNullOrEmpty(localSettings.KabuOrderPassword) ? "取引暗証番号" : ""));

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, cfg) =>
            {
                // 本番デフォルト値は exe ディレクトリの appsettings.json から
                cfg.SetBasePath(AppContext.BaseDirectory);
                cfg.AddJsonFile("appsettings.json", optional: false);
            })
            .ConfigureServices((ctx, services) =>
            {
                services.Configure<WebhookListenerOptions>(ctx.Configuration.GetSection("Webhook"));
                services.Configure<KabuOptions>(ctx.Configuration.GetSection("Kabu"));

                // 機密 (passphrase / kabu パスワード) は LocalSettingsStore から後注入
                services.PostConfigure<WebhookListenerOptions>(opts =>
                {
                    if (localSettings.WebhookPassphrase is not null) opts.Passphrase = localSettings.WebhookPassphrase;
                    if (localSettings.WebhookPort is int p) opts.Port = p;
                });
                services.PostConfigure<KabuOptions>(opts =>
                {
                    // kabu Station の接続環境を本番/検証で切り替える。
                    // 検証ポート (18081) は実発注されないモック応答のため、旧 N225OrderBridge と
                    // 並行運用してもダブル発注リスクなし。
                    var env = localSettings.KabuEnvironment ?? KabuEnvironments.Production;
                    if (env == KabuEnvironments.Verification)
                    {
                        opts.BaseUrl = KabuEnvironments.BaseUrlFor(env);
                        opts.WebSocketUrl = KabuEnvironments.WebSocketUrlFor(env);
                        if (localSettings.KabuApiPasswordTest is not null) opts.ApiPassword = localSettings.KabuApiPasswordTest;
                    }
                    else
                    {
                        // 本番: appsettings.json の既定値 (18080) を維持しつつ ApiPassword だけ後注入
                        if (localSettings.KabuApiPassword is not null) opts.ApiPassword = localSettings.KabuApiPassword;
                    }
                    if (localSettings.KabuOrderPassword is not null) opts.OrderPassword = localSettings.KabuOrderPassword;
                });

                services.AddLogging(b =>
                {
                    b.ClearProviders();
                    b.AddProvider(new SerilogLoggerProvider(Log.Logger, dispose: false));
                });

                services.AddBrokerBridgeApplication(localSettings.WebhookPassphrase);
                services.AddBrokerBridgeInfrastructure(simulatorMode: IsSimulatorMode);
                if (IsSimulatorMode)
                {
                    // シミュレータ: Mock ブローカーで kabu 接続を完全置換
                    services.AddBrokerBridgeMockBroker();
                }
                else
                {
                    services.AddBrokerBridgeKabu();
                }
                services.AddBrokerBridgeWebhook();

                services.AddSingleton(uiLogSink);
                services.AddSingleton(localSettingsStore);
                services.AddSingleton<UILayoutStore>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsWindow>();
                services.AddTransient<StrategyManagerViewModel>();
                services.AddTransient<StrategyManagerWindow>();
                services.AddTransient<PositionHistoryViewModel>();
                services.AddTransient<PositionHistoryWindow>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        // ★ デモモード分岐 ★
        // バックグラウンドサービス (kabu API クライアント / Webhook listener / 注文ポーリング /
        // Position reconciliation / WebSocket 板情報受信) は _host.StartAsync() で一括起動される。
        //
        // デモモード時はこれを呼ばないことで、外部との通信ラインを物理的に遮断する:
        //   - kabu Station への接続なし → 本番口座の建玉/残高は一切取得しない・触らない
        //   - Webhook listener (ポート 8001) は LISTEN しない → 外部からの POST を一切受け付けない
        //   - 注文ポーリングも走らない → 偽の "約定検出" 等が混入しない
        //
        // 通常モード (IsDemoMode=false) は変更前と完全に同じ挙動 (await _host.StartAsync())。
        if (IsDemoMode)
        {
            Log.Information("====================================================");
            Log.Information("デモモードで起動します (kabu/Webhook 接続なし、決め打ちデータ表示)。");
            Log.Information("バックグラウンドサービスは一切起動しません。");
            Log.Information("実際のデータ流し込みは MainViewModel.SeedDemoData() で実行。");
            Log.Information("====================================================");
        }
        else
        {
            Log.Information("DI コンテナ構築完了。バックグラウンドサービスを起動します...");
            await _host.StartAsync();
            Log.Information("全バックグラウンドサービス起動完了。");
        }

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        Log.Information("====================================================");
        Log.Information(IsDemoMode
            ? "N225BrokerBridge 起動完了 (デモモード)。"
            : (IsSimulatorMode
                ? "N225BrokerBridge 起動完了 (シミュレータモード)。"
                : "N225BrokerBridge 起動完了。運用中。"));
        Log.Information("====================================================");
    }

    private static void ShowFatal(Exception? ex, string source)
    {
        var msg = $"[{source}] {ex?.GetType().FullName}: {ex?.Message}\n\n{ex?.StackTrace}";
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "startup-error.txt"), msg);
        }
        catch { /* ignore */ }
        MessageBox.Show(msg, "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
