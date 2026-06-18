using N225BrokerBridge.Domain.Common;

namespace N225BrokerBridge.Domain.ValueObjects;

/// <summary>
/// ブローカー識別コード。マルチブローカー対応で、同じ ExecutionId 等が異なる
/// ブローカーで衝突しないようにするため、ドメイン内では常に
/// (BrokerCode, OrderId / ExecutionId) のペアで識別する。
/// </summary>
public sealed record BrokerCode
{
    /// <summary>ブローカー識別文字列 (例: "kabu", "rakuten")。</summary>
    public string Value { get; }

    private BrokerCode(string value) => Value = value;

    /// <summary>
    /// 任意のブローカーコードを生成する (拡張用)。
    /// 標準のブローカーは <see cref="Kabu"/> / <see cref="Rakuten"/> を使う。
    /// </summary>
    /// <param name="value">ブローカー識別文字列。空文字不可。</param>
    /// <returns>生成された <see cref="BrokerCode"/>。</returns>
    /// <exception cref="InvalidValueObjectException">value が空または空白のみの場合。</exception>
    public static BrokerCode Of(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidValueObjectException("BrokerCode must not be empty.");
        return new BrokerCode(value);
    }

    /// <summary>kabu (au カブコム証券)。</summary>
    public static readonly BrokerCode Kabu = new("kabu");

    /// <summary>楽天証券 (RSS 経由)。</summary>
    public static readonly BrokerCode Rakuten = new("rakuten");

    /// <summary>シミュレータモード (--simulator) の Mock ブローカー。詳細は docs/simulator-mode.md。</summary>
    public static readonly BrokerCode Mock = new("mock");

    /// <inheritdoc/>
    public override string ToString() => Value;
}
