using N225BrokerBridge.Domain.Common;

namespace N225BrokerBridge.Domain.ValueObjects;

/// <summary>
/// 約定 ID。ブローカーが各約定 (fill) ごとに採番した文字列をラップする。
/// kabu API での HoldID (建玉識別子) と同じ概念。
///
/// 注意: 新規約定の ExecutionID は建玉識別子として使えるが、返済約定の ExecutionID は
/// 「約定自体の新規 ID」であり、返済対象建玉の元 ID とは別。建玉の元 ID は呼び出し元が
/// 別途解決して保持する必要がある (現 N225OrderBridge の 2026-04-30 修正参照)。
/// </summary>
public sealed record ExecutionId
{
    /// <summary>約定 ID 文字列 (ブローカー採番)。kabu では HoldID と同じ。</summary>
    public string Value { get; }

    /// <summary>
    /// 約定 ID を生成する。
    /// </summary>
    /// <param name="value">約定 ID 文字列。空文字不可。</param>
    /// <exception cref="InvalidValueObjectException">value が空または空白のみの場合。</exception>
    public ExecutionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidValueObjectException("ExecutionId must not be empty.");
        Value = value;
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}
