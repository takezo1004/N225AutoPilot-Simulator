using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Application.Sync;

namespace N225BrokerBridge.Infrastructure.Persistence;

/// <summary>
/// 注文メタデータの JSON 永続化実装。
///
/// 保存先デフォルト: %LOCALAPPDATA%/N225BrokerBridge/orders-metadata.json
/// 形式: List&lt;OrderMetadata&gt; を WriteIndented で出力。
///
/// 旧 N225OrderBridge の order.csv (OrderManager.ToCsv / CsvRead) を JSON 化したもの。
/// 用途は OrderID をキーにした「自動/手動 / 戦略 / Interval」の保存。
/// </summary>
public sealed class JsonOrderMetadataStore : IOrderMetadataStore
{
    private readonly string _filePath;
    private readonly ILogger<JsonOrderMetadataStore> _logger;
    private readonly ConcurrentDictionary<string, OrderMetadata> _entries = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonOrderMetadataStore(string filePath, ILogger<JsonOrderMetadataStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
        Load();
    }

    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "N225BrokerBridge", "orders-metadata.json");
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("注文メタストア: 保存ファイル未作成 (空で開始) {Path}", _filePath);
                return;
            }
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<OrderMetadata>>(json, JsonOptions);
            if (list is null) return;
            foreach (var e in list)
            {
                if (!string.IsNullOrEmpty(e.BrokerOrderId))
                    _entries[e.BrokerOrderId] = e;
            }
            _logger.LogInformation("注文メタストア: {Count} 件読み込み完了 ({Path})。",
                _entries.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrderMetadataStore: load failed {Path}", _filePath);
        }
    }

    public Task<IReadOnlyList<OrderMetadata>> LoadAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<OrderMetadata> snapshot = _entries.Values.ToList();
        return Task.FromResult(snapshot);
    }

    public async Task UpsertAsync(OrderMetadata metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrEmpty(metadata.BrokerOrderId))
            throw new ArgumentException("BrokerOrderId is required", nameof(metadata));
        _entries[metadata.BrokerOrderId] = metadata;
        await SaveAsync(ct);
    }

    public async Task RemoveAsync(string brokerOrderId, CancellationToken ct = default)
    {
        if (_entries.TryRemove(brokerOrderId, out _))
            await SaveAsync(ct);
    }

    /// <summary>同期的に取得 (UI 1 秒ポーリングの突合用ホットパス)。</summary>
    public OrderMetadata? TryGet(string brokerOrderId)
    {
        return _entries.TryGetValue(brokerOrderId, out var e) ? e : null;
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
                .OrderBy(e => e.CreatedAt)
                .ToList();
            var json = JsonSerializer.Serialize(list, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrderMetadataStore: save failed {Path}", _filePath);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
