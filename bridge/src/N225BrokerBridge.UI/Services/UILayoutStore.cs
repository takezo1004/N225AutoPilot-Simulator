using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace N225BrokerBridge.UI.Services;

/// <summary>
/// UI レイアウト設定 (<see cref="UILayoutSettings"/>) の永続化を担う。
/// 保存先: %LOCALAPPDATA%\N225BrokerBridge\ui-layout.json (DPAPI 暗号化なし、秘密情報を含まないため)。
/// 読み込み失敗時は null を返し、呼び出し側でデフォルト値を使う方針。
/// </summary>
public sealed class UILayoutStore
{
    private readonly string _filePath;
    private readonly ILogger<UILayoutStore>? _logger;

    public UILayoutStore(ILogger<UILayoutStore>? logger = null)
    {
        _logger = logger;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "N225BrokerBridge");
        _filePath = Path.Combine(dir, "ui-layout.json");
    }

    /// <summary>
    /// 保存済みの UI レイアウト設定を読み込む。
    /// ファイルが存在しない / パース失敗 / 異常値の場合は null。
    /// </summary>
    public UILayoutSettings? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger?.LogInformation("UI レイアウトファイルが未生成 (初回起動か削除済み): {Path}", _filePath);
                return null;
            }

            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<UILayoutSettings>(json);

            if (settings is null)
            {
                _logger?.LogWarning("UI レイアウト JSON のデシリアライズ結果が null: {Path}", _filePath);
                return null;
            }

            _logger?.LogInformation("UI レイアウト読み込み完了: {Path}", _filePath);
            return settings;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "UI レイアウト読み込み失敗、デフォルトに戻します: {Path}", _filePath);
            return null;
        }
    }

    /// <summary>
    /// 現在の UI レイアウト設定を保存する。
    /// 例外は握りつぶしてログのみ (シャットダウン中の例外でアプリを落とさない)。
    /// </summary>
    public void Save(UILayoutSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_filePath, json);
            // 5 秒毎の自動保存でログがうるさくなるため Information ログは出さない
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "UI レイアウト保存失敗 (アプリ動作には影響なし): {Path}", _filePath);
        }
    }
}
