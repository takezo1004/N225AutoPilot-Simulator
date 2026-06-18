using System.Collections.Concurrent;
using N225BrokerBridge.Application.Sync;

namespace N225BrokerBridge.Application.Tests.TestDoubles;

/// <summary>
/// テスト用シンプル ClosedTrade ストア (メモリ内)。ExitExecutionId をキーに upsert (冪等)。
/// </summary>
public sealed class StubClosedTradeStore : IClosedTradeStore
{
    private readonly ConcurrentDictionary<string, ClosedTrade> _entries = new();

    /// <summary>記録された決済 (ClosedAt 昇順)。</summary>
    public IReadOnlyList<ClosedTrade> Trades => _entries.Values.OrderBy(e => e.ClosedAt).ToList();

    public Task AppendAsync(ClosedTrade trade, CancellationToken ct = default)
    {
        _entries[trade.ExitExecutionId] = trade;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ClosedTrade>> LoadAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ClosedTrade>>(_entries.Values.ToList());
}
