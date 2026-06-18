using System.Text.Json.Serialization;

namespace N225BrokerBridge.Infrastructure.Brokers.Kabu.Dto;

/// <summary>
/// kabu API /positions レスポンスの 1 建玉。
/// </summary>
public sealed class KabuPositionDto
{
    [JsonPropertyName("ExecutionID")]
    public string? ExecutionID { get; set; }

    [JsonPropertyName("HoldID")]
    public string? HoldID { get; set; }

    [JsonPropertyName("Symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("SymbolName")]
    public string? SymbolName { get; set; }

    [JsonPropertyName("Exchange")]
    public int Exchange { get; set; }

    [JsonPropertyName("Side")]
    public string? Side { get; set; }       // "1"=売建 / "2"=買建

    [JsonPropertyName("LeavesQty")]
    public double LeavesQty { get; set; }

    [JsonPropertyName("HoldQty")]
    public double HoldQty { get; set; }

    [JsonPropertyName("Price")]
    public double Price { get; set; }       // 建値

    [JsonPropertyName("ExecutionDay")]
    public int ExecutionDay { get; set; }   // 約定日 yyyyMMdd
}
