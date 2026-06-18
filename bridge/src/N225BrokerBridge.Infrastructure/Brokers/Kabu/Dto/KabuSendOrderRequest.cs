using System.Text.Json.Serialization;

namespace N225BrokerBridge.Infrastructure.Brokers.Kabu.Dto;

/// <summary>
/// kabu API /sendorder/future リクエスト DTO。
///
/// 仕様参照: kabu ステーション API ドキュメント
/// 新規/返済どちらも同じ URL を使う。返済時は CashMargin=3 + ClosePositions[] を指定。
/// </summary>
public sealed class KabuSendOrderRequest
{
    [JsonPropertyName("Password")]
    public string? Password { get; set; }            // 注文パスワード

    [JsonPropertyName("Symbol")]
    public string? Symbol { get; set; }              // 銘柄コード

    [JsonPropertyName("Exchange")]
    public int Exchange { get; set; }                // 23=大阪夜間 / 2=大阪日中

    [JsonPropertyName("TradeType")]
    public int TradeType { get; set; }               // 1=新規, 2=返済

    [JsonPropertyName("TimeInForce")]
    public int TimeInForce { get; set; }             // 1=FAS, 2=FAK, 3=FOK

    [JsonPropertyName("Side")]
    public string? Side { get; set; }                // "1"=売 / "2"=買

    [JsonPropertyName("Qty")]
    public int Qty { get; set; }

    [JsonPropertyName("ClosePositions")]
    public ClosePosition[]? ClosePositions { get; set; }   // 返済時のみ

    [JsonPropertyName("FrontOrderType")]
    public int FrontOrderType { get; set; }          // 18=指値, 27=逆指値, 30=成行, 等

    [JsonPropertyName("Price")]
    public double Price { get; set; }                // 0 = 成行

    [JsonPropertyName("ExpireDay")]
    public int ExpireDay { get; set; }               // 0=当日中

    /// <summary>
    /// 逆指値条件。FrontOrderType=30 (逆指値) のときのみ必須。
    /// それ以外は null (シリアライズ時に省略される)。
    /// </summary>
    [JsonPropertyName("ReverseLimitOrder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReverseLimitOrder? ReverseLimitOrder { get; set; }
}

/// <summary>
/// 逆指値条件ブロック (kabu API /sendorder/future 仕様の ReverseLimitOrder)。
/// 4 フィールドすべて必須。
/// </summary>
public sealed class ReverseLimitOrder
{
    /// <summary>トリガー価格。</summary>
    [JsonPropertyName("TriggerPrice")]
    public double TriggerPrice { get; set; }

    /// <summary>1=以下 (価格下落でトリガー、売建/売 stop)、2=以上 (価格上昇でトリガー、買建/買 stop)。</summary>
    [JsonPropertyName("UnderOver")]
    public int UnderOver { get; set; }

    /// <summary>ヒット後の執行条件。1=成行、2=指値。</summary>
    [JsonPropertyName("AfterHitOrderType")]
    public int AfterHitOrderType { get; set; }

    /// <summary>ヒット後の注文価格。成行 (AfterHitOrderType=1) なら 0、指値 (=2) なら指定価格。</summary>
    [JsonPropertyName("AfterHitPrice")]
    public double AfterHitPrice { get; set; }
}

/// <summary>
/// 返済時の建玉指定。複数指定可 (将来の一括返済最適化で活用予定)。
/// 現状は 1 件ずつ送る (旧 N225OrderBridge と同じ動作)。
/// </summary>
public sealed class ClosePosition
{
    [JsonPropertyName("HoldID")]
    public string? HoldID { get; set; }

    [JsonPropertyName("Qty")]
    public int Qty { get; set; }
}

public sealed class KabuSendOrderResponse
{
    [JsonPropertyName("Result")]
    public int Result { get; set; }

    [JsonPropertyName("OrderId")]
    public string? OrderId { get; set; }

    [JsonPropertyName("Code")]
    public int? Code { get; set; }                   // エラー時のみ

    [JsonPropertyName("Message")]
    public string? Message { get; set; }
}
