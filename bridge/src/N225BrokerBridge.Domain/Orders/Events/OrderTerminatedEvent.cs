using N225BrokerBridge.Domain.Common;

namespace N225BrokerBridge.Domain.Orders.Events;

/// <summary>
/// 注文が異常終了 (Cancelled / Expired / Rejected) した。
/// </summary>
public sealed record OrderTerminatedEvent(
    Guid AggregateId,
    OrderState TerminalState,
    string? Reason,
    DateTime OccurredAt) : IDomainEvent;
