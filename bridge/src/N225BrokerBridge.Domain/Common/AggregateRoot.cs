namespace N225BrokerBridge.Domain.Common;

/// <summary>
/// 集約ルート基底クラス。
/// 集約 (Aggregate) の境界の入口となるエンティティで、ドメインイベントの発火を担う。
/// 発火されたイベントは永続化後にディスパッチされる前提。
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = new();

    protected AggregateRoot(TId id) : base(id) { }

    /// <summary>未配信のドメインイベント一覧 (読み取り専用ビュー)。</summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>ドメインイベントを発火 (内部キューに追加)。</summary>
    protected void RaiseEvent(IDomainEvent @event) => _domainEvents.Add(@event);

    /// <summary>ディスパッチ完了後にイベントキューをクリア。</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
