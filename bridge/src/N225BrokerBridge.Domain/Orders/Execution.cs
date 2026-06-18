using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Orders;

/// <summary>
/// 約定 (Fill)。1 注文が分割約定する場合、複数の Execution が紐付く。
/// 集約 <see cref="Order"/> 内のエンティティとして扱う (Order からのみ生成・更新可)。
///
/// 不変条件:
///   - ExecutionId は非空 (値オブジェクトで保証)
///   - Quantity は正 (1 以上)
///   - Price は非負
/// </summary>
public sealed class Execution : Entity<ExecutionId>
{
    public Quantity Quantity { get; }
    public Price Price { get; }
    public DateTime ExecutedAt { get; }

    public Execution(ExecutionId id, Quantity quantity, Price price, DateTime executedAtUtc)
        : base(id)
    {
        if (!quantity.IsPositive)
            throw new InvalidValueObjectException("Execution quantity must be positive.");
        Quantity = quantity;
        Price = price;
        ExecutedAt = executedAtUtc;
    }
}
