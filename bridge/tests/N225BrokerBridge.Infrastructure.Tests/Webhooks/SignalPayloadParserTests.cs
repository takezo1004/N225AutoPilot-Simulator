using N225BrokerBridge.Infrastructure.Webhooks;
using Xunit;

namespace N225BrokerBridge.Infrastructure.Tests.Webhooks;

public class SignalPayloadParserTests
{
    // ── 正常系: V7-7 fixed の典型ペイロード ──────────────────────

    [Fact]
    public void Parse_FullValidPayload_Succeeds()
    {
        // V7-7 fixed: 3 枚新規買い
        var json = """
        {
          "passphrase": "secret",
          "alert_name": "V7-7-fixed",
          "interval": "5",
          "ticker": "OSE:NK225M1!",
          "strategy": {
            "order_action": "buy",
            "market_position": "long",
            "prev_market_position": "flat",
            "order_contracts": 3,
            "market_position_size": 3,
            "prev_market_position_size": 0,
            "order_price": 38000
          }
        }
        """;

        var payload = SignalPayloadParser.Parse(json);

        Assert.Equal("V7-7-fixed", payload.AlertName);
        Assert.Equal(5, payload.Interval);
        Assert.Equal("buy", payload.OrderAction);
        Assert.Equal("long", payload.MarketPosition);
        Assert.Equal("flat", payload.PrevMarketPosition);
        Assert.Equal(3, payload.OrderContracts);
        Assert.Equal(3, payload.MarketPositionSize);
        Assert.Equal(0, payload.PrevMarketPositionSize);
        Assert.Equal(38000m, payload.OrderPrice);
        Assert.Equal("OSE:NK225M1!", payload.SymbolTicker);
        Assert.Equal("secret", payload.Passphrase);
    }

    [Fact]
    public void Parse_PartialExitPayload_Succeeds()
    {
        // V7-7 fixed: 部分返済 1 枚 (long → long で qty 減)
        var json = """
        {
          "alert_name": "V7-7-fixed",
          "interval": "5",
          "ticker": "OSE:NK225M1!",
          "strategy": {
            "order_action": "sell",
            "market_position": "long",
            "prev_market_position": "long",
            "order_contracts": 1,
            "market_position_size": 2,
            "prev_market_position_size": 3,
            "order_price": 38050
          }
        }
        """;

        var payload = SignalPayloadParser.Parse(json);
        Assert.Equal("sell", payload.OrderAction);
        Assert.Equal(1, payload.OrderContracts);
        Assert.Equal(2, payload.MarketPositionSize);
        Assert.Equal(3, payload.PrevMarketPositionSize);
    }

    [Fact]
    public void Parse_DecimalContracts_RoundedToInt()
    {
        // TradingView は時々 contracts を小数で送ってくる (1.0 など)
        var json = """
        {
          "alert_name": "X",
          "interval": "1",
          "ticker": "S",
          "strategy": {
            "order_action": "buy",
            "market_position": "long",
            "prev_market_position": "flat",
            "order_contracts": 1.0,
            "market_position_size": 1.0,
            "prev_market_position_size": 0,
            "order_price": 0
          }
        }
        """;

        var payload = SignalPayloadParser.Parse(json);
        Assert.Equal(1, payload.OrderContracts);
    }

    [Fact]
    public void Parse_NoPassphrase_PassphraseIsNull()
    {
        var json = """
        {
          "alert_name": "X",
          "interval": "5",
          "ticker": "S",
          "strategy": {
            "order_action": "buy", "market_position": "long",
            "prev_market_position": "flat", "order_contracts": 1,
            "market_position_size": 1, "prev_market_position_size": 0,
            "order_price": 0
          }
        }
        """;
        var payload = SignalPayloadParser.Parse(json);
        Assert.Null(payload.Passphrase);
    }

    // ── 異常系 ──────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyBody_Throws()
    {
        Assert.Throws<WebhookParseException>(() => SignalPayloadParser.Parse(""));
        Assert.Throws<WebhookParseException>(() => SignalPayloadParser.Parse("   "));
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        Assert.Throws<WebhookParseException>(() => SignalPayloadParser.Parse("{not json"));
    }

    [Fact]
    public void Parse_MissingAlertName_Throws()
    {
        var json = """
        {
          "interval": "5", "ticker": "S",
          "strategy": {
            "order_action": "buy", "market_position": "long",
            "prev_market_position": "flat", "order_contracts": 1,
            "market_position_size": 1, "prev_market_position_size": 0,
            "order_price": 0
          }
        }
        """;
        var ex = Assert.Throws<WebhookParseException>(() => SignalPayloadParser.Parse(json));
        Assert.Contains("alert_name", ex.Message);
    }

    [Fact]
    public void Parse_MissingStrategy_Throws()
    {
        var json = """
        { "alert_name": "X", "interval": "5", "ticker": "S" }
        """;
        var ex = Assert.Throws<WebhookParseException>(() => SignalPayloadParser.Parse(json));
        Assert.Contains("strategy", ex.Message);
    }

    [Fact]
    public void Parse_MissingTicker_Throws()
    {
        var json = """
        {
          "alert_name": "X", "interval": "5",
          "strategy": {
            "order_action": "buy", "market_position": "long",
            "prev_market_position": "flat", "order_contracts": 1,
            "market_position_size": 1, "prev_market_position_size": 0,
            "order_price": 0
          }
        }
        """;
        var ex = Assert.Throws<WebhookParseException>(() => SignalPayloadParser.Parse(json));
        Assert.Contains("ticker", ex.Message);
    }

    [Fact]
    public void Parse_InvalidInterval_Throws()
    {
        var json = """
        {
          "alert_name": "X", "interval": "abc", "ticker": "S",
          "strategy": {
            "order_action": "buy", "market_position": "long",
            "prev_market_position": "flat", "order_contracts": 1,
            "market_position_size": 1, "prev_market_position_size": 0,
            "order_price": 0
          }
        }
        """;
        Assert.Throws<WebhookParseException>(() => SignalPayloadParser.Parse(json));
    }

    [Fact]
    public void Parse_ZeroInterval_Throws()
    {
        var json = """
        {
          "alert_name": "X", "interval": "0", "ticker": "S",
          "strategy": {
            "order_action": "buy", "market_position": "long",
            "prev_market_position": "flat", "order_contracts": 1,
            "market_position_size": 1, "prev_market_position_size": 0,
            "order_price": 0
          }
        }
        """;
        Assert.Throws<WebhookParseException>(() => SignalPayloadParser.Parse(json));
    }

    [Fact]
    public void Parse_TrailingCommasAllowed()
    {
        var json = """
        {
          "alert_name": "X",
          "interval": "5",
          "ticker": "S",
          "strategy": {
            "order_action": "buy",
            "market_position": "long",
            "prev_market_position": "flat",
            "order_contracts": 1,
            "market_position_size": 1,
            "prev_market_position_size": 0,
            "order_price": 0,
          },
        }
        """;
        var payload = SignalPayloadParser.Parse(json);
        Assert.Equal("X", payload.AlertName);
    }

    [Fact]
    public void Parse_CaseInsensitiveFieldNames()
    {
        // システムによっては大文字が来る可能性
        var json = """
        {
          "Alert_Name": "X",
          "Interval": "5",
          "Ticker": "S",
          "Strategy": {
            "Order_Action": "buy",
            "Market_Position": "long",
            "Prev_Market_Position": "flat",
            "Order_Contracts": 1,
            "Market_Position_Size": 1,
            "Prev_Market_Position_Size": 0,
            "Order_Price": 0
          }
        }
        """;
        var payload = SignalPayloadParser.Parse(json);
        Assert.Equal("X", payload.AlertName);
    }
}
