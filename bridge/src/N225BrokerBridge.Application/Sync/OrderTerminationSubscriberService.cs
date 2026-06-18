using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Application.Orders;
using N225BrokerBridge.Domain.Orders;

namespace N225BrokerBridge.Application.Sync;

/// <summary>
/// <see cref="IOrderSnapshotNotifier.SnapshotsUpdated"/> を購読し、
/// kabu 側で終端状態 (Cancelled/Expired/Rejected) になった注文を
/// <see cref="ExecutionApplier.ApplyTerminationAsync"/> で Order/Position 集約に反映する HostedService。
///
/// 約定 (Filled) 遷移は <see cref="ExecutionStreamSubscriberService"/> が ExecutionEvent 経由で
/// 既に処理しているため、本サービスでは Filled をスキップする (二重発火防止)。
///
/// これが無いと、ユーザーが注文を取消しても建玉一覧の「注文中」枚数が解放されない。
/// </summary>
public sealed class OrderTerminationSubscriberService : IHostedService, IDisposable
{
    private readonly IOrderSnapshotNotifier _notifier;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderTerminationSubscriberService> _logger;

    public OrderTerminationSubscriberService(
        IOrderSnapshotNotifier notifier,
        IServiceScopeFactory scopeFactory,
        ILogger<OrderTerminationSubscriberService> logger)
    {
        _notifier = notifier;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _notifier.SnapshotsUpdated += OnSnapshotsUpdated;
        _logger.LogInformation("注文終端購読開始 (OrderSnapshots → ExecutionApplier.ApplyTerminationAsync)。");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _notifier.SnapshotsUpdated -= OnSnapshotsUpdated;
        _logger.LogInformation("注文終端購読停止。");
        return Task.CompletedTask;
    }

    private async void OnSnapshotsUpdated(object? sender, OrderSnapshotsEventArgs e)
    {
        // Rx 同様、購読ハンドラから例外を漏らさない (上位の event 発火が壊れる)。
        try
        {
            // 終端状態 (Filled/Cancelled/Expired/Rejected) すべてを処理する。
            // Filled でも「部分約定後のキャンセル」のケースがあり、その場合 Order の RemainingQuantity が
            // 残っているので予約解放が必要。フル約定済の場合は ApplyTerminationAsync 側の冪等チェックで no-op になる。
            var terminal = e.Snapshots
                .Where(s => s.State.IsTerminal())
                .ToList();
            if (terminal.Count == 0) return;

            using var scope = _scopeFactory.CreateScope();
            var applier = scope.ServiceProvider.GetRequiredService<ExecutionApplier>();
            foreach (var snapshot in terminal)
            {
                try
                {
                    await applier.ApplyTerminationAsync(
                        snapshot.BrokerCode,
                        snapshot.BrokerOrderId,
                        reason: $"Terminal from poll: {snapshot.State}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "ApplyTerminationAsync failed: orderId={OrderId} state={State}",
                        snapshot.BrokerOrderId, snapshot.State);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrderTerminationSubscriber failed");
        }
    }

    public void Dispose()
    {
        _notifier.SnapshotsUpdated -= OnSnapshotsUpdated;
    }
}
