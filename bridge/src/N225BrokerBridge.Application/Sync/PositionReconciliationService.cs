using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Positions;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Application.Sync;

/// <summary>
/// 建玉の「定期リコンサイル」HostedService。
///
/// 背景 (なぜ必要か):
///   ブリッジのライブ建玉は従来、<b>起動時の一回</b> (<see cref="BrokerSessionInitializerService"/> Step 3)
///   と「ブリッジ経由の約定差分」だけで更新されていた。そのため、ブリッジを介さずに口座側で
///   消滅した建玉 (SQ 限月決済・株ステーション GUI からの手動決済 など) がブリッジに残り続け、
///   kabu (= source of truth) と乖離していた。限月ロール後に「前月の建玉が消えない」のはこれが原因。
///
/// 本サービスは一定間隔で kabu <c>/positions</c> を取得し、ブリッジのライブ建玉を
/// <b>kabu のミラー</b>に保つ:
///   - kabu に在ってブリッジに無い建玉 → 追加 (auto/manual を自動取引メタで判定)
///   - ブリッジに在って kabu に無い建玉 → 除去 (SQ 決済・外部決済の追従)
///
/// kabu を常に正とするため、限月が変わっても取りに行くだけで自動追従する
/// (現月コードを意識する必要がない)。<c>position-history.json</c> 等の履歴とは独立。
///
/// 安全策 (実取引のため誤操作を避ける):
///   - <b>追加・除去とも 2 ティック連続で同じ状態が続いて初めて確定</b>する
///     (kabu /positions の反映ラグや、ブリッジ自身の発注直後の一時的な不一致で
///      建玉を誤って足したり消したりしないため)。
///   - 自動取引メタ (auto-positions.json) の prune はここでは行わない。発注直後に
///     書かれたメタを kabu 反映ラグ中に消すリスクを避け、既存フロー
///     (起動時 Step 3 / 決済時 ExecutionApplier) に委ねる。
///   - 例外は 1 ティック内で握りつぶしてログのみ。ループは止めない (kabu 一時不通でも継続)。
///
/// 発注は一切行わない (読み取り + ブリッジ内リポジトリの整合のみ) ため全モードで安全。
/// </summary>
public sealed class PositionReconciliationService : BackgroundService
{
    /// <summary>リコンサイル間隔。</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>起動直後の初回待機 (起動時 Step 3 の整合が終わってから始める)。</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    /// <summary>追加・除去を確定するのに必要な「連続同状態」ティック数。</summary>
    private const int ConfirmTicks = 2;

    private readonly IBrokerAdapter _broker;
    private readonly IPositionRepository _positionRepo;
    private readonly IAutoPositionMetadataStore _autoStore;
    private readonly ILogger<PositionReconciliationService> _logger;

    // 連続不在カウンタ (除去確定用): repo に在るが kabu に無い ExecutionId → 連続不在回数
    private readonly Dictionary<string, int> _absenceStreak = new(StringComparer.Ordinal);
    // 連続存在カウンタ (追加確定用): kabu に在るが repo に無い ExecutionId → 連続存在回数
    private readonly Dictionary<string, int> _presenceStreak = new(StringComparer.Ordinal);

    public PositionReconciliationService(
        IBrokerAdapter broker,
        IPositionRepository positionRepo,
        IAutoPositionMetadataStore autoStore,
        ILogger<PositionReconciliationService> logger)
    {
        _broker = broker;
        _positionRepo = positionRepo;
        _autoStore = autoStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "建玉定期リコンサイル起動 (初回={Delay}s 後・間隔={Interval}s・確定={Confirm}ティック連続)。",
            (int)StartupDelay.TotalSeconds, (int)Interval.TotalSeconds, ConfirmTicks);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await ReconcileOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // kabu 一時不通等。1 ティックは諦めて次へ。
                _logger.LogWarning(ex, "建玉定期リコンサイル: 1 ティック失敗 (次回継続)。");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>1 ティック分のリコンサイル。kabu /positions を取得しブリッジ建玉をミラーに寄せる。</summary>
    internal async Task ReconcileOnceAsync(CancellationToken ct)
    {
        var result = await _broker.GetPositionsAsync(ct);

        // ★最重要ガード: kabu /positions が確定的に読めなかった (未認証・夜間セッション切替・
        // メンテ窓・トークン失効・通信エラー) ときは、空応答を「建玉ゼロ」と取り違えて
        // 生きた建玉を除去しないため、このティックは一切手を付けずに抜ける。
        // 連続不在/存在カウンタも触らない (読めたティックだけで確定を進める)。
        // 2026-06-23: ここを欠いていたため、夜間セッション引け直後の空応答で
        //             約定済み自動建玉・手動建玉を一括除去する実害が出た。
        if (!result.IsAvailable)
        {
            _logger.LogDebug(
                "建玉リコンサイル: kabu /positions が照会不能のためこのティックはスキップ (建玉・カウンタは保持)。");
            return;
        }

        var kabuActive = result.Positions.Where(p => !p.LeaveQuantity.IsZero).ToList();
        var kabuIds = new HashSet<string>(kabuActive.Select(s => s.PositionId.Value), StringComparer.Ordinal);

        var existing = await _positionRepo.FindActiveAsync(ct);
        var existingIds = new HashSet<string>(existing.Select(p => p.Id.Value), StringComparer.Ordinal);

        int added = 0, removed = 0;

        // ── 追加: kabu に在ってブリッジに無い建玉 (2 ティック連続で確定) ──
        IReadOnlyList<AutoPositionMetadata>? autoMetadata = null;
        foreach (var snap in kabuActive)
        {
            var id = snap.PositionId.Value;
            if (existingIds.Contains(id))
            {
                _presenceStreak.Remove(id);   // 既にブリッジにある → カウンタ不要
                continue;
            }

            int streak = _presenceStreak.GetValueOrDefault(id) + 1;
            if (streak < ConfirmTicks)
            {
                _presenceStreak[id] = streak;
                continue;   // まだ確定しない (反映ラグの可能性)
            }

            // 確定 → 追加。auto/manual は自動取引メタで判定 (Step 3 と同一ロジック)。
            autoMetadata ??= await _autoStore.LoadAllAsync(ct);
            var position = BuildPosition(snap, autoMetadata);
            await _positionRepo.AddAsync(position, ct);
            _presenceStreak.Remove(id);
            added++;
            _logger.LogInformation(
                "建玉リコンサイル: kabu のみに在った建玉を追加 id={Id} symbol={Symbol} side={Side} qty={Qty} mode={Mode} strategy={Strategy}",
                id, position.Symbol, position.Side, position.LeaveQuantity, position.TradeMode, position.Strategy);
        }
        // kabu から消えた ID の presence カウンタは破棄
        PruneCounter(_presenceStreak, kabuIds);

        // ── 除去: ブリッジに在って kabu に無い建玉 (2 ティック連続で確定) ──
        foreach (var pos in existing)
        {
            var id = pos.Id.Value;
            if (kabuIds.Contains(id))
            {
                _absenceStreak.Remove(id);    // kabu にまだ在る → カウンタ不要
                continue;
            }

            int streak = _absenceStreak.GetValueOrDefault(id) + 1;
            if (streak < ConfirmTicks)
            {
                _absenceStreak[id] = streak;
                continue;   // まだ確定しない (反映ラグ・発注直後の可能性)
            }

            await _positionRepo.RemoveAsync(pos.Id, ct);
            _absenceStreak.Remove(id);
            removed++;
            _logger.LogInformation(
                "建玉リコンサイル: kabu に存在しない建玉を除去 id={Id} symbol={Symbol} side={Side} qty={Qty} (SQ 決済/外部決済の追従)",
                id, pos.Symbol, pos.Side, pos.LeaveQuantity);
        }
        // repo から消えた ID の absence カウンタは破棄
        PruneCounter(_absenceStreak, existingIds);

        if (added > 0 || removed > 0)
        {
            _logger.LogInformation(
                "建玉リコンサイル完了: kabu 建玉={Kabu} 件 / 追加={Added} / 除去={Removed}",
                kabuActive.Count, added, removed);
        }
    }

    /// <summary>kabu 建玉スナップショットから Position を生成 (auto/manual を自動取引メタで判定)。</summary>
    private static Position BuildPosition(
        PositionSnapshot snap,
        IReadOnlyList<AutoPositionMetadata> autoMetadata)
    {
        var meta = autoMetadata.FirstOrDefault(
            m => string.Equals(m.ExecutionId, snap.PositionId.Value, StringComparison.Ordinal));

        if (meta is not null)
        {
            return new Position(
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
        }

        return new Position(
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
    }

    /// <summary>現存しない ID のカウンタを削除する。</summary>
    private static void PruneCounter(Dictionary<string, int> counter, HashSet<string> liveIds)
    {
        if (counter.Count == 0) return;
        var dead = counter.Keys.Where(k => !liveIds.Contains(k)).ToList();
        foreach (var k in dead)
            counter.Remove(k);
    }
}
