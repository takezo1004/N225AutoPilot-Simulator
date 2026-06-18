using System.Text.Json;
using N225BrokerBridge.Application.Signals;

namespace N225BrokerBridge.Infrastructure.Webhooks;

/// <summary>
/// TradingView Webhook の生 JSON を <see cref="SignalPayload"/> にパースする。
///
/// 失敗時は <see cref="WebhookParseException"/> を投げる。
/// 呼び出し側 (HttpWebhookListener) は例外をキャッチして 400 応答 + ログ警告する。
/// </summary>
public static class SignalPayloadParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static SignalPayload Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new WebhookParseException("Empty webhook body.");

        RawWebhookPayload? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawWebhookPayload>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new WebhookParseException("Invalid JSON.", ex);
        }

        if (raw is null)
            throw new WebhookParseException("Webhook payload deserialized to null.");

        return ToSignalPayload(raw);
    }

    public static SignalPayload ToSignalPayload(RawWebhookPayload raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (string.IsNullOrWhiteSpace(raw.AlertName))
            throw new WebhookParseException("alert_name is required.");
        if (raw.Strategy is null)
            throw new WebhookParseException("strategy is required.");
        if (string.IsNullOrWhiteSpace(raw.Ticker))
            throw new WebhookParseException("ticker is required.");

        int interval = ParseInterval(raw.Interval);

        return new SignalPayload(
            AlertName: raw.AlertName,
            Interval: interval,
            OrderAction: raw.Strategy.OrderAction ?? string.Empty,
            MarketPosition: raw.Strategy.MarketPosition ?? string.Empty,
            PrevMarketPosition: raw.Strategy.PrevMarketPosition ?? string.Empty,
            OrderContracts: ToInt(raw.Strategy.OrderContracts),
            MarketPositionSize: ToInt(raw.Strategy.MarketPositionSize),
            PrevMarketPositionSize: ToInt(raw.Strategy.PrevMarketPositionSize),
            OrderPrice: ToDecimal(raw.Strategy.OrderPrice),
            SymbolTicker: raw.Ticker,
            Passphrase: raw.Passphrase);
    }

    private static int ParseInterval(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new WebhookParseException("interval is required.");
        if (!int.TryParse(raw.Trim(), out var v) || v <= 0)
            throw new WebhookParseException($"interval must be positive integer. Got: '{raw}'");
        return v;
    }

    private static int ToInt(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static decimal ToDecimal(double value) => (decimal)value;
}

public sealed class WebhookParseException : Exception
{
    public WebhookParseException(string message) : base(message) { }
    public WebhookParseException(string message, Exception inner) : base(message, inner) { }
}
