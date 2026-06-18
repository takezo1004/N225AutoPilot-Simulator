namespace N225BrokerBridge.Domain.Common;

/// <summary>
/// エンティティ基底クラス。
/// エンティティは「ID で同一性を持つ」オブジェクト。属性が変わっても ID が同じなら同一物。
/// (値オブジェクトは record で表現するため、本基底は使わない)
/// </summary>
/// <typeparam name="TId">識別子型 (例: Guid, OrderId 等)</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; }

    protected Entity(TId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
    }

    public bool Equals(Entity<TId>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? a, Entity<TId>? b) => Equals(a, b);
    public static bool operator !=(Entity<TId>? a, Entity<TId>? b) => !Equals(a, b);
}
