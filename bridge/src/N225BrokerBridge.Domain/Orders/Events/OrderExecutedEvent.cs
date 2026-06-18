using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Orders.Events;

/// <summary>
/// 注文に新しい約定 (Fill) が追加された。
/// 部分約定なら IsFullyFilled = false、最終約定で true。
/// </summary>
public sealed record OrderExecutedEvent(
    Guid AggregateId,
    ExecutionId ExecutionId,
    Quantity ExecutedQuantity,
    Price ExecutedPrice,
    bool IsFullyFilled,
    DateTime OccurredAt) : IDomainEvent;
