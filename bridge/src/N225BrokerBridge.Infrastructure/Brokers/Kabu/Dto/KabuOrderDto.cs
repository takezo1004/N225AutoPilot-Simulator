using System.Text.Json.Serialization;

namespace N225BrokerBridge.Infrastructure.Brokers.Kabu.Dto;

/// <summary>
/// kabu API /orders レスポンスの 1 注文。
/// </summary>
public sealed class KabuOrderDto
{
    [JsonPropertyName("ID")]
    public string? ID { get; set; }       // OrderID

    [JsonPropertyName("State")]
    public int State { get; set; }        // 1=待機, 2=処理中, 3=処理済, 4=訂正取消, 5=終了

    [JsonPropertyName("Symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("Side")]
    public string? Side { get; set; }

    [JsonPropertyName("CashMargin")]
    public int CashMargin { get; set; }   // 2=新規, 3=返済

    [JsonPropertyName("OrderQty")]
    public double OrderQty { get; set; }  // 注文枚数

    [JsonPropertyName("CumQty")]
    public double CumQty { get; set; }    // 約定累計

    [JsonPropertyName("Price")]
    public double Price { get; set; }

    [JsonPropertyName("RecvTime")]
    public string? RecvTime { get; set; } // ISO 8601 etc

    [JsonPropertyName("Details")]
    public KabuOrderDetailDto[]? Details { get; set; }
}

/// <summary>
/// 注文明細 (約定 fill / 取消等の履歴)。
/// </summary>
public sealed class KabuOrderDetailDto
{
    [JsonPropertyName("ExecutionID")]
    public string? ExecutionID { get; set; }

    [JsonPropertyName("RecType")]
    public int RecType { get; set; }   // 1=受付, 2=受付取消, 3=失効, 4=訂正, 5=取消, 7=注文期限切れ, 8=約定

    // kabu は約定明細以外 (受付/取消/失効等) で Qty/Price/ExecutionDay を null で返す。
    // 非 nullable double だと JsonException で /orders 全体のパースが失敗するため必ず nullable で受け取る。
    [JsonPropertyName("Qty")]
    public double? Qty { get; set; }

    [JsonPropertyName("Price")]
    public double? Price { get; set; }

    [JsonPropertyName("ExecutionDay")]
    public string? ExecutionDay { get; set; }   // ISO 8601
}
