using N225BrokerBridge.Domain.Common;

namespace N225BrokerBridge.Domain.ValueObjects;

/// <summary>
/// 価格を表す値オブジェクト。
/// 日経225ミニは最小刻み 5 円だが、検証はドメイン側ではなくブローカーアダプタ側で行う
/// (銘柄ごとに刻み幅が違うため)。本値オブジェクトは「非負の数値」のみを保証する。
/// </summary>
public readonly record struct Price
{
    /// <summary>価格の数値表現 (非負)。</summary>
    public decimal Value { get; }

    /// <summary>
    /// 価格を生成する。
    /// </summary>
    /// <param name="value">価格 (0 以上)。負の値が渡された場合は例外。</param>
    /// <exception cref="InvalidValueObjectException">value が負の場合。</exception>
    public Price(decimal value)
    {
        if (value < 0)
            throw new InvalidValueObjectException($"Price must be non-negative. Got: {value}");
        Value = value;
    }

    /// <summary>ゼロ価格 (成行注文で Price=0 として使う)。</summary>
    public static Price Zero => new(0m);

    /// <summary>ゼロ価格かどうか。</summary>
    public bool IsZero => Value == 0m;

    /// <summary>2 つの価格を加算する。</summary>
    public static Price operator +(Price a, Price b) => new(a.Value + b.Value);
    /// <summary>2 つの価格を減算する (結果が負になる場合は例外)。</summary>
    public static Price operator -(Price a, Price b) => new(a.Value - b.Value);
    /// <summary>左辺が右辺より小さいか。</summary>
    public static bool operator <(Price a, Price b) => a.Value < b.Value;
    /// <summary>左辺が右辺より大きいか。</summary>
    public static bool operator >(Price a, Price b) => a.Value > b.Value;
    /// <summary>左辺が右辺以下か。</summary>
    public static bool operator <=(Price a, Price b) => a.Value <= b.Value;
    /// <summary>左辺が右辺以上か。</summary>
    public static bool operator >=(Price a, Price b) => a.Value >= b.Value;

    /// <inheritdoc/>
    public override string ToString() => Value.ToString("0.##");
}
