using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace N225BrokerBridge.UI.Services;

/// <summary>
/// ローカル機密設定 (passphrase / kabu credentials) のストア。
///
/// 保存先: %LOCALAPPDATA%/N225BrokerBridge/appsettings.Local.json
/// 暗号化: Windows DPAPI (DataProtectionScope.CurrentUser)
///   - 暗号化値は "enc:<base64>" プレフィックス付きで保存
///   - 同一 Windows ユーザーアカウント + 同一マシンでのみ復号可
///   - 旧 plaintext 値はそのまま読み込み可 → 次回保存時に自動で暗号化される
///
/// Windows 11 互換性: Windows 10 と同じ DPAPI API。特別な設定不要。
/// </summary>
public sealed class LocalSettingsStore
{
    private readonly string _filePath;
    private const string EncPrefix = "enc:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// デフォルトコンストラクタ。保存先は %LOCALAPPDATA%/N225BrokerBridge/appsettings.Local.json
    /// </summary>
    public LocalSettingsStore() : this(DefaultPath()) { }

    public LocalSettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "N225BrokerBridge", "appsettings.Local.json");
    }

    public string FilePath => _filePath;
    public bool Exists => File.Exists(_filePath);

    public LocalSettingsValues Load()
    {
        if (!File.Exists(_filePath))
            return new LocalSettingsValues();

        try
        {
            var json = File.ReadAllText(_filePath);
            var node = JsonNode.Parse(json) as JsonObject;
            return new LocalSettingsValues
            {
                WebhookPassphrase = Decrypt(node?["Webhook"]?["Passphrase"]?.GetValue<string>()),
                WebhookPort = node?["Webhook"]?["Port"]?.GetValue<int?>(),
                KabuEnvironment = node?["Kabu"]?["Environment"]?.GetValue<string>(),
                KabuApiPassword = Decrypt(node?["Kabu"]?["ApiPassword"]?.GetValue<string>()),
                KabuApiPasswordTest = Decrypt(node?["Kabu"]?["ApiPasswordTest"]?.GetValue<string>()),
                KabuOrderPassword = Decrypt(node?["Kabu"]?["OrderPassword"]?.GetValue<string>()),
                RequireConfirmBeforeOrder =
                    node?["Behavior"]?["RequireConfirmBeforeOrder"]?.GetValue<bool>() ?? true
            };
        }
        catch
        {
            return new LocalSettingsValues();
        }
    }

    public void Save(LocalSettingsValues values)
    {
        // 既存ファイルを部分マージ
        JsonObject root = new();
        if (File.Exists(_filePath))
        {
            try
            {
                var existing = File.ReadAllText(_filePath);
                if (JsonNode.Parse(existing) is JsonObject existingObj)
                    root = existingObj;
            }
            catch { /* 壊れたファイルは新規扱い */ }
        }

        var webhook = root["Webhook"] as JsonObject ?? new JsonObject();
        if (values.WebhookPassphrase is not null)
            webhook["Passphrase"] = Encrypt(values.WebhookPassphrase);
        if (values.WebhookPort is int port)
            webhook["Port"] = port;
        root["Webhook"] = webhook;

        var kabu = root["Kabu"] as JsonObject ?? new JsonObject();
        if (values.KabuEnvironment is not null)
            kabu["Environment"] = values.KabuEnvironment;
        if (values.KabuApiPassword is not null)
            kabu["ApiPassword"] = Encrypt(values.KabuApiPassword);
        if (values.KabuApiPasswordTest is not null)
            kabu["ApiPasswordTest"] = Encrypt(values.KabuApiPasswordTest);
        if (values.KabuOrderPassword is not null)
            kabu["OrderPassword"] = Encrypt(values.KabuOrderPassword);
        root["Kabu"] = kabu;

        // 動作設定 (機密ではないので平文保存)。MainViewModel が ExitPosition 時に
        // 毎回 Load を呼ぶ前提なので、保存後の再起動不要で即反映される。
        var behavior = root["Behavior"] as JsonObject ?? new JsonObject();
        behavior["RequireConfirmBeforeOrder"] = values.RequireConfirmBeforeOrder;
        root["Behavior"] = behavior;

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(_filePath, root.ToJsonString(JsonOptions));
    }

    // ── DPAPI 暗号化/復号 ─────────────────────────────────────

    private static string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return plaintext;
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return EncPrefix + Convert.ToBase64String(encrypted);
    }

    private static string? Decrypt(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return stored;
        if (!stored.StartsWith(EncPrefix, StringComparison.Ordinal))
            return stored;   // 旧 plaintext 値は次回保存時に再暗号化される
        try
        {
            var base64 = stored.Substring(EncPrefix.Length);
            var encrypted = Convert.FromBase64String(base64);
            var decrypted = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            // 別ユーザー/別マシンで作られた暗号化データ → 復号不可
            return null;
        }
    }
}

public sealed class LocalSettingsValues
{
    public string? WebhookPassphrase { get; set; }
    public int? WebhookPort { get; set; }

    /// <summary>kabu 接続環境。"Production" (デフォルト) または "Verification"。</summary>
    public string? KabuEnvironment { get; set; }

    /// <summary>kabu ステーション 本番用 API パスワード (port 18080)。</summary>
    public string? KabuApiPassword { get; set; }

    /// <summary>kabu ステーション 検証用 API パスワード (port 18081、モック応答・実発注なし)。</summary>
    public string? KabuApiPasswordTest { get; set; }

    /// <summary>取引暗証番号 (本番/検証共通)。</summary>
    public string? KabuOrderPassword { get; set; }

    /// <summary>
    /// 手動操作 (UI の買/売/返済/キャンセルボタン) 前に確認ダイアログを表示するか。
    /// デフォルトは true (確認あり)。ファイル未保存時もデフォルト true で扱われる。
    ///
    /// ※ TradingView Webhook 経由の自動発注 (SignalHandler → UseCase 直呼び) には適用されない。
    ///   このフラグの参照は MainViewModel の手動操作メソッド内のみで、SignalHandler の
    ///   自動発注経路は MainViewModel を通らないため、自動側は常にダイアログなしで実行される。
    /// </summary>
    public bool RequireConfirmBeforeOrder { get; set; } = true;
}

public static class KabuEnvironments
{
    public const string Production = "Production";
    public const string Verification = "Verification";

    public const int ProductionPort = 18080;
    public const int VerificationPort = 18081;

    public static string BaseUrlFor(string env) =>
        $"http://localhost:{PortFor(env)}/kabusapi";

    public static string WebSocketUrlFor(string env) =>
        $"ws://localhost:{PortFor(env)}/kabusapi/websocket";

    public static int PortFor(string env) =>
        env == Verification ? VerificationPort : ProductionPort;
}
