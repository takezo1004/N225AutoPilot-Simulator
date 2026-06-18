namespace N225BrokerBridge.Domain.Orders;

/// <summary>
/// 注文タイプ (約定条件)。
/// </summary>
public enum OrderType
{
    /// <summary>成行注文。価格指定なしで即時約定。</summary>
    Market,
    /// <summary>指値注文。指定価格以下 (買) / 以上 (売) のみ約定。</summary>
    Limit,
    /// <summary>逆指値注文。指定価格到達でトリガー (ストップロス等)。</summary>
    Stop,
    /// <summary>最良気配注文。kabu API の SelectedOrder=1 相当。</summary>
    BestMarket
}
