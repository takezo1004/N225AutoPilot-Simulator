using N225BrokerBridge.Domain.Common;

namespace N225BrokerBridge.Domain.ValueObjects;

/// <summary>
/// 売買サイド (Buy / Sell)。
/// kabu API では "1"=売 / "2"=買 (注文発注時)、UI では "買"/"売" で表示する。
/// ドメイン内では <see cref="Buy"/> / <see cref="Sell"/> のいずれかで保持する。
/// 注意: enum int 値は kabu API の値とは一致しない。kabu 用には <c>ToKabuCode()</c> を必ず使う。
/// </summary>
public enum Side
{
    /// <summary>買い注文・買い建玉。</summary>
    Buy = 1,
    /// <summary>売り注文・売り建玉。</summary>
    Sell = 2
}

/// <summary>
/// <see cref="Side"/> 拡張メソッド。
/// 反対サイドの取得や表示文字列への変換を提供。
/// </summary>
public static class SideExtensions
{
    /// <summary>反対サイドを返す (返済注文のサイド決定に使用)。</summary>
    public static Side Opposite(this Side side) => side switch
    {
        Side.Buy => Side.Sell,
        Side.Sell => Side.Buy,
        _ => throw new InvalidValueObjectException($"Unknown Side: {side}")
    };

    /// <summary>日本語表示文字列 ("買" / "売")。</summary>
    public static string ToDisplay(this Side side) => side switch
    {
        Side.Buy => "買",
        Side.Sell => "売",
        _ => throw new InvalidValueObjectException($"Unknown Side: {side}")
    };

    /// <summary>kabu API の数値表現 (注文発注時の Side フィールド)。kabu: 1=売 / 2=買。</summary>
    public static int ToKabuCode(this Side side) => side switch
    {
        Side.Buy => 2,
        Side.Sell => 1,
        _ => throw new InvalidValueObjectException($"Unknown Side: {side}")
    };
}
