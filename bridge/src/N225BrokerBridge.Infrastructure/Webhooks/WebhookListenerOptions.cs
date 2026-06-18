namespace N225BrokerBridge.Infrastructure.Webhooks;

/// <summary>
/// Webhook リスナー設定。appsettings.json から DI 経由でバインドする想定。
/// </summary>
public sealed class WebhookListenerOptions
{
    /// <summary>
    /// リスニングポート。現 N225OrderBridge と並行稼働するため、デフォルトは 8001
    /// (旧ブリッジは 8000)。
    /// </summary>
    public int Port { get; set; } = 8001;

    /// <summary>
    /// バインドアドレス。デフォルトは localhost のみ (外部直接アクセス禁止)。
    /// Cloudflare Tunnel 等経由でのみ受信する前提。
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// 受信パス (例: "/webhook")。
    /// </summary>
    public string Path { get; set; } = "/webhook";

    /// <summary>
    /// パスフレーズ (空なら認証スキップ)。
    /// </summary>
    public string? Passphrase { get; set; }
}
