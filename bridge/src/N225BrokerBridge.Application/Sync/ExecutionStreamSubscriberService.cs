using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Application.Orders;
using N225BrokerBridge.Domain.Brokers;

namespace N225BrokerBridge.Application.Sync;

/// <summary>
/// <see cref="IBrokerAdapter.ExecutionStream"/> を購読して
/// <see cref="ExecutionApplier"/> に流す HostedService。
///
/// これが無いと、ブローカーから流れてくる ExecutionEvent が誰にも受け取られず
/// Order / Position の状態更新が一切起こらない (旧 N225OrderBridge の
/// MessageEventHandler → OrderManager/PositionManager に相当する経路)。
///
/// ExecutionApplier は Transient なので、購読ごとに DI スコープを切って解決する。
/// (ScopedLifetime からの解決を許可するため IServiceScopeFactory を注入する)
/// </summary>
public sealed class ExecutionStreamSubscriberService : IHostedService, IDisposable
{
    private readonly IBrokerAdapter _broker;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExecutionStreamSubscriberService> _logger;
    private IDisposable? _subscription;

    public ExecutionStreamSubscriberService(
        IBrokerAdapter broker,
        IServiceScopeFactory scopeFactory,
        ILogger<ExecutionStreamSubscriberService> logger)
    {
        _broker = broker;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = _broker.ExecutionStream.Subscribe(
            onNext: OnExecutionEvent,
            onError: ex => _logger.LogError(ex, "ExecutionStream emitted an error"),
            onCompleted: () => _logger.LogInformation("ExecutionStream completed"));

        _logger.LogInformation("約定通知ストリーム購読開始 (ExecutionStream → ExecutionApplier 接続)。");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        _logger.LogInformation("約定通知ストリーム購読停止。");
        return Task.CompletedTask;
    }

    private async void OnExecutionEvent(ExecutionEvent ev)
    {
        // Rx の OnNext は ThreadPool 上で動く。ここで例外を漏らすと
        // Subject 全体が壊れるので必ず catch する。
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var applier = scope.ServiceProvider.GetRequiredService<ExecutionApplier>();
            await applier.ApplyAsync(ev);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ExecutionApplier.ApplyAsync failed: execId={ExecId} orderId={OrderId}",
                ev.ExecutionId, ev.BrokerOrderId);
        }
    }

    public void Dispose() => _subscription?.Dispose();
}
