using N225BrokerBridge.Domain.Common;

namespace N225BrokerBridge.Domain.ValueObjects;

/// <summary>
/// 銘柄コードを表す値オブジェクト。
/// ブローカーごとにコード体系が違う (kabu: "167060019"、TV: "OSE:NK225M1!" など) ため、
/// 本値オブジェクトは「文字列」のみを保証し、変換は各ブローカーアダプタが担当する。
/// </summary>
public sealed record SymbolCode
{
    /// <summary>銘柄コード文字列 (ブローカー固有形式)。</summary>
    public string Value { get; }

    /// <summary>
    /// 銘柄コードを生成する。
    /// </summary>
    /// <param name="value">空文字・空白不可。</param>
    /// <exception cref="InvalidValueObjectException">value が空または空白のみの場合。</exception>
    public SymbolCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidValueObjectException("SymbolCode must not be empty.");
        Value = value;
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}
