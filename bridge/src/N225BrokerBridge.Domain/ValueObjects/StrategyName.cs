using N225BrokerBridge.Domain.Common;

namespace N225BrokerBridge.Domain.ValueObjects;

/// <summary>
/// 戦略名 (TradingView アラート名等)。
/// 注文・建玉を戦略単位で紐付けるための識別子として使う (alert_name 相当)。
/// </summary>
public sealed record StrategyName
{
    /// <summary>戦略名文字列 (TV alert_name 相当)。</summary>
    public string Value { get; }

    /// <summary>
    /// 戦略名を生成する。
    /// </summary>
    /// <param name="value">戦略名 (例: "MyStrategy")。空文字不可。手動注文は "Manual" 固定。</param>
    /// <exception cref="InvalidValueObjectException">value が空または空白のみの場合。</exception>
    public StrategyName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidValueObjectException("StrategyName must not be empty.");
        Value = value;
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}
