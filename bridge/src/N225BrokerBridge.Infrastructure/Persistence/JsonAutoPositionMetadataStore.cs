using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Application.Sync;

namespace N225BrokerBridge.Infrastructure.Persistence;

/// <summary>
/// 自動取引建玉メタデータの JSON ファイル永続化実装。
///
/// 保存先デフォルト: %LOCALAPPDATA%/N225BrokerBridge/auto-positions.json
/// 形式: List&lt;AutoPositionMetadata&gt; を WriteIndented で出力。
///
/// 旧 N225OrderBridge の PositionAuto + CSV ファイルを置き換え。
/// </summary>
public sealed class JsonAutoPositionMetadataStore : IAutoPositionMetadataStore
{
    private readonly string _filePath;
    private readonly ILogger<JsonAutoPositionMetadataStore> _logger;
    private readonly ConcurrentDictionary<string, AutoPositionMetadata> _entries = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonAutoPositionMetadataStore(string filePath, ILogger<JsonAutoPositionMetadataStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
        Load();
    }

    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "N225BrokerBridge", "auto-positions.json");
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("自動取引建玉メタストア: 保存ファイル未作成 (空で開始) {Path}", _filePath);
                return;
            }
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<AutoPositionMetadata>>(json, JsonOptions);
            if (list is null) return;
            foreach (var e in list)
            {
                if (!string.IsNullOrEmpty(e.ExecutionId))
                    _entries[e.ExecutionId] = e;
            }
            _logger.LogInformation("自動取引建玉メタストア: {Count} 件読み込み完了 ({Path})。",
                _entries.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AutoPositionStore: load failed {Path}", _filePath);
        }
    }

    public Task<IReadOnlyList<AutoPositionMetadata>> LoadAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AutoPositionMetadata> snapshot = _entries.Values.ToList();
        return Task.FromResult(snapshot);
    }

    public async Task UpsertAsync(AutoPositionMetadata metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrEmpty(metadata.ExecutionId))
            throw new ArgumentException("ExecutionId is required", nameof(metadata));
        _entries[metadata.ExecutionId] = metadata;
        await SaveAsync(ct);
    }

    public async Task RemoveAsync(string executionId, CancellationToken ct = default)
    {
        if (_entries.TryRemove(executionId, out _))
            await SaveAsync(ct);
    }

    public async Task SyncToActiveSetAsync(IEnumerable<string> activeExecutionIds, CancellationToken ct = default)
    {
        var active = new HashSet<string>(activeExecutionIds, StringComparer.Ordinal);
        var staleKeys = _entries.Keys.Where(k => !active.Contains(k)).ToList();
        if (staleKeys.Count == 0) return;

        foreach (var k in staleKeys)
            _entries.TryRemove(k, out _);

        _logger.LogInformation("自動取引建玉メタストア: 古いエントリ {Count} 件を削除 (kabu に存在しないもの)。", staleKeys.Count);
        await SaveAsync(ct);
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
                .OrderBy(e => e.OpenedAt)
                .ToList();
            var json = JsonSerializer.Serialize(list, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AutoPositionStore: save failed {Path}", _filePath);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
