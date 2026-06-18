using System.Collections.Concurrent;
using N225BrokerBridge.Application.Sync;

namespace N225BrokerBridge.Application.Tests.TestDoubles;

/// <summary>
/// テスト用シンプル OrderMetadata ストア (メモリ内)。
/// </summary>
public sealed class StubOrderMetadataStore : IOrderMetadataStore
{
    private readonly ConcurrentDictionary<string, OrderMetadata> _entries = new();

    public IReadOnlyDictionary<string, OrderMetadata> Entries => _entries;

    public Task<IReadOnlyList<OrderMetadata>> LoadAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OrderMetadata>>(_entries.Values.ToList());

    public Task UpsertAsync(OrderMetadata metadata, CancellationToken ct = default)
    {
        _entries[metadata.BrokerOrderId] = metadata;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string brokerOrderId, CancellationToken ct = default)
    {
        _entries.TryRemove(brokerOrderId, out _);
        return Task.CompletedTask;
    }

    public OrderMetadata? TryGet(string brokerOrderId)
        => _entries.TryGetValue(brokerOrderId, out var e) ? e : null;
}
