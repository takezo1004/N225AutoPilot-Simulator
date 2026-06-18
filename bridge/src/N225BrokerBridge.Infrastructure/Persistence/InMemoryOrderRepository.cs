using System.Collections.Concurrent;
using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Infrastructure.Persistence;

/// <summary>
/// メモリ内 Order リポジトリ。初期段階 / 単体テストで利用。
/// 将来 SQLite/EF Core 等の永続実装に差し替え可能。
/// <see cref="IOrderChangeNotifier"/> を同居実装し UI 等の購読者へ変更を通知する。
/// </summary>
public sealed class InMemoryOrderRepository : IOrderRepository, IOrderChangeNotifier
{
    private readonly ConcurrentDictionary<Guid, Order> _byId = new();

    public event EventHandler<OrderChangedEventArgs>? Changed;

    public Task AddAsync(Order order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (!_byId.TryAdd(order.Id, order))
            throw new InvalidOperationException($"Order {order.Id} already exists.");
        Changed?.Invoke(this, OrderChangedEventArgs.Added(order));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Order order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        // 同一参照で保持する単純実装 (集約は同インスタンスをそのまま使う)
        _byId[order.Id] = order;
        Changed?.Invoke(this, OrderChangedEventArgs.Updated(order));
        return Task.CompletedTask;
    }

    public Task<Order?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        return Task.FromResult(_byId.TryGetValue(id, out var order) ? order : null);
    }

    public Task<Order?> FindByBrokerOrderIdAsync(
        BrokerCode brokerCode, OrderId brokerOrderId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(brokerCode);
        ArgumentNullException.ThrowIfNull(brokerOrderId);
        var match = _byId.Values.FirstOrDefault(o =>
            o.BrokerCode == brokerCode && o.BrokerOrderId == brokerOrderId);
        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<Order>> FindActiveAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Order> active = _byId.Values.Where(o => !o.IsTerminal).ToList();
        return Task.FromResult(active);
    }
}
