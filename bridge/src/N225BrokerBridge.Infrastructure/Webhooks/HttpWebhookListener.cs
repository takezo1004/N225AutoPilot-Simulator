using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using N225BrokerBridge.Application.Signals;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Infrastructure.Webhooks;

/// <summary>
/// HttpListener ベースの Webhook 受信機。
///
/// http://{host}:{port}{path} で待ち受け、POST されたペイロードを
/// <see cref="SignalPayloadParser"/> でパース、<see cref="SignalHandler"/> へ渡す。
///
/// 現 N225OrderBridge の CustomTcpServer.cs (raw TCP) を HTTP に置き換えた版。
/// Cloudflare Tunnel は HTTP→HTTP でルーティングするので、原始 TCP より相性が良い。
/// </summary>
public sealed class HttpWebhookListener : IDisposable
{
    private readonly WebhookListenerOptions _options;
    private readonly SignalHandler _handler;
    private readonly ILogger<HttpWebhookListener> _logger;
    private HttpListener? _httpListener;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public HttpWebhookListener(
        IOptions<WebhookListenerOptions> options,
        SignalHandler handler,
        ILogger<HttpWebhookListener> logger)
    {
        _options = options.Value;
        _handler = handler;
        _logger = logger;
    }

    public bool IsRunning => _httpListener?.IsListening == true;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_httpListener is not null)
            throw new InvalidOperationException("Listener already started.");

        _httpListener = new HttpListener();
        var prefix = $"http://{_options.Host}:{_options.Port}{NormalizePath(_options.Path)}";
        _httpListener.Prefixes.Add(prefix);
        _httpListener.Start();

        _logger.LogInformation("Webhook リスナー起動完了 (受信 URL={Prefix})。", prefix);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_httpListener is null) return;

        _cts?.Cancel();
        try
        {
            _httpListener.Stop();
        }
        catch (ObjectDisposedException) { /* ignore */ }

        if (_loopTask is not null)
        {
            try { await _loopTask.WaitAsync(TimeSpan.FromSeconds(5), ct); }
            catch (TimeoutException) { _logger.LogWarning("Listener loop did not finish in time."); }
            catch (OperationCanceledException) { /* ignore */ }
        }

        _logger.LogInformation("WebhookListener: stopped");
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _httpListener!.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _httpListener.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }   // listener.Stop() で投げられる
            catch (ObjectDisposedException) { break; }

            // 個別リクエストはバックグラウンドで処理 (次の受付をブロックしない)
            _ = Task.Run(() => HandleRequestAsync(context, ct), CancellationToken.None);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await RespondAsync(response, 405, "Method Not Allowed");
                return;
            }

            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync(ct);
            }

            SignalPayload payload;
            try
            {
                payload = SignalPayloadParser.Parse(body);
            }
            catch (WebhookParseException ex)
            {
                _logger.LogWarning(ex, "Webhook parse failed");
                await RespondAsync(response, 400, "Bad Request: " + ex.Message);
                return;
            }

            // TradeMode は固定 Auto (Webhook 経由のシグナルは常に自動と扱う)
            var outcome = await _handler.HandleAsync(payload, TradeMode.Auto, ct);

            await RespondAsync(response, 200, outcome.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in webhook handler");
            try { await RespondAsync(response, 500, "Internal Server Error"); } catch { }
        }
    }

    private static async Task RespondAsync(HttpListenerResponse response, int statusCode, string message)
    {
        response.StatusCode = statusCode;
        response.ContentType = "text/plain; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(message);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        if (!path.StartsWith('/')) path = "/" + path;
        if (!path.EndsWith('/')) path += "/";
        return path;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        try { _httpListener?.Close(); } catch { }
    }
}
