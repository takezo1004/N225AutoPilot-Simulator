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
/// データ基準＝日経225ミニのみ転送 (倍率 100 = Mini で識別・Micro=10 等は捨てる)。
///   ミニ/マイクロのティック混在を防ぎ、受信側 (LocalEngine) が常にミニ1本で足を生成できるようにする。
/// volume は kabu の当日累積売買高 (TradingVolume) を「売買高時刻 (TradingVolumeTime) が進んだ時だけ」
///   増分として数え、その売買高時刻のバーに入るよう timestamp も売買高時刻にする
///   (受信側 OHLCManager は per-bar で += するだけ・無変更)。初回/セッションリセットは 0。
/// </summary>
public sealed class AiTickForwarderService : IHostedService, IDisposable
{
    private const string Host = "127.0.0.1";
    private const int Port = 5000;
    private const int MiniMultiplier = 100;   // 日経225ミニの ProfitMultiplier (= データ基準銘柄の識別子)

    private readonly IPriceUpdateNotifier _notifier;
    private readonly IContractMultiplierResolver _multipliers;
    private readonly ILogger<AiTickForwarderService> _logger;

    private readonly object _gate = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private volatile bool _connected;
    private CancellationTokenSource? _cts;
    private decimal _prevCumVolume = -1m;                  // 直前のミニ当日累積売買高
    private DateTime _prevVolumeAt = DateTime.MinValue;    // 直前の売買高時刻 (進行時のみ出来高計上)

    public AiTickForwarderService(
        IPriceUpdateNotifier notifier,
        IContractMultiplierResolver multipliers,
        ILogger<AiTickForwarderService> logger)
    {
        _notifier = notifier;
        _multipliers = multipliers;
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

        // データ基準＝日経225ミニのみ転送 (倍率 100)。Micro(10)・未登録(=銘柄解決前)は捨てる。
        // → ミニ/マイクロのティック混在を防ぎ、受信側の足を常にミニ1本に保つ。
        if (_multipliers.Resolve(tick.Symbol.Value) != MiniMultiplier) return;

        // 出来高: 売買高時刻 (TradingVolumeTime) が進んだ時だけ当日累積の増分を計上する。
        //   累積をそのまま足すと桁違い／毎 tick 差分は境界跨ぎで誤計上 → 売買高時刻基準が正しい。
        var cum = tick.Volume;
        var vAt = tick.VolumeAt;                          // UTC (未提供は MinValue)
        decimal volInc = 0m;
        if (_prevVolumeAt == DateTime.MinValue)
        {
            _prevVolumeAt = vAt; _prevCumVolume = cum;    // 初回はベースライン化 (計上しない)
        }
        else if (vAt > _prevVolumeAt)
        {
            if (cum >= _prevCumVolume) volInc = cum - _prevCumVolume;  // 正常増分 (cum<prev はセッションリセット→0)
            _prevVolumeAt = vAt; _prevCumVolume = cum;
        }

        try
        {
            // 出来高があれば売買高時刻のバーへ・無ければ現値時刻 (価格更新のみ)。UTC→JST(+9h)。
            var jst = (volInc > 0m ? vAt : tick.At).AddHours(9);
            var line = FormattableString.Invariant(
                $"{{\"timestamp\":\"{jst:yyyy/MM/dd HH:mm:ss}\",\"close\":{tick.LastPrice.Value},\"volume\":{(long)volInc}}}\n");
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
