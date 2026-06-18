namespace N225BrokerBridge.Domain.ValueObjects;

/// <summary>
/// 取引モード: 自動 (Webhook 等から発注) / 手動 (UI 操作で発注)。
/// 返済対象の建玉選択時に、自動取引で生じた建玉のみを対象にする等のフィルタに使う。
/// </summary>
public enum TradeMode
{
    /// <summary>手動 (UI のボタン操作で発注した注文・建玉)。</summary>
    Manual = 0,
    /// <summary>自動 (Webhook / シグナル経由で発注した注文・建玉)。</summary>
    Auto = 1
}
