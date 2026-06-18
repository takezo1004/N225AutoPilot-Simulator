using System.Text.Json.Serialization;

namespace N225BrokerBridge.Infrastructure.Brokers.Kabu.Dto;

/// <summary>
/// kabu API /symbolname/future レスポンス。
/// FutureCode (例: "NK225mini") + DerivMonth (0=現月) を投げると、
/// 該当する具体的な銘柄コード ("167060019" 等) と銘柄名を返す。
///
/// 旧 N225OrderBridge の Symbolname_Future クラス相当。
/// </summary>
public sealed class KabuSymbolNameDto
{
    [JsonPropertyName("Symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("SymbolName")]
    public string? SymbolName { get; set; }
}
