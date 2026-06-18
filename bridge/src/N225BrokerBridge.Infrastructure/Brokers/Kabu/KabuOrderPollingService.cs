using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Application.Sync;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.ValueObjects;
using N225BrokerBridge.Infrastructure.Brokers.Kabu.Dto;

namespace N225BrokerBridge.Infrastructure.Brokers.Kabu;

/// <summary>
/// kabu /orders を「pending 追跡型」でポーリングし、約定検出時に
/// <see cref="KabuAdapter.ExecutionStream"/> に <see cref="ExecutionEvent"/> を流す。
///
/// 旧 N225OrderBridge の <c>InquiryTimer</c> 相当を新ブリッジに正確に移植:
///   - <see cref="IPendingOrderTracker"/> に追跡中の OrderID リストを保持
///   - 毎秒、Tracker が空ならネットワークを叩かない (旧 IsList() == null と同じ)
///   - 値があれば各 OrderID を /orders?id=xxx で個別照会
///   - 約定 (RecType=8) を検出したら ExecutionEvent を発火
///   - 終端状態 (約定済 / 取消 / 期限切れ / 失効) になったら Tracker.Untrack
///
/// 旧との差異 (新仕様):
///   - global static (OrderInquiryList) → DI 注入 interface
///   - SeqNum ベースの未処理明細追跡は ExecutionApplier 側に委譲 (Duplicate ExecutionId 検出は Order 集約が担当)
///
/// kabu 未接続時は WARN ログのみで継続 (UI 起動はブロックしない)。
/// </summary>
public sealed class KabuOrderPollingService : IHostedService, IOrderSnapshotNotifier, IOrderInitialFetcher, IDisposable
{
    private readonly KabuApiClient _client;
    private readonly KabuAdapter _adapter;
    private readonly IOrderRepository _orderRepo;
    private readonly IPendingOrderTracker _pendingTracker;
    private readonly ILogger<KabuOrderPollingService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>同一 ExecutionID の重複発火防止 (kabu 仕様で SeqNum は単調増加なので明細ごとに一意)。</summary>
    private readonly ConcurrentDictionary<string, byte> _seenExecutionIds = new();

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 注文一覧 UI 更新用。pending 中の注文だけスナップショットを発火する
    /// (約定済の注文は内部 IOrderRepository に残るが、ここでは push しない)。
    /// 旧 N225OrderBridge では UI 側 (OrderList) を別途管理していたのと同じ思想。
    /// </summary>
    public event EventHandler<OrderSnapshotsEventArgs>? SnapshotsUpdated;

    /// <summary>
    /// 最後に発火したスナップショット一覧 (起動順の都合で MainViewModel が遅れて購読する場合
    /// のために常に最新を保持する)。
    /// </summary>
    private IReadOnlyList<OrderSnapshot> _latestSnapshots = Array.Empty<OrderSnapshot>();
    public IReadOnlyList<OrderSnapshot> LatestSnapshots => _latestSnapshots;

    /// <summary>
    /// 起動時の注文一覧初期取得 (旧 InitialOrdersLIstView 相当)。
    /// kabu /orders を全件取得して UI に push する。
    /// BrokerSessionInitializerService から起動順序の Step 2 で呼ばれる。
    /// </summary>
    public async Task<int> InitialFetchOrdersAsync(CancellationToken ct = default)
    {
        try
        {
            var orders = await _client.GetOrdersAsync(ct);

            // 既存約定 ID は seen に積んでおく (再起動時の重複発火防止)
            foreach (var o in orders)
            {
                if (o.Details is null) continue;
                foreach (var d in o.Details)
                    if (d.RecType == 8 && !string.IsNullOrEmpty(d.ExecutionID))
                        _seenExecutionIds.TryAdd(d.ExecutionID, 0);
            }

            // UI に全件 push (購読者が無くても LatestSnapshots に保持しておく)
            if (orders.Count > 0)
            {
                var snapshots = orders
                    .Where(o => !string.IsNullOrEmpty(o.ID))
                    .Select(o => KabuMappers.ToOrderSnapshot(o, _adapter.BrokerCode))
                    .ToList();
                _latestSnapshots = snapshots;
                SnapshotsUpdated?.Invoke(this, new OrderSnapshotsEventArgs(snapshots, DateTime.UtcNow));
            }

            return orders.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "起動時の注文一覧取得失敗 (kabu 未接続の可能性)");
            return 0;
        }
    }

    public KabuOrderPollingService(
        KabuApiClient client,
        KabuAdapter adapter,
        IOrderRepository orderRepo,
        IPendingOrderTracker pendingTracker,
        ILogger<KabuOrderPollingService> logger)
    {
        _client = client;
        _adapter = adapter;
        _orderRepo = orderRepo;
        _pendingTracker = pendingTracker;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => PollLoopAsync(_cts.Token));
        _logger.LogInformation(
            "注文約定照会タイマー起動 (間隔={Interval}秒、約定待ちのみ追跡モード)。",
            PollInterval.TotalSeconds);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_loopTask is not null)
        {
            try { await _loopTask.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken); }
            catch (TimeoutException) { _logger.LogWarning("Polling loop did not stop in time"); }
            catch (OperationCanceledException) { }
        }
        _logger.LogInformation("注文約定照会タイマー停止。");
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_pendingTracker.IsEmpty)
                {
                    await PollOnceAsync(ct);
                }
                // pending が空ならスキップ (ネットワーク叩かない、旧 InquiryTimer 準拠)
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "注文照会失敗 (kabu 未接続の可能性)。");
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var pendingIds = _pendingTracker.GetAll();
        if (pendingIds.Count == 0) return;

        // 毎秒走るルーチンログは Debug レベルに留める (UI ログタブを埋め尽くさないため)。
        // 異常時は LogWarning/LogError が別途出力されるので、UI 上では従来通り目に入る。
        _logger.LogDebug(
            "注文ポーリング: {Count} 件照会 ({Ids})", pendingIds.Count, string.Join(",", pendingIds));

        var snapshots = new List<OrderSnapshot>();

        foreach (var id in pendingIds)
        {
            KabuOrderDto? order;
            try
            {
                order = await _client.GetOrderByIdAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetOrderById failed for {OrderId}", id);
                continue;
            }

            if (order is null)
            {
                _logger.LogWarning("注文ポーリング: /orders?id={Id} が null を返しました", id);
                continue;
            }
            // 毎ポーリングごとに同じ State が大量に出るのを避けるため Debug レベルに変更。
            // 状態遷移の重要瞬間 (約定検出 / Position opened 等) は ExecutionApplier 側で
            // Information ログが別途出るので、運用に必要な情報は失われない。
            _logger.LogDebug(
                "注文ポーリング: 受信 注文ID={Id} 状態={State} 約定数={Cum}/{Qty}",
                order.ID, order.State, order.CumQty, order.OrderQty);

            // UI 注文一覧用スナップショット
            try
            {
                snapshots.Add(KabuMappers.ToOrderSnapshot(order, _adapter.BrokerCode));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ToOrderSnapshot failed for {OrderId}", order.ID);
            }

            // 約定明細をスキャンして新規分を発火
            if (order.Details is not null)
            {
                foreach (var detail in order.Details)
                {
                    if (detail.RecType != 8) continue;   // 約定以外スキップ
                    if (string.IsNullOrEmpty(detail.ExecutionID)) continue;
                    if (!_seenExecutionIds.TryAdd(detail.ExecutionID, 0)) continue;

                    var ev = await BuildExecutionEventAsync(order, detail, ct);
                    if (ev is not null)
                    {
                        _adapter.PushExecution(ev);
                        _logger.LogInformation(
                            "約定検出: 注文ID={OrderId} 約定ID={ExecId} 数量={Qty} 価格={Price}",
                            order.ID, detail.ExecutionID, detail.Qty, detail.Price);
                    }
                }
            }

            // 終端状態 (kabu State 5=終了) なら Tracker から外す
            // 旧 InquiryTimer は約定 (RecType=8) で OrderManager.ContrctAll → OrderInquiryList.Remove
            if (order.State == 5)
            {
                _pendingTracker.Untrack(id);
            }
        }

        if (snapshots.Count > 0)
        {
            _latestSnapshots = snapshots;
            if (SnapshotsUpdated is { } handler)
            {
                try
                {
                    handler.Invoke(this, new OrderSnapshotsEventArgs(snapshots, DateTime.UtcNow));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "OrderSnapshotsUpdated handler threw");
                }
            }
        }
    }

    private async Task<ExecutionEvent?> BuildExecutionEventAsync(
        KabuOrderDto order, KabuOrderDetailDto detail, CancellationToken ct)
    {
        try
        {
            var brokerOrderId = new OrderId(order.ID!);

            // 内部 Order を OrderRepository から探す (TargetExecutionId / TradeType の情報源)
            var internalOrder = await _orderRepo.FindByBrokerOrderIdAsync(
                _adapter.BrokerCode, brokerOrderId, ct);

            TradeType tradeType = internalOrder?.TradeType
                                  ?? (order.CashMargin == 3 ? TradeType.ExitOrder : TradeType.NewOrder);

            ExecutionId? targetPositionId = internalOrder?.TargetExecutionId;

            Side side = order.Side switch
            {
                "2" => Side.Buy,
                "1" => Side.Sell,
                _ => internalOrder?.Side ?? Side.Buy
            };

            var symbol = new SymbolCode(order.Symbol ?? internalOrder?.Symbol.Value ?? "?");
            var execId = new ExecutionId(detail.ExecutionID!);
            var qty = new Quantity((int)Math.Round(detail.Qty ?? 0));
            var price = new Price((decimal)(detail.Price ?? 0));

            DateTime executedAt = DateTime.UtcNow;
            if (DateTime.TryParse(detail.ExecutionDay, out var dt))
                executedAt = dt.ToUniversalTime();

            return new ExecutionEvent(
                BrokerCode: _adapter.BrokerCode,
                BrokerOrderId: brokerOrderId,
                ExecutionId: execId,
                TradeType: tradeType,
                Side: side,
                Symbol: symbol,
                Quantity: qty,
                Price: price,
                ExecutedAt: executedAt,
                TargetPositionId: targetPositionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to build ExecutionEvent orderId={OrderId} execId={ExecId}",
                order.ID, detail.ExecutionID);
            return null;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
