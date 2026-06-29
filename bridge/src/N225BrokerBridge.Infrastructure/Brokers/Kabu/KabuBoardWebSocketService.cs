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
                    At: ParseTime(board.CurrentPriceTime),
                    Volume: board.TradingVolume > 0 ? (decimal)board.TradingVolume : 0m,
                    VolumeAt: ParseVolumeTime(board.TradingVolumeTime))
                {
                    // ── 拡張 (2026-06-29): 同一 board スナップショットの OHLC ＋ 歩み値系を載せる。──
                    //   時刻は ParseVolumeTime (未提供は MinValue ＝受信側が「当該バー外」とみなす安全側)。
                    Open = board.OpeningPrice > 0 ? new Price((decimal)board.OpeningPrice) : (Price?)null,
                    OpenAt = ParseVolumeTime(board.OpeningPriceTime),
                    High = board.HighPrice > 0 ? new Price((decimal)board.HighPrice) : (Price?)null,
                    HighAt = ParseVolumeTime(board.HighPriceTime),
                    Low = board.LowPrice > 0 ? new Price((decimal)board.LowPrice) : (Price?)null,
                    LowAt = ParseVolumeTime(board.LowPriceTime),
                    BidQty = board.BidQty > 0 ? (decimal)board.BidQty : 0m,   // kabu BidQty = 通常 ASK 数量
                    AskQty = board.AskQty > 0 ? (decimal)board.AskQty : 0m,   // kabu AskQty = 通常 BID 数量
                    Vwap = board.VWAP > 0 ? (decimal)board.VWAP : 0m,
                    ChangeStatus = board.CurrentPriceChangeStatus ?? "",
                };

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

    // 売買高時刻。未提供/解析不能は MinValue (= 進行なし = 出来高を計上しない安全側)。
    private static DateTime ParseVolumeTime(string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw) && DateTime.TryParse(raw, out var dt)) return dt.ToUniversalTime();
        return DateTime.MinValue;
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
    [JsonPropertyName("CurrentPriceChangeStatus")] public string? CurrentPriceChangeStatus { get; set; } // 現値前値比較(0057=UP/0058=DOWN/0059=寄り初値等)
    [JsonPropertyName("BidPrice")] public double BidPrice { get; set; }
    [JsonPropertyName("BidQty")] public double BidQty { get; set; }                     // 最良売気配数量(= 通常 ASK 数量)
    [JsonPropertyName("AskPrice")] public double AskPrice { get; set; }
    [JsonPropertyName("AskQty")] public double AskQty { get; set; }                     // 最良買気配数量(= 通常 BID 数量)
    [JsonPropertyName("OpeningPrice")] public double OpeningPrice { get; set; }         // 始値(当日寄付)
    [JsonPropertyName("OpeningPriceTime")] public string? OpeningPriceTime { get; set; }
    [JsonPropertyName("HighPrice")] public double HighPrice { get; set; }               // 高値(当日累積)
    [JsonPropertyName("HighPriceTime")] public string? HighPriceTime { get; set; }
    [JsonPropertyName("LowPrice")] public double LowPrice { get; set; }                 // 安値(当日累積)
    [JsonPropertyName("LowPriceTime")] public string? LowPriceTime { get; set; }
    [JsonPropertyName("TradingVolume")] public double TradingVolume { get; set; }       // 当日累積売買高
    [JsonPropertyName("TradingVolumeTime")] public string? TradingVolumeTime { get; set; } // 売買高時刻
    [JsonPropertyName("VWAP")] public double VWAP { get; set; }                         // 当日VWAP
}
