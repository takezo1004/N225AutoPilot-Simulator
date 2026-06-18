using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Application.Signals;
using N225BrokerBridge.Application.Sync;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.Positions;
using N225BrokerBridge.Infrastructure.Brokers.Kabu;
using N225BrokerBridge.Infrastructure.Brokers.Mock;
using N225BrokerBridge.Infrastructure.Persistence;
using N225BrokerBridge.Infrastructure.Strategies;
using N225BrokerBridge.Infrastructure.Webhooks;

namespace N225BrokerBridge.Infrastructure.DI;

/// <summary>
/// Infrastructure 層のサービス登録拡張メソッド。
/// 初期段階ではメモリ内リポジトリを登録。将来 SQLite 実装に差し替え可能。
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddBrokerBridgeInfrastructure(
        this IServiceCollection services,
        bool simulatorMode = false)
    {
        // 永続化 (メモリ実装、Singleton で全ライフタイム共有)
        // IPositionRepository / IOrderRepository と IPositionChangeNotifier / IOrderChangeNotifier の
        // 両方を同一インスタンスで解決させる (具象クラスは Singleton 登録 → forward)
        services.AddSingleton<InMemoryOrderRepository>();
        services.AddSingleton<IOrderRepository>(sp => sp.GetRequiredService<InMemoryOrderRepository>());
        services.AddSingleton<IOrderChangeNotifier>(sp => sp.GetRequiredService<InMemoryOrderRepository>());

        services.AddSingleton<InMemoryPositionRepository>();
        services.AddSingleton<IPositionRepository>(sp => sp.GetRequiredService<InMemoryPositionRepository>());
        services.AddSingleton<IPositionChangeNotifier>(sp => sp.GetRequiredService<InMemoryPositionRepository>());

        // 戦略レジストリ (JSON 永続化、本番は strategies.json / シミュレータは strategies.simulator.json)
        // 詳細仕様は docs/simulator-mode.md §9-3 (A 案: 永続化分離)。
        services.AddSingleton<IStrategyRegistry>(sp => new JsonStrategyRegistry(
            BrokerBridgePersistencePaths.Strategies(simulatorMode),
            sp.GetRequiredService<ILogger<JsonStrategyRegistry>>()));

        // 自動取引建玉メタデータストア
        services.AddSingleton<IAutoPositionMetadataStore>(sp => new JsonAutoPositionMetadataStore(
            BrokerBridgePersistencePaths.AutoPositions(simulatorMode),
            sp.GetRequiredService<ILogger<JsonAutoPositionMetadataStore>>()));

        // 注文メタデータストア (旧 N225OrderBridge の order.csv 相当)
        services.AddSingleton<IOrderMetadataStore>(sp => new JsonOrderMetadataStore(
            BrokerBridgePersistencePaths.OrdersMetadata(simulatorMode),
            sp.GetRequiredService<ILogger<JsonOrderMetadataStore>>()));

        // ポジション履歴ストア (決済済み実現損益・追記型)。詳細: docs/position-history-spec.md
        services.AddSingleton<IClosedTradeStore>(sp => new JsonClosedTradeStore(
            BrokerBridgePersistencePaths.PositionHistory(simulatorMode),
            sp.GetRequiredService<ILogger<JsonClosedTradeStore>>()));

        // 約定待ち注文 ID 追跡 (旧 N225OrderBridge の OrderInquiryList 相当)
        // 発注 Accepted 時に Track、約定/取消で Untrack。Polling は Tracker が空ならネットワークを叩かない
        services.AddSingleton<IPendingOrderTracker, InMemoryPendingOrderTracker>();

        // 起動時セッション初期化 (Token → 注文一覧 → 建玉整合 を順序立てて実行)
        // 旧 N225OrderBridge.TradeViewModel.Initialize() の順序を踏襲。
        // HostedService の StartAsync は登録順に呼ばれるため、最初に登録して他より先に走らせる。
        services.AddHostedService<BrokerSessionInitializerService>();

        // ブローカーから流れる ExecutionEvent を ExecutionApplier に渡す購読者
        // (これが無いと約定が Repository に反映されない)
        services.AddHostedService<ExecutionStreamSubscriberService>();

        // 注文終端 (キャンセル/失効/拒否) を ExecutionApplier に渡す購読者
        // (これが無いと返済キャンセル時に Position の HoldQty が解放されない)
        services.AddHostedService<OrderTerminationSubscriberService>();

        // 建玉の定期リコンサイル (kabu /positions を周期取得しブリッジ建玉を kabu のミラーに保つ)。
        // SQ 限月決済・外部決済で消えた建玉をブリッジから除去し、kabu を常に正とする。
        // 起動時 Step 3 だけでは追従できなかった「前月建玉が消えない」問題への恒久対処。
        services.AddHostedService<PositionReconciliationService>();

        // ブローカーアダプタは具象を呼び出し側で登録する (KabuAdapter/RakutenAdapter 等)

        return services;
    }

    /// <summary>
    /// Webhook 受信機 (HttpListener ベース) をホステッドサービスとして登録する。
    /// 呼び出し側で services.Configure&lt;WebhookListenerOptions&gt;(...) を先に設定すること。
    /// </summary>
    public static IServiceCollection AddBrokerBridgeWebhook(this IServiceCollection services)
    {
        services.AddSingleton<HttpWebhookListener>();
        services.AddHostedService<WebhookHostedService>();
        return services;
    }

    /// <summary>
    /// kabu ステーション API アダプタを登録する。
    /// 呼び出し側で services.Configure&lt;KabuOptions&gt;(...) を先に設定すること。
    ///
    /// HttpClient はファクトリ経由 (kabu API は localhost 直通でコネクション再利用が効く)。
    /// </summary>
    public static IServiceCollection AddBrokerBridgeKabu(this IServiceCollection services)
    {
        // kabu API は「最後に発行したトークンだけが有効」(後発トークンが先発を即無効化)。
        // KabuTokenService が複数インスタンス存在するとお互いの取得トークンを潰し合うので
        // 必ず Singleton 1 個に統一する。
        //
        // AddHttpClient<T>() は T を Transient で登録してしまうため、ここでは
        // 名前付き HttpClient + Singleton ファクトリで両立させる (HttpClient は localhost 直通で
        // コネクション再利用が効くため、Singleton 保持で問題ない)。
        services.AddHttpClient("Kabu");
        services.AddSingleton<KabuTokenService>(sp => new KabuTokenService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Kabu"),
            sp.GetRequiredService<IOptions<KabuOptions>>(),
            sp.GetRequiredService<ILogger<KabuTokenService>>()));
        services.AddSingleton<KabuApiClient>(sp => new KabuApiClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Kabu"),
            sp.GetRequiredService<KabuTokenService>(),
            sp.GetRequiredService<IOptions<KabuOptions>>(),
            sp.GetRequiredService<ILogger<KabuApiClient>>()));
        services.AddSingleton<KabuAdapter>();
        services.AddSingleton<IBrokerAdapter>(sp => sp.GetRequiredService<KabuAdapter>());

        // /orders 周期ポーリング → ExecutionEvent ストリーム化 (旧 InquiryTimer 相当)
        // 同インスタンスを IOrderSnapshotNotifier としても解決 (UI 注文一覧の毎秒更新源)
        services.AddSingleton<KabuOrderPollingService>();
        services.AddHostedService(sp => sp.GetRequiredService<KabuOrderPollingService>());
        services.AddSingleton<IOrderSnapshotNotifier>(sp => sp.GetRequiredService<KabuOrderPollingService>());
        services.AddSingleton<IOrderInitialFetcher>(sp => sp.GetRequiredService<KabuOrderPollingService>());

        // WebSocket /websocket 接続 → PriceTick ストリーム化 (旧 WebSocket_Future 相当)
        // 同インスタンスを IPriceUpdateNotifier としても解決 (UI 価格表示更新源)
        services.AddSingleton<KabuBoardWebSocketService>();
        services.AddHostedService(sp => sp.GetRequiredService<KabuBoardWebSocketService>());
        services.AddSingleton<IPriceUpdateNotifier>(sp => sp.GetRequiredService<KabuBoardWebSocketService>());

        // kabu board tick を AI (N225StrategyAI・127.0.0.1:5000) へ転送する追加サービス。
        // 既存ロジック不変の純粋追加 (price stream を購読して TCP 送信するだけ)。
        services.AddHostedService<Integration.AiTickForwarderService>();

        return services;
    }

    /// <summary>
    /// シミュレータモード (--simulator) 用の Mock ブローカーアダプタを登録する。
    /// kabu API クライアント・ポーリング・WebSocket は一切登録しない。
    /// 詳細仕様は docs/simulator-mode.md を参照。
    /// </summary>
    public static IServiceCollection AddBrokerBridgeMockBroker(this IServiceCollection services)
    {
        services.AddSingleton<MockBrokerAdapter>();
        services.AddSingleton<IBrokerAdapter>(sp => sp.GetRequiredService<MockBrokerAdapter>());
        services.AddSingleton<IOrderSnapshotNotifier>(sp => sp.GetRequiredService<MockBrokerAdapter>());
        services.AddSingleton<IOrderInitialFetcher>(sp => sp.GetRequiredService<MockBrokerAdapter>());
        services.AddSingleton<IPriceUpdateNotifier>(sp => sp.GetRequiredService<MockBrokerAdapter>());
        return services;
    }
}
