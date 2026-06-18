using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Positions.Events;

/// <summary>
/// 建玉が全量返済され消滅した (LeaveQuantity が 0 になった)。
/// </summary>
public sealed record PositionClosedEvent(
    ExecutionId PositionId,
    DateTime OccurredAt) : IDomainEvent;
