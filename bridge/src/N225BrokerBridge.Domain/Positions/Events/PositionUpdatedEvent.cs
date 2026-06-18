using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Positions.Events;

/// <summary>
/// 建玉の状態 (残数量・拘束数量) が変化した。
/// </summary>
public sealed record PositionUpdatedEvent(
    ExecutionId PositionId,
    Quantity LeaveQuantity,
    Quantity HoldQuantity,
    DateTime OccurredAt) : IDomainEvent;
