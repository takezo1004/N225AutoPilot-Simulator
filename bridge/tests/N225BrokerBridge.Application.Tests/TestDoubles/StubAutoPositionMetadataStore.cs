using System.Collections.Concurrent;
using N225BrokerBridge.Application.Sync;

namespace N225BrokerBridge.Application.Tests.TestDoubles;

/// <summary>
/// テスト用シンプル AutoPositionMetadata ストア (メモリ内)。
/// </summary>
public sealed class StubAutoPositionMetadataStore : IAutoPositionMetadataStore
{
    private readonly ConcurrentDictionary<string, AutoPositionMetadata> _entries = new();

    public IReadOnlyDictionary<string, AutoPositionMetadata> Entries => _entries;

    public Task<IReadOnlyList<AutoPositionMetadata>> LoadAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AutoPositionMetadata>>(_entries.Values.ToList());

    public Task UpsertAsync(AutoPositionMetadata metadata, CancellationToken ct = default)
    {
        _entries[metadata.ExecutionId] = metadata;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string executionId, CancellationToken ct = default)
    {
        _entries.TryRemove(executionId, out _);
        return Task.CompletedTask;
    }

    public Task SyncToActiveSetAsync(IEnumerable<string> activeExecutionIds, CancellationToken ct = default)
    {
        var active = new HashSet<string>(activeExecutionIds, StringComparer.Ordinal);
        foreach (var key in _entries.Keys.Where(k => !active.Contains(k)).ToList())
            _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
