using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Infrastructure.Brokers.Kabu;

/// <summary>
/// kabu /websocket に接続し、登録銘柄の board (気配・現在値) push を受信して
/// <see cref="KabuAdapter.PriceStream"/> に <see cref="PriceTick"/> を流す。
///
/// 旧 N225OrderBridge の WebSocket_Future クラス相当 (Python 転送部分は除外)。
///
/// 動作:
///   - 起動時に ClientWebSocket で接続
///   - 切断時は 5 秒待機 → 自動再接続
///   - 受信メッセージを JSON パース → PriceTick にマップ
///   - 銘柄登録は KabuApiClient.RegisterSymbolAsync 経由 (今は stub)
/// </summary>
public sealed class KabuBoardWebSocketService : IHostedService, IPriceUpdateNotifier, IDisposable
{
    private readonly KabuOptions _options;
    private readonly KabuAdapter _adapter;
    private readonly ILogger<KabuBoardWebSocketService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>
    /// 受信した PriceTick を UI 等に再発火するための通知イベント。
    /// IBrokerAdapter.PriceStream (Rx) と別経路で、Rx 依存しない購読者向け。
    /// </summary>
    public event EventHandler<PriceTick>? PriceUpdated;

    public KabuBoardWebSocketService(
        IOptions<KabuOptions> options,
        KabuAdapter adapter,
        ILogger<KabuBoardWebSocketService> logger)
    {
        _options = options.Value;
        _adapter = adapter;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => ConnectionLoopAsync(_cts.Token));
        _logger.LogInformation("板情報 WebSocket サービス起動 (接続先={Url})。", _options.WebSocketUrl);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_loopTask is not null)
        {
            try { await _loopTask.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken); }
            catch (TimeoutException) { _logger.LogWarning("WebSocket ループの停止がタイムアウト。"); }
            catch (OperationCanceledException) { }
        }
        _logger.LogInformation("板情報 WebSocket サービス停止。");
    }

    private async Task ConnectionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                _logger.LogInformation("板情報 WebSocket 接続中... ({Url})", _options.WebSocketUrl);
                await ws.ConnectAsync(new Uri(_options.WebSocketUrl), ct);
                _logger.LogInformation("板情報 WebSocket 接続完了。");
                await ReceiveLoopAsync(ws, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "板情報 WebSocket 接続失敗 (kabu Station 未起動の可能性)。");
            }

            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var ms = new MemoryStream();

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            ms.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("板情報 WebSocket サーバ切断。");
                    return;
                }
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            if (string.IsNullOrWhiteSpace(json)) continue;

            try
            {
                var board = JsonSerializer.Deserialize<KabuBoardPushDto>(json);
                if (board is null) continue;
                if (string.IsNullOrEmpty(board.Symbol)) continue;

                var tick = new PriceTick(
                    BrokerCode: _adapter.BrokerCode,
                    Symbol: new SymbolCode(board.Symbol),
                    LastPrice: new Price(SafeDecimal(board.CurrentPrice)),
                    BidPrice: new Price(SafeDecimal(board.BidPrice)),
                    AskPrice: new Price(SafeDecimal(board.AskPrice)),
                    At: ParseTime(board.CurrentPriceTime));

                _adapter.PushPriceTick(tick);
                _logger.LogDebug(
                    "板情報 push: {Symbol} 現在={Last} BID={Bid} ASK={Ask}",
                    tick.Symbol.Value, tick.LastPrice.Value, tick.BidPrice.Value, tick.AskPrice.Value);

                try
                {
                    PriceUpdated?.Invoke(this, tick);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PriceUpdated handler threw");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "WebSocket parse failed (skip)");
            }
        }
    }

    private static decimal SafeDecimal(double value) => value <= 0 ? 0m : (decimal)value;

    private static DateTime ParseTime(string? raw)
    {
        if (DateTime.TryParse(raw, out var dt)) return dt.ToUniversalTime();
        return DateTime.UtcNow;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

/// <summary>
/// kabu /websocket push メッセージ (board)。主要フィールドのみ抜粋。
/// </summary>
internal sealed class KabuBoardPushDto
{
    [JsonPropertyName("Symbol")] public string? Symbol { get; set; }
    [JsonPropertyName("CurrentPrice")] public double CurrentPrice { get; set; }
    [JsonPropertyName("CurrentPriceTime")] public string? CurrentPriceTime { get; set; }
    [JsonPropertyName("BidPrice")] public double BidPrice { get; set; }
    [JsonPropertyName("AskPrice")] public double AskPrice { get; set; }
}
