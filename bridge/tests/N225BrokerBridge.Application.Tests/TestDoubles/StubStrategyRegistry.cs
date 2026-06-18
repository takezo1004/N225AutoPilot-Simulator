using N225BrokerBridge.Application.Signals;

namespace N225BrokerBridge.Application.Tests.TestDoubles;

/// <summary>
/// テスト用シンプル戦略レジストリ。
/// デフォルトは全戦略 IsEnabled=true (テストで明示的に disable しない限り)。
/// </summary>
public sealed class StubStrategyRegistry : IStrategyRegistry
{
    private readonly Dictionary<string, StrategyEntry> _entries = new();

    public bool DefaultEnabled { get; set; } = true;

    public event EventHandler? Changed;

    private static string Key(string alertName, int interval) => $"{alertName}|{interval}";

    public IReadOnlyList<StrategyEntry> GetAll() => _entries.Values.ToList();

    public bool IsEnabled(string alertName, int interval)
    {
        return _entries.TryGetValue(Key(alertName, interval), out var e)
            ? e.IsEnabled
            : DefaultEnabled;
    }

    public Task UpsertAsync(StrategyEntry entry, CancellationToken ct = default)
    {
        _entries[Key(entry.AlertName, entry.Interval)] = entry;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task MarkSignalReceivedAsync(
        string alertName, int interval, DateTime atUtc, CancellationToken ct = default)
    {
        if (_entries.TryGetValue(Key(alertName, interval), out var e))
        {
            e.LastSignalAt = atUtc;
        }
        return Task.CompletedTask;
    }

    public Task UpdateLastSignalAsync(
        string alertName, int interval, DateTime atUtc,
        string tradeType, string side, decimal price,
        CancellationToken ct = default)
    {
        if (_entries.TryGetValue(Key(alertName, interval), out var e))
        {
            e.LastSignalAt = atUtc;
            e.LastTradeType = tradeType;
            e.LastSide = side;
            e.LastPrice = price;
        }
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string alertName, int interval, CancellationToken ct = default)
    {
        _entries.Remove(Key(alertName, interval));
        return Task.CompletedTask;
    }
}
