using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Application.Sync;

namespace N225BrokerBridge.Infrastructure.Persistence;

/// <summary>
/// <see cref="IPendingOrderTracker"/> のメモリ内実装。
///
/// 旧 N225OrderBridge の <c>OrderInquiryList</c> 相当。
/// アプリ再起動で消えるが、起動時に kabu /orders 全件取得 + 未終端を再 Track する
/// reconciliation を別途行えば永続化不要。
/// </summary>
public sealed class InMemoryPendingOrderTracker : IPendingOrderTracker
{
    private readonly ConcurrentDictionary<string, byte> _ids = new();
    private readonly ILogger<InMemoryPendingOrderTracker> _logger;

    public InMemoryPendingOrderTracker(ILogger<InMemoryPendingOrderTracker> logger)
    {
        _logger = logger;
    }

    public void Track(string brokerOrderId)
    {
        if (string.IsNullOrEmpty(brokerOrderId)) return;
        if (_ids.TryAdd(brokerOrderId, 0))
        {
            _logger.LogInformation(
                "約定待ちリストに追加: 注文ID={OrderId} (約定待ち合計 {Count} 件)",
                brokerOrderId, _ids.Count);
        }
    }

    public void Untrack(string brokerOrderId)
    {
        if (string.IsNullOrEmpty(brokerOrderId)) return;
        if (_ids.TryRemove(brokerOrderId, out _))
        {
            _logger.LogInformation(
                "約定待ちリストから削除: 注文ID={OrderId} (約定待ち合計 {Count} 件)",
                brokerOrderId, _ids.Count);
        }
    }

    public IReadOnlyList<string> GetAll() => _ids.Keys.ToList();

    public bool IsEmpty => _ids.IsEmpty;
}
