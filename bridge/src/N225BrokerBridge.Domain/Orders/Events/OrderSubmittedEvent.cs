using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Orders.Events;

/// <summary>
/// 注文がブローカーへ送信され、受付 (OrderId 採番) された。
/// </summary>
public sealed record OrderSubmittedEvent(
    Guid AggregateId,
    BrokerCode BrokerCode,
    OrderId BrokerOrderId,
    DateTime OccurredAt) : IDomainEvent;
