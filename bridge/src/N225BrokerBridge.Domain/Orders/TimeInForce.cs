namespace N225BrokerBridge.Domain.Orders;

/// <summary>
/// 注文の時間条件。
/// </summary>
public enum TimeInForce
{
    /// <summary>Fill And Kill: 即約定可能分のみ約定、残りは取消。</summary>
    FAK,
    /// <summary>Fill And Store: 即約定可能分は約定、残りは板に残す。</summary>
    FAS,
    /// <summary>Fill Or Kill: 全量約定できなければ全取消。</summary>
    FOK
}
