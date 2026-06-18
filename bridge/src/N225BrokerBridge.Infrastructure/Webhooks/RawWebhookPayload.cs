using System.Text.Json.Serialization;

namespace N225BrokerBridge.Infrastructure.Webhooks;

/// <summary>
/// TradingView Webhook の素 JSON 構造に対応する DTO。
/// 旧 N225OrderBridge の TcpClientModel.cs と同じフィールド名を踏襲し、
/// 既存 TradingView アラート設定がそのまま動くようにする。
///
/// 想定 JSON:
/// {
///   "passphrase": "...",
///   "alert_name": "V7-7-fixed",
///   "interval": "5",
///   "ticker": "OSE:NK225M1!",
///   "strategy": {
///     "order_action": "buy",
///     "market_position": "long",
///     "prev_market_position": "flat",
///     "order_contracts": 3,
///     "market_position_size": 3,
///     "prev_market_position_size": 0,
///     "order_price": 38000
///   }
/// }
/// </summary>
public sealed class RawWebhookPayload
{
    [JsonPropertyName("passphrase")]
    public string? Passphrase { get; set; }

    [JsonPropertyName("alert_name")]
    public string? AlertName { get; set; }

    /// <summary>"1" / "5" / "60" などの文字列で来ることが多い。</summary>
    [JsonPropertyName("interval")]
    public string? Interval { get; set; }

    [JsonPropertyName("ticker")]
    public string? Ticker { get; set; }

    [JsonPropertyName("strategy")]
    public RawStrategy? Strategy { get; set; }
}

public sealed class RawStrategy
{
    [JsonPropertyName("order_action")]
    public string? OrderAction { get; set; }

    [JsonPropertyName("market_position")]
    public string? MarketPosition { get; set; }

    [JsonPropertyName("prev_market_position")]
    public string? PrevMarketPosition { get; set; }

    [JsonPropertyName("order_contracts")]
    public double OrderContracts { get; set; }

    [JsonPropertyName("market_position_size")]
    public double MarketPositionSize { get; set; }

    [JsonPropertyName("prev_market_position_size")]
    public double PrevMarketPositionSize { get; set; }

    [JsonPropertyName("order_price")]
    public double OrderPrice { get; set; }
}
