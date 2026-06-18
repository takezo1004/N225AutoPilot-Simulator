using System.Collections.Concurrent;
using N225BrokerBridge.Application.Sync;

namespace N225BrokerBridge.Application.Tests.TestDoubles;

/// <summary>テスト用シンプル PendingOrderTracker (メモリ内)。</summary>
public sealed class StubPendingOrderTracker : IPendingOrderTracker
{
    private readonly ConcurrentDictionary<string, byte> _ids = new();

    public void Track(string brokerOrderId)
    {
        if (!string.IsNullOrEmpty(brokerOrderId)) _ids.TryAdd(brokerOrderId, 0);
    }

    public void Untrack(string brokerOrderId)
    {
        if (!string.IsNullOrEmpty(brokerOrderId)) _ids.TryRemove(brokerOrderId, out _);
    }

    public IReadOnlyList<string> GetAll() => _ids.Keys.ToList();
    public bool IsEmpty => _ids.IsEmpty;
}
