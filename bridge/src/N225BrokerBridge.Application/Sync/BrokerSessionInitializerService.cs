using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Positions;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Application.Sync;

/// <summary>
/// ブリッジ起動時のブローカーセッション初期化を「順序立てて」実行する HostedService。
///
/// 旧 N225OrderBridge の <c>TradeViewModel.Initialize()</c> と同じ順序を再現:
///   Step 1: kabu API トークン取得 (X-API-KEY warm up)
///   Step 2: 注文一覧初期取得 (旧 InitialOrdersLIstView) → UI へ push
///   Step 3: 建玉一覧整合チェック (旧 InitialPositionsLIstView + auto-positions メタ突合)
///
/// HostedService の StartAsync は登録順に呼ばれるため、本サービスを **最初** に登録すれば
/// 他の HostedService (KabuOrderPollingService / WebSocket / Webhook) の起動より先に走る。
/// kabu API リソース系は本サービス完了後に動くため、初期データが揃った状態で UI が出る。
///
/// 失敗時は警告ログのみで継続 (kabu 未接続でも UI は起動する)。
/// </summary>
public sealed class BrokerSessionInitializerService : IHostedService
{
    private readonly IBrokerAdapter _broker;
    private readonly IOrderInitialFetcher _orderInitialFetcher;
    private readonly IPositionRepository _positionRepo;
    private readonly IAutoPositionMetadataStore _autoStore;
    private readonly ILogger<BrokerSessionInitializerService> _logger;

    public BrokerSessionInitializerService(
        IBrokerAdapter broker,
        IOrderInitialFetcher orderInitialFetcher,
        IPositionRepository positionRepo,
        IAutoPositionMetadataStore autoStore,
        ILogger<BrokerSessionInitializerService> logger)
    {
        _broker = broker;
        _orderInitialFetcher = orderInitialFetcher;
        _positionRepo = positionRepo;
        _autoStore = autoStore;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("========== ブローカーセッション初期化 開始 ==========");

        // ── Step 1: kabu API トークン取得 ──────────────────────
        await Step1_AcquireTokenAsync(cancellationToken);

        // ── Step 2: 注文一覧初期取得 (旧 InitialOrdersLIstView) ─
        await Step2_FetchInitialOrdersAsync(cancellationToken);

        // ── Step 3: 建玉一覧整合チェック (旧 InitialPositionsLIstView + 自動取引メタ突合) ──
        await Step3_ReconcilePositionsAsync(cancellationToken);

        _logger.LogInformation("========== ブローカーセッション初期化 完了 ==========");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Step 1: kabu API トークン warm up。</summary>
    private async Task Step1_AcquireTokenAsync(CancellationToken ct)
    {
        try
        {
            // KabuTokenService に直接アクセスするのは Application 層の責務外なので、
            // ダミーの API 呼び出し (GetPositionsAsync) で token 取得をトリガーする。
            // これにより token 取得ログが出る。
            _logger.LogInformation("Step 1: kabu API トークン取得 (warm up)...");
            await _broker.GetPositionsAsync(ct);
            _logger.LogInformation("Step 1: 完了。");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step 1 失敗 (kabu 未接続の可能性)");
        }
    }

    /// <summary>Step 2: kabu /orders を全件取得 → UI に push。</summary>
    private async Task Step2_FetchInitialOrdersAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Step 2: 注文一覧初期取得 (kabu /orders) ...");
            int count = await _orderInitialFetcher.InitialFetchOrdersAsync(ct);
            _logger.LogInformation("Step 2: 完了。注文 {Count} 件取得 + UI 通知", count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step 2 失敗");
        }
    }

    /// <summary>Step 3: kabu /positions + 自動取引メタを突合して PositionRepository を初期化。</summary>
    private async Task Step3_ReconcilePositionsAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Step 3: 建玉一覧初期取得 + 整合チェック (kabu /positions と自動取引メタを突合) ...");

            var brokerPositions = await _broker.GetPositionsAsync(ct);
            var autoMetadata = await _autoStore.LoadAllAsync(ct);
            var metadataByExecId = autoMetadata.ToDictionary(m => m.ExecutionId, StringComparer.Ordinal);

            int restored = 0, manual = 0;
            var activeExecutionIds = new List<string>();

            foreach (var snap in brokerPositions)
            {
                if (snap.LeaveQuantity.IsZero) continue;

                var execId = snap.PositionId.Value;
                activeExecutionIds.Add(execId);

                if (metadataByExecId.TryGetValue(execId, out var meta))
                {
                    var position = new Position(
                        id: snap.PositionId,
                        brokerCode: snap.BrokerCode,
                        strategy: new StrategyName(meta.Strategy),
                        interval: meta.Interval > 0 ? meta.Interval : 1,
                        tradeMode: TradeMode.Auto,
                        symbol: snap.Symbol,
                        side: snap.Side,
                        initialQuantity: snap.LeaveQuantity,
                        entryPrice: snap.EntryPrice,
                        openedAtUtc: meta.OpenedAt == default ? snap.OpenedAt : meta.OpenedAt);
                    await _positionRepo.AddAsync(position, ct);
                    restored++;
                }
                else
                {
                    var position = new Position(
                        id: snap.PositionId,
                        brokerCode: snap.BrokerCode,
                        strategy: new StrategyName("Manual"),
                        interval: 0,
                        tradeMode: TradeMode.Manual,
                        symbol: snap.Symbol,
                        side: snap.Side,
                        initialQuantity: snap.LeaveQuantity,
                        entryPrice: snap.EntryPrice,
                        openedAtUtc: snap.OpenedAt);
                    await _positionRepo.AddAsync(position, ct);
                    manual++;
                }
            }

            await _autoStore.SyncToActiveSetAsync(activeExecutionIds, ct);

            _logger.LogInformation(
                "Step 3: 完了。kabu 建玉={Broker} 件 / 自動復元={Restored} 件 / 手動扱い={Manual} 件",
                brokerPositions.Count, restored, manual);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step 3 失敗");
        }
    }
}
