using N225BrokerBridge.Domain.Common;

namespace N225BrokerBridge.Domain.ValueObjects;

/// <summary>
/// 注文枚数・建玉枚数を表す値オブジェクト。
/// 整数のみ (先物の最小単位は 1 枚)。非負制約あり。
/// </summary>
public readonly record struct Quantity
{
    /// <summary>枚数の数値表現 (非負整数)。</summary>
    public int Value { get; }

    /// <summary>
    /// 枚数を生成する。
    /// </summary>
    /// <param name="value">枚数 (0 以上)。負の値が渡された場合は例外。</param>
    /// <exception cref="InvalidValueObjectException">value が負の場合。</exception>
    public Quantity(int value)
    {
        if (value < 0)
            throw new InvalidValueObjectException($"Quantity must be non-negative. Got: {value}");
        Value = value;
    }

    /// <summary>ゼロ枚 (累計約定数量の初期値などに使う)。</summary>
    public static Quantity Zero => new(0);

    /// <summary>ゼロ枚かどうか。</summary>
    public bool IsZero => Value == 0;
    /// <summary>1 枚以上かどうか。</summary>
    public bool IsPositive => Value > 0;

    /// <summary>2 つの枚数を加算する。</summary>
    public static Quantity operator +(Quantity a, Quantity b) => new(a.Value + b.Value);
    /// <summary>2 つの枚数を減算する (結果が負になる場合は例外)。</summary>
    public static Quantity operator -(Quantity a, Quantity b) => new(a.Value - b.Value);
    /// <summary>左辺が右辺より小さいか。</summary>
    public static bool operator <(Quantity a, Quantity b) => a.Value < b.Value;
    /// <summary>左辺が右辺より大きいか。</summary>
    public static bool operator >(Quantity a, Quantity b) => a.Value > b.Value;
    /// <summary>左辺が右辺以下か。</summary>
    public static bool operator <=(Quantity a, Quantity b) => a.Value <= b.Value;
    /// <summary>左辺が右辺以上か。</summary>
    public static bool operator >=(Quantity a, Quantity b) => a.Value >= b.Value;

    /// <summary>2 つの Quantity のうち小さい方を返す (跨ぎ消化計算で多用する)。</summary>
    public static Quantity Min(Quantity a, Quantity b) => a.Value <= b.Value ? a : b;

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
