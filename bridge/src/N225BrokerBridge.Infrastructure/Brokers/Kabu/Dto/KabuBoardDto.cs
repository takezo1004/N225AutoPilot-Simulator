using System.Text.Json.Serialization;

namespace N225BrokerBridge.Infrastructure.Brokers.Kabu.Dto;

/// <summary>
/// kabu API /board/{symbol}@{exchange} レスポンス (気配)。
/// 主要フィールドのみ抜粋。
/// </summary>
public sealed class KabuBoardDto
{
    [JsonPropertyName("Symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("CurrentPrice")]
    public double CurrentPrice { get; set; }

    [JsonPropertyName("BidPrice")]
    public double BidPrice { get; set; }

    [JsonPropertyName("BidQty")]
    public double BidQty { get; set; }

    [JsonPropertyName("AskPrice")]
    public double AskPrice { get; set; }

    [JsonPropertyName("AskQty")]
    public double AskQty { get; set; }

    [JsonPropertyName("CurrentPriceTime")]
    public string? CurrentPriceTime { get; set; }
}
