namespace N225BrokerBridge.Domain.Orders;

/// <summary>
/// 注文ライフサイクル状態。
///
/// 状態遷移:
///   Created → Submitted → PartiallyFilled → Filled (正常完了)
///                       → Cancelled / Expired / Rejected (異常終了)
///
/// Created  : 集約生成直後。まだブローカーへ送っていない
/// Submitted: ブローカーへ送信完了。約定待ち
/// PartiallyFilled: 一部約定済み。残数量あり
/// Filled   : 全数量約定済み。終端状態
/// Cancelled: 取消完了。終端状態
/// Expired  : 期限切れ。終端状態
/// Rejected : ブローカーが受付拒否。終端状態
/// </summary>
public enum OrderState
{
    Created,
    Submitted,
    PartiallyFilled,
    Filled,
    Cancelled,
    Expired,
    Rejected
}

public static class OrderStateExtensions
{
    /// <summary>これ以上状態が変わらない終端状態か。</summary>
    public static bool IsTerminal(this OrderState state) => state switch
    {
        OrderState.Filled or OrderState.Cancelled or OrderState.Expired or OrderState.Rejected => true,
        _ => false
    };
}
