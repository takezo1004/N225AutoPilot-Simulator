using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Application.Sync;

namespace N225BrokerBridge.Infrastructure.Persistence;

/// <summary>
/// ポジション履歴 (決済済み実現損益) の JSON 永続化実装。
///
/// 保存先デフォルト: %LOCALAPPDATA%/N225BrokerBridge/position-history.json
/// 形式: List&lt;ClosedTrade&gt; を WriteIndented + camelCase で出力。
///
/// 追記型: <see cref="AppendAsync"/> は ExitExecutionId をキーに upsert (冪等)。
/// 削除メソッドは持たない (履歴恒久保持)。旧 kabu / 旧 N225OrderBridge の
/// 「ポジション」CSV に相当。詳細: docs/position-history-spec.md §4-4。
/// </summary>
public sealed class JsonClosedTradeStore : IClosedTradeStore
{
    private readonly string _filePath;
    private readonly ILogger<JsonClosedTradeStore> _logger;
    private readonly ConcurrentDictionary<string, ClosedTrade> _entries = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonClosedTradeStore(string filePath, ILogger<JsonClosedTradeStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
        Load();
    }

    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "N225BrokerBridge", "position-history.json");
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("ポジション履歴ストア: 保存ファイル未作成 (空で開始) {Path}", _filePath);
                return;
            }
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<ClosedTrade>>(json, JsonOptions);
            if (list is null) return;
            foreach (var e in list)
            {
                if (!string.IsNullOrEmpty(e.ExitExecutionId))
                    _entries[e.ExitExecutionId] = e;
            }
            _logger.LogInformation("ポジション履歴ストア: {Count} 件読み込み完了 ({Path})。",
                _entries.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClosedTradeStore: load failed {Path}", _filePath);
        }
    }

    public async Task AppendAsync(ClosedTrade trade, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trade);
        if (string.IsNullOrEmpty(trade.ExitExecutionId))
            throw new ArgumentException("ExitExecutionId is required", nameof(trade));
        _entries[trade.ExitExecutionId] = trade;
        await SaveAsync(ct);
    }

    public Task<IReadOnlyList<ClosedTrade>> LoadAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ClosedTrade> snapshot = _entries.Values.ToList();
        return Task.FromResult(snapshot);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        await _saveLock.WaitAsync(ct);
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var list = _entries.Values
                .OrderBy(e => e.ClosedAt)
                .ToList();
            var json = JsonSerializer.Serialize(list, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClosedTradeStore: save failed {Path}", _filePath);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
