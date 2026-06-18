using System.Text.Json.Serialization;

namespace N225BrokerBridge.Infrastructure.Brokers.Kabu.Dto;

/// <summary>
/// kabu API <c>/symbol/{symbol}@{exchange}?info=true</c> のレスポンス DTO。
/// 旧 N225OrderBridge の <c>SymbolElements</c> 相当 (主要フィールドのみ)。
/// 限月計算 (<c>DerivMonthCalculator</c>) で <c>TradeEnd</c> / <c>DerivMonth</c> を使う。
/// </summary>
public sealed class KabuSymbolFutureDto
{
    [JsonPropertyName("Symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("SymbolName")]
    public string? SymbolName { get; set; }

    [JsonPropertyName("DisplayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("Exchange")]
    public int Exchange { get; set; }

    [JsonPropertyName("ExchangeName")]
    public string? ExchangeName { get; set; }

    /// <summary>"2026/06" 形式の限月。</summary>
    [JsonPropertyName("DerivMonth")]
    public string? DerivMonth { get; set; }

    /// <summary>取引開始日 (yyyyMMdd 整数)。</summary>
    [JsonPropertyName("TradeStart")]
    public int TradeStart { get; set; }

    /// <summary>取引最終日 (SQ 日前日、yyyyMMdd 整数)。限月計算のキー。</summary>
    [JsonPropertyName("TradeEnd")]
    public int TradeEnd { get; set; }

    [JsonPropertyName("Underlyer")]
    public string? Underlyer { get; set; }

    [JsonPropertyName("ClearingPrice")]
    public decimal ClearingPrice { get; set; }
}
