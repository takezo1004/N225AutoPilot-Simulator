using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Application.Signals;

namespace N225BrokerBridge.Infrastructure.Strategies;

/// <summary>
/// 戦略レジストリの JSON ファイル実装。
/// 旧 N225OrderBridge の StrategyListCash + Csv ライターの代替 (CSV → JSON 化)。
///
/// 保存先デフォルト: %LOCALAPPDATA%/N225BrokerBridge/strategies.json
/// 起動時に Load() で読み込み、UpsertAsync / RemoveAsync 時に SaveAsync で書き戻し。
/// </summary>
public sealed class JsonStrategyRegistry : IStrategyRegistry
{
    private readonly string _filePath;
    private readonly ILogger<JsonStrategyRegistry> _logger;
    private readonly ConcurrentDictionary<string, StrategyEntry> _entries = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public event EventHandler? Changed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonStrategyRegistry(string filePath, ILogger<JsonStrategyRegistry> logger)
    {
        _filePath = filePath;
        _logger = logger;
        Load();
    }

    private static string Key(string alertName, int interval) => $"{alertName}|{interval}";

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("戦略レジストリ: 保存ファイル未作成 (空で開始) {Path}", _filePath);
                return;
            }
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<StrategyEntry>>(json, JsonOptions);
            if (list is null) return;
            foreach (var e in list)
            {
                _entries[Key(e.AlertName, e.Interval)] = e;
            }
            _logger.LogInformation("戦略レジストリ: {Count} 件読み込み完了 ({Path})。",
                _entries.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StrategyRegistry: load failed {Path}", _filePath);
        }
    }

    public IReadOnlyList<StrategyEntry> GetAll() => _entries.Values.ToList();

    public bool IsEnabled(string alertName, int interval)
    {
        return _entries.TryGetValue(Key(alertName, interval), out var e) && e.IsEnabled;
    }

    public async Task UpsertAsync(StrategyEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries[Key(entry.AlertName, entry.Interval)] = entry;
        await SaveAsync(ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task MarkSignalReceivedAsync(
        string alertName, int interval, DateTime atUtc, CancellationToken ct = default)
    {
        if (_entries.TryGetValue(Key(alertName, interval), out var e))
        {
            e.LastSignalAt = atUtc;
            await SaveAsync(ct);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        // 登録されていない戦略はスキップ (旧ブリッジ準拠: 未登録は受信のみ・実行されない)
    }

    public async Task UpdateLastSignalAsync(
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
            await SaveAsync(ct);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task RemoveAsync(string alertName, int interval, CancellationToken ct = default)
    {
        if (_entries.TryRemove(Key(alertName, interval), out _))
        {
            await SaveAsync(ct);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        await _saveLock.WaitAsync(ct);
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var list = _entries.Values.OrderBy(e => e.AlertName).ThenBy(e => e.Interval).ToList();
            var json = JsonSerializer.Serialize(list, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StrategyRegistry: save failed {Path}", _filePath);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// デフォルト保存先パス (%LOCALAPPDATA%/N225BrokerBridge/strategies.json)。
    /// </summary>
    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "N225BrokerBridge", "strategies.json");
    }
}
