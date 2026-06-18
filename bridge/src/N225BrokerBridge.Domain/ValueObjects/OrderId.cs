using N225BrokerBridge.Domain.Common;

namespace N225BrokerBridge.Domain.ValueObjects;

/// <summary>
/// 注文 ID。ブローカーが採番した文字列をラップする。
/// ブローカー単位で一意 (異なるブローカー間で同じ文字列があり得るが、
/// アプリ内では <see cref="BrokerCode"/> と組み合わせて一意性を確保する)。
/// </summary>
public sealed record OrderId
{
    /// <summary>注文 ID 文字列 (ブローカー採番)。</summary>
    public string Value { get; }

    /// <summary>
    /// 注文 ID を生成する。
    /// </summary>
    /// <param name="value">ブローカーが返した注文 ID。空文字不可。</param>
    /// <exception cref="InvalidValueObjectException">value が空または空白のみの場合。</exception>
    public OrderId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidValueObjectException("OrderId must not be empty.");
        Value = value;
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}
