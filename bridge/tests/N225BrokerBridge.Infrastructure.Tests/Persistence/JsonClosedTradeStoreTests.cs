using Microsoft.Extensions.Logging.Abstractions;
using N225BrokerBridge.Application.Sync;
using N225BrokerBridge.Infrastructure.Persistence;
using Xunit;

namespace N225BrokerBridge.Infrastructure.Tests.Persistence;

/// <summary>
/// JsonClosedTradeStore の永続化テスト。詳細仕様: docs/position-history-spec.md §6 (PH-U5/U6)。
/// </summary>
public class JsonClosedTradeStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"position-history-test-{Guid.NewGuid():N}.json");

    private JsonClosedTradeStore NewStore() =>
        new(_path, NullLogger<JsonClosedTradeStore>.Instance);

    private static ClosedTrade Sample(string exitId, decimal pnl = 1000m) => new()
    {
        EntryExecutionId = "E1",
        ExitExecutionId = exitId,
        BrokerCode = "kabu",
        Strategy = "V7-7",
        Interval = 5,
        TradeMode = "Auto",
        SymbolCode = "161060023",
        Side = "Buy",
        EntryPrice = 38000m,
        ExitPrice = 38100m,
        Quantity = 1,
        ProfitMultiplier = 10,
        RealizedPnl = pnl,
        OpenedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        ClosedAt = new DateTime(2026, 6, 1, 1, 0, 0, DateTimeKind.Utc),
    };

    // ── PH-U5: AppendAsync 冪等 (同 ExitExecutionId 二重 → 1 件のまま) ──

    [Fact]
    public async Task Append_SameExitExecutionId_IsIdempotent()
    {
        var store = NewStore();
        await store.AppendAsync(Sample("EX-1", 1000m));
        await store.AppendAsync(Sample("EX-1", 9999m));   // 同キーで上書き

        var all = await store.LoadAllAsync();
        var trade = Assert.Single(all);
        Assert.Equal(9999m, trade.RealizedPnl);           // 後勝ち upsert
    }

    // ── PH-U6: ストア再読込で保存内容が一致 ───────────────────────

    [Fact]
    public async Task Reload_PersistsAcrossInstances()
    {
        var store1 = NewStore();
        await store1.AppendAsync(Sample("EX-1"));
        await store1.AppendAsync(Sample("EX-2"));

        var store2 = NewStore();   // 同一ファイルを読み直す
        var all = await store2.LoadAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, t => t.ExitExecutionId == "EX-1");
        Assert.Contains(all, t => t.ExitExecutionId == "EX-2");
        Assert.Equal(10, all.First().ProfitMultiplier);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
