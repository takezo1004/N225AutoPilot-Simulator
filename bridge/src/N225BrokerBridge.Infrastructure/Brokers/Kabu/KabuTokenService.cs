using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace N225BrokerBridge.Infrastructure.Brokers.Kabu;

/// <summary>
/// kabu ステーション API トークン管理。
/// /token エンドポイントで API パスワードを送り、X-API-KEY 用トークンを取得・キャッシュする。
///
/// 旧 N225OrderBridge の GenerateToken.cs を modern HttpClient + DI + 非同期 + キャッシュに改修。
/// </summary>
public sealed class KabuTokenService
{
    private readonly HttpClient _http;
    private readonly KabuOptions _options;
    private readonly ILogger<KabuTokenService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedToken;
    private DateTime _acquiredAtUtc;

    /// <summary>同一トークンを使い続ける期間。kabu はセッションごとに有効。実用上は当日中再利用可。</summary>
    public TimeSpan CacheLifetime { get; set; } = TimeSpan.FromHours(12);

    public KabuTokenService(
        HttpClient http,
        IOptions<KabuOptions> options,
        ILogger<KabuTokenService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && (DateTime.UtcNow - _acquiredAtUtc) < CacheLifetime)
                return _cachedToken;

            return await AcquireNewTokenAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>強制的にトークンを再取得する (401 が出た時等)。</summary>
    public async Task<string> RefreshAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await AcquireNewTokenAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> AcquireNewTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.ApiPassword))
            throw new InvalidOperationException("KabuOptions.ApiPassword is not configured.");

        var url = $"{_options.BaseUrl.TrimEnd('/')}/token";
        var body = new { APIPassword = _options.ApiPassword };

        using var response = await _http.PostAsJsonAsync(url, body, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                   ?? throw new InvalidOperationException("Empty token response.");

        if (string.IsNullOrEmpty(json.Token))
            throw new InvalidOperationException($"Token acquisition failed: ResultCode={json.ResultCode?.ToString() ?? "(null)"}");

        _cachedToken = json.Token;
        _acquiredAtUtc = DateTime.UtcNow;

        _logger.LogInformation("kabu API トークン取得成功 (キャッシュ期限={Expiry})。", _acquiredAtUtc + CacheLifetime);
        return json.Token;
    }

    private sealed class TokenResponse
    {
        // kabu API は ResultCode を JSON Number (int) で返す (例: 0 = 成功)。
        // 旧定義の string? では JsonException が出るため int? に修正。
        public int? ResultCode { get; set; }
        public string? Token { get; set; }
    }
}
