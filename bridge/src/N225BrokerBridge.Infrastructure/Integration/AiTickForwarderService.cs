using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Domain.Brokers;

namespace N225BrokerBridge.Infrastructure.Integration;

/// <summary>
/// kabu board ティックを AI (N225StrategyAI) の OHLC 受信サーバ (127.0.0.1:5000) へ
/// 改行区切り JSON で転送する **追加サービス**。
///
/// 形式 (AI 側 tcp_receiver.py の契約):
///   {"timestamp":"yyyy/MM/dd HH:mm:ss","close":&lt;price&gt;,"volume":&lt;vol&gt;}\n   (JST)
///
/// 設計: ブリッジ→AI のデータ供給路。AI は自前で kabu に接続しない (単一トークン則の衝突回避)。
/// 既存ロジックは一切変更しない純粋な追加。price stream (IPriceUpdateNotifier) を購読して送るだけ。
/// AI 側 (TCP サーバ) が未起動でも握りつぶして 3 秒ごとに再接続を試みる。
///
/// ※ volume は現状 0 (board push DTO に出来高未抽出)。15分足の価格パイプライン実証が目的。
///   出来高の正式対応は feed 整合フェーズ (TradingVolume 抽出) で行う。
/// </summary>
public sealed class AiTickForwarderService : IHostedService, IDisposable
{
    private const string Host = "127.0.0.1";
    private const int Port = 5000;

    private readonly IPriceUpdateNotifier _notifier;
    private readonly ILogger<AiTickForwarderService> _logger;

    private readonly object _gate = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private volatile bool _connected;
    private CancellationTokenSource? _cts;

    public AiTickForwarderService(IPriceUpdateNotifier notifier, ILogger<AiTickForwarderService> logger)
    {
        _notifier = notifier;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _notifier.PriceUpdated += OnPriceUpdated;
        _ = Task.Run(() => ConnectLoopAsync(_cts.Token));
        _logger.LogInformation("AI tick 転送サービス起動 (→ {Host}:{Port}・AI 未起動時は再接続待機)。", Host, Port);
        return Task.CompletedTask;
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!_connected)
            {
                try
                {
                    var client = new TcpClient();
                    await client.ConnectAsync(Host, Port, ct);
                    lock (_gate)
                    {
                        _client = client;
                        _stream = client.GetStream();
                        _connected = true;
                    }
                    _logger.LogInformation("AI tick 転送: 接続成功 {Host}:{Port}", Host, Port);
                }
                catch
                {
                    // AI 受信サーバ未起動。静かに再試行。
                }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(3), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void OnPriceUpdated(object? sender, PriceTick tick)
    {
        if (!_connected) return;
        try
        {
            // tick.At は UTC。JST(+9h) のウォール時刻で送る (AI は %Y/%m/%d %H:%M:%S で解釈)。
            var jst = tick.At.AddHours(9);
            var line = FormattableString.Invariant(
                $"{{\"timestamp\":\"{jst:yyyy/MM/dd HH:mm:ss}\",\"close\":{tick.LastPrice.Value},\"volume\":0}}\n");
            var bytes = Encoding.UTF8.GetBytes(line);
            lock (_gate)
            {
                if (_stream is null) return;
                _stream.Write(bytes, 0, bytes.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AI tick 送信失敗 → 再接続へ");
            lock (_gate)
            {
                _connected = false;
                _stream = null;
                _client?.Dispose();
                _client = null;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _notifier.PriceUpdated -= OnPriceUpdated;
        _cts?.Cancel();
        lock (_gate)
        {
            _stream?.Dispose();
            _client?.Dispose();
            _connected = false;
        }
        _logger.LogInformation("AI tick 転送サービス停止。");
        return Task.CompletedTask;
    }

    public void Dispose() => _cts?.Dispose();
}
