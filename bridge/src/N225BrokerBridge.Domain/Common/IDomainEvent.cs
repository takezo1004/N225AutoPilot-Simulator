namespace N225BrokerBridge.Domain.Common;

/// <summary>
/// ドメインイベントを表すマーカーインターフェース。
/// 集約ルートから発火され、永続化後に他コンテキスト・UI へ伝播する。
/// </summary>
public interface IDomainEvent
{
    /// <summary>イベント発生時刻 (UTC)。</summary>
    DateTime OccurredAt { get; }
}
