using System.Collections.Concurrent;
using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Domain.Positions;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Infrastructure.Persistence;

/// <summary>
/// メモリ内 Position リポジトリ。初期段階 / 単体テストで利用。
/// <see cref="IPositionChangeNotifier"/> も同居実装し、UI 等の購読者へ変更を通知する。
/// </summary>
public sealed class InMemoryPositionRepository : IPositionRepository, IPositionChangeNotifier
{
    private readonly ConcurrentDictionary<ExecutionId, Position> _byId = new();

    public event EventHandler<PositionChangedEventArgs>? Changed;

    public Task AddAsync(Position position, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (!_byId.TryAdd(position.Id, position))
            throw new InvalidOperationException($"Position {position.Id} already exists.");
        Changed?.Invoke(this, PositionChangedEventArgs.Added(position));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Position position, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        _byId[position.Id] = position;
        Changed?.Invoke(this, PositionChangedEventArgs.Updated(position));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(ExecutionId id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_byId.TryRemove(id, out _))
            Changed?.Invoke(this, PositionChangedEventArgs.Removed(id));
        return Task.CompletedTask;
    }

    public Task<Position?> FindByIdAsync(ExecutionId id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Task.FromResult(_byId.TryGetValue(id, out var pos) ? pos : null);
    }

    public Task<IReadOnlyList<Position>> FindMatchingForCloseAsync(
        BrokerCode brokerCode,
        StrategyName strategy,
        int interval,
        TradeMode tradeMode,
        Side originalSide,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(brokerCode);
        ArgumentNullException.ThrowIfNull(strategy);

        IReadOnlyList<Position> matched = _byId.Values
            .Where(p => !p.IsClosed
                && p.BrokerCode == brokerCode
                && p.Strategy == strategy
                && p.Interval == interval
                && p.TradeMode == tradeMode
                && p.Side == originalSide)
            .ToList();
        return Task.FromResult(matched);
    }

    public Task<IReadOnlyList<Position>> FindActiveAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Position> active = _byId.Values.Where(p => !p.IsClosed).ToList();
        return Task.FromResult(active);
    }
}
