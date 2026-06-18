namespace N225BrokerBridge.Application.Signals;

/// <summary>
/// シグナル受信時のパスフレーズ認証。
///
/// 現 N225OrderBridge と同じ方針:
/// 設定パスフレーズが空なら認証スキップ (後方互換)、設定されていれば必ず一致を要求する。
/// </summary>
public interface ISignalAuthenticator
{
    bool Authenticate(string? receivedPassphrase);
}

public sealed class ConfiguredSignalAuthenticator : ISignalAuthenticator
{
    private readonly string? _configured;

    public ConfiguredSignalAuthenticator(string? configuredPassphrase)
    {
        _configured = configuredPassphrase;
    }

    public bool Authenticate(string? receivedPassphrase)
    {
        if (string.IsNullOrEmpty(_configured))
            return true;  // パスフレーズ未設定なら認証スキップ
        return string.Equals(receivedPassphrase ?? string.Empty, _configured, StringComparison.Ordinal);
    }
}
