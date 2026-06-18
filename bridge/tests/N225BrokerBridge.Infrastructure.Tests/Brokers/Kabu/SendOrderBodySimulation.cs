using System.Text.Json;
using System.Text.RegularExpressions;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.ValueObjects;
using N225BrokerBridge.Infrastructure.Brokers.Kabu;
using Xunit;
using Xunit.Abstractions;

namespace N225BrokerBridge.Infrastructure.Tests.Brokers.Kabu;

/// <summary>
/// /sendorder/future 送信 body の見え方確認用シミュレーション (実機停止不要)。
/// 修正後のソースで、5/22 20:45 と同等のシグナルが来たとき kabu API に渡される
/// JSON body をログと同じ形式で出力する。
/// </summary>
public class SendOrderBodySimulation
{
    private readonly ITestOutputHelper _out;
    public SendOrderBodySimulation(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Simulate_5_22_2045_Signal_AfterFix()
    {
        // ブリッジで Micro (161060023) が選択されている前提
        var req = new OrderRequest(
            CorrelationId: Guid.NewGuid(),
            Strategy: new StrategyName("mesa77s3"),
            Interval: 15,
            TradeMode: TradeMode.Auto,
            Symbol: new SymbolCode("161060023"),    // ← provider 由来 (修正後)
            Side: Side.Buy,
            OrderType: OrderType.Limit,
            TimeInForce: TimeInForce.FAS,
            Quantity: new Quantity(3),
            LimitPrice: new Price(63030m),
            StopPrice: Price.Zero);

        var kabu = KabuMappers.ToKabuRequest(req, orderPassword: "takao102769", exchange: 24);
        var raw = JsonSerializer.Serialize(kabu);
        var masked = Regex.Replace(raw, "(\"Password\"\\s*:\\s*\")[^\"]*(\")", "$1***$2");

        _out.WriteLine("── 生 body (内部、ログには出さない) ──");
        _out.WriteLine($"/sendorder/future 送信 body={raw}");
        _out.WriteLine("");
        _out.WriteLine("── マスク後 body (これが実ログに出る) ──");
        _out.WriteLine($"/sendorder/future 送信 body={masked}");

        // 期待値 (修正前は "NK225M1!" が入っていたところに 161060023 が入る)
        Assert.Equal("161060023", kabu.Symbol);
        Assert.Equal("***", Regex.Match(masked, "\"Password\":\"([^\"]*)\"").Groups[1].Value);
    }
}
