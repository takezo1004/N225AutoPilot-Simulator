namespace N225BrokerBridge.Domain.Orders;

/// <summary>
/// 注文の種別。新規建てか返済か。
/// kabu API では CashMargin=2 (新規) / CashMargin=3 (返済) に相当。
/// </summary>
public enum TradeType
{
    NewOrder,    // 新規建て
    ExitOrder    // 返済
}
