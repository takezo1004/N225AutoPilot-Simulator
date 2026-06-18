using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace N225BrokerBridge.Infrastructure.Webhooks;

/// <summary>
/// アプリ起動時に <see cref="HttpWebhookListener"/> を立ち上げ、
/// 終了時にきれいに止めるホステッドサービス。
/// </summary>
public sealed class WebhookHostedService : IHostedService
{
    private readonly HttpWebhookListener _listener;
    private readonly ILogger<WebhookHostedService> _logger;

    public WebhookHostedService(HttpWebhookListener listener, ILogger<WebhookHostedService> logger)
    {
        _listener = listener;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Webhook 受信サービス起動中...");
        return _listener.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Webhook 受信サービス停止中...");
        await _listener.StopAsync(cancellationToken);
    }
}
