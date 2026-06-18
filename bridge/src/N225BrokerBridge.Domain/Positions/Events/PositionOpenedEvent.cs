using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Positions.Events;

/// <summary>
/// 新規約定により建玉が発生した。
/// </summary>
public sealed record PositionOpenedEvent(
    ExecutionId PositionId,
    BrokerCode BrokerCode,
    StrategyName Strategy,
    Side Side,
    Quantity OpenedQuantity,
    Price EntryPrice,
    DateTime OccurredAt) : IDomainEvent;
