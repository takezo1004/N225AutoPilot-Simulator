using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using N225BrokerBridge.Infrastructure.Brokers.Kabu.Dto;

namespace N225BrokerBridge.Infrastructure.Brokers.Kabu;

/// <summary>
/// kabu ステーション REST API の HttpClient ラッパ。
/// X-API-KEY 認証、HTTP メソッド・ペイロード組立、JSON シリアライズを集約。
///
/// 旧 N225OrderBridge の SendorderFutureEntryApi / SendorderFutureExitApi 等
/// (各エンドポイント別の静的クラス) を 1 クライアントに統合。
/// </summary>
public sealed class KabuApiClient
{
    private readonly HttpClient _http;
    private readonly KabuTokenService _tokenService;
    private readonly KabuOptions _options;
    private readonly ILogger<KabuApiClient> _logger;

    public KabuApiClient(
        HttpClient http,
        KabuTokenService tokenService,
        IOptions<KabuOptions> options,
        ILogger<KabuApiClient> logger)
    {
        _http = http;
        _tokenService = tokenService;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>新規 / 返済共通の /sendorder/future。
    /// 401 (Unauthorized = APIキー不一致) を受けた場合は token を強制 refresh して 1 回だけ自動リトライ。
    /// </summary>
    public async Task<KabuSendOrderResponse> SendOrderAsync(
        KabuSendOrderRequest request, CancellationToken ct = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/sendorder/future";
        _logger.LogInformation("/sendorder/future 発射 (1 回目)...");
        var resp = await SendOrderInternalAsync(url, request, useRefresh: false, ct);
        _logger.LogInformation("/sendorder/future 1 回目応答: ステータス={Status} body.Code={Code} body.OrderId={OrderId} body.Message={Message}",
            (int)resp.statusCode, resp.body?.Code, resp.body?.OrderId, resp.body?.Message);

        if (resp.statusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("/sendorder で 401。token を refresh して 1 回だけ再試行します。");
            resp = await SendOrderInternalAsync(url, request, useRefresh: true, ct);
            _logger.LogInformation("/sendorder/future 2 回目応答: ステータス={Status} body.Code={Code} body.OrderId={OrderId}",
                (int)resp.statusCode, resp.body?.Code, resp.body?.OrderId);
        }
        return resp.body ?? new KabuSendOrderResponse { Code = -1, Message = "Empty response" };
    }

    private async Task<(System.Net.HttpStatusCode statusCode, KabuSendOrderResponse? body)> SendOrderInternalAsync(
        string url, KabuSendOrderRequest request, bool useRefresh, CancellationToken ct)
    {
        _logger.LogInformation("SendOrderInternal 開始 (useRefresh={UseRefresh})...", useRefresh);
        var token = useRefresh
            ? await _tokenService.RefreshAsync(ct)
            : await _tokenService.GetTokenAsync(ct);
        _logger.LogInformation("token 準備完了 (長さ={Len})。 HTTP POST 送信...", token?.Length ?? 0);

        var requestJson = System.Text.Json.JsonSerializer.Serialize(request);
        // ⚠️ 運用上の注意: 送信 body には kabu 取引暗証番号 (Password フィールド) が含まれる。
        //   ログ出力時のみ "***" にマスクすること。kabu に実送信される body は生の値のまま (HTTPS over localhost なので通信路リスクは低)。
        //   ログを Slack やサポートに共有する際、生のパスワードが流出しないようここで一段噛ませる。
        _logger.LogInformation("/sendorder/future 送信 body={Body}", MaskOrderPassword(requestJson));

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-API-KEY", token);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.OrderTimeoutSeconds));

        try
        {
            using var response = await _http.SendAsync(req, cts.Token);
            _logger.LogInformation("HTTP POST 応答受信 (ステータス={Status})。ボディ読み込み中...", (int)response.StatusCode);
            KabuSendOrderResponse? body = null;
            try
            {
                body = await response.Content.ReadFromJsonAsync<KabuSendOrderResponse>(cancellationToken: ct);
            }
            catch (Exception jex)
            {
                _logger.LogWarning(jex, "応答 JSON パース失敗。ステータス={Status}", (int)response.StatusCode);
            }
            return (response.StatusCode, body);
        }
        catch (OperationCanceledException oce) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogError(oce, "/sendorder タイムアウト ({Sec} 秒)。", _options.OrderTimeoutSeconds);
            throw;
        }
    }

    /// <summary>注文取消 /cancelorder。</summary>
    public async Task<KabuSendOrderResponse> CancelOrderAsync(
        string brokerOrderId, string orderPassword, CancellationToken ct = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/cancelorder";
        var body = new
        {
            OrderId = brokerOrderId,
            Password = orderPassword
        };
        using var req = await BuildJsonRequestAsync(HttpMethod.Put, url, body, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.OrderTimeoutSeconds));

        using var response = await _http.SendAsync(req, cts.Token);
        return await response.Content.ReadFromJsonAsync<KabuSendOrderResponse>(cancellationToken: ct)
               ?? new KabuSendOrderResponse { Code = -1, Message = "Empty response" };
    }

    /// <summary>建玉一覧 /positions?product=...
    /// 定期リコンサイルで頻繁に呼ばれるため、応答ログは Debug レベルに留める
    /// (UI ログタブを埋め尽くさないため。診断時はログレベルを Debug に下げれば見られる)。
    /// </summary>
    public async Task<IReadOnlyList<KabuPositionDto>> GetPositionsAsync(CancellationToken ct = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/positions?product={_options.Product}";
        using var req = await BuildGetRequestAsync(url, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.QueryTimeoutSeconds));

        using var response = await _http.SendAsync(req, cts.Token);
        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogDebug(
            "/positions 応答: product={Product} ステータス={Status} body長={Len}",
            _options.Product, (int)response.StatusCode, body.Length);
        if (string.IsNullOrWhiteSpace(body)) return Array.Empty<KabuPositionDto>();
        // kabu は未認証・セッション外 (kabu Station ログアウト時) などで、建玉配列でなく
        // エラーエンベロープ {"Code":...,"Message":...} を返す (例: Code=4001007 ログイン認証エラー)。
        // これは想定内なので、例外スタック付き警告で 30 秒ごとにログを埋めず、Debug で簡潔に記録し
        // 空を返す (UI ログのノイズ抑制)。配列 '[' で始まらない応答＝建玉なしとして扱う。
        if (!body.TrimStart().StartsWith('['))
        {
            _logger.LogDebug(
                "/positions が建玉配列でない応答 (未認証/セッション外の想定内): body={Body}", body);
            return Array.Empty<KabuPositionDto>();
        }
        try
        {
            var list = System.Text.Json.JsonSerializer.Deserialize<KabuPositionDto[]>(body);
            return list ?? Array.Empty<KabuPositionDto>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "/positions の JSON パース失敗 (配列だが解析不能): body={Body}", body);
            return Array.Empty<KabuPositionDto>();
        }
    }

    /// <summary>kabu /orders 応答 body を安全に注文配列へ解析する。
    /// 未認証・セッション外 (kabu Station ログアウト時) では配列でなくエラーエンベロープ
    /// {"Code":...,"Message":...} を返すため、配列 '[' で始まらない応答は「注文なし」として
    /// Debug 記録し空を返す (例外/Warning スパム防止)。/positions と同方針。</summary>
    private IReadOnlyList<KabuOrderDto> ParseOrders(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return Array.Empty<KabuOrderDto>();
        if (!body.TrimStart().StartsWith('['))
        {
            _logger.LogDebug(
                "/orders が注文配列でない応答 (未認証/セッション外の想定内): body={Body}", body);
            return Array.Empty<KabuOrderDto>();
        }
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<KabuOrderDto[]>(body)
                   ?? Array.Empty<KabuOrderDto>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "/orders の JSON パース失敗 (配列だが解析不能): body={Body}", body);
            return Array.Empty<KabuOrderDto>();
        }
    }

    /// <summary>注文一覧 /orders?product=...</summary>
    public async Task<IReadOnlyList<KabuOrderDto>> GetOrdersAsync(CancellationToken ct = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/orders?product={_options.Product}";
        using var req = await BuildGetRequestAsync(url, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.QueryTimeoutSeconds));

        using var response = await _http.SendAsync(req, cts.Token);
        var body = await response.Content.ReadAsStringAsync(ct);
        return ParseOrders(body);
    }

    /// <summary>
    /// 単一注文照会 /orders?product=...&id=brokerOrderId。
    /// 旧 N225OrderBridge の Orders_Future.Orders(XAPIkey, entity) 相当。
    /// 約定待ち注文を 1 件ずつ照会する pending 追跡型ポーリングで使用。
    /// </summary>
    public async Task<KabuOrderDto?> GetOrderByIdAsync(string brokerOrderId, CancellationToken ct = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/orders?product={_options.Product}&id={brokerOrderId}";
        using var req = await BuildGetRequestAsync(url, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.QueryTimeoutSeconds));

        using var response = await _http.SendAsync(req, cts.Token);
        var body = await response.Content.ReadAsStringAsync(ct);
        return ParseOrders(body).FirstOrDefault();
    }

    /// <summary>気配 /board/{symbol}@{exchange}</summary>
    public async Task<KabuBoardDto?> GetBoardAsync(string symbol, CancellationToken ct = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/board/{symbol}@{_options.Exchange}";
        using var req = await BuildGetRequestAsync(url, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.QueryTimeoutSeconds));

        using var response = await _http.SendAsync(req, cts.Token);
        return await response.Content.ReadFromJsonAsync<KabuBoardDto>(cancellationToken: ct);
    }

    /// <summary>
    /// 銘柄名解決 /symbolname/future?FutureCode=...&amp;DerivMonth=0 (0=現月)。
    /// 旧 N225OrderBridge の Symbolname_Future 相当。
    /// 起動時に FutureCode (NK225mini 等) から現月の具体銘柄コードを取得する。
    /// 診断のため、生レスポンス body を一度ログに出してから JSON パースする。
    /// </summary>
    public async Task<KabuSymbolNameDto?> GetSymbolNameAsync(
        string futureCode, int derivMonth = 0, CancellationToken ct = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/symbolname/future?FutureCode={futureCode}&DerivMonth={derivMonth}";
        using var req = await BuildGetRequestAsync(url, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.QueryTimeoutSeconds));

        using var response = await _http.SendAsync(req, cts.Token);
        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation(
            "/symbolname/future 応答: FutureCode={FutureCode} DerivMonth={DerivMonth} ステータス={Status} body={Body}",
            futureCode, derivMonth, (int)response.StatusCode, body);
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<KabuSymbolNameDto>(body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "/symbolname/future の JSON パース失敗: body={Body}", body);
            return null;
        }
    }

    /// <summary>
    /// 銘柄詳細取得 /symbol/{symbol}@{exchange}?info=true。
    /// 旧 N225OrderBridge の Symbol_Future 相当。
    /// TradeEnd / DerivMonth 等を取得して限月計算 (DerivMonthCalculator) で使う。
    /// </summary>
    public async Task<KabuSymbolFutureDto?> GetSymbolFutureAsync(
        string symbol, int exchange, CancellationToken ct = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/symbol/{symbol}@{exchange}?info=true";
        using var req = await BuildGetRequestAsync(url, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.QueryTimeoutSeconds));

        using var response = await _http.SendAsync(req, cts.Token);
        return await response.Content.ReadFromJsonAsync<KabuSymbolFutureDto>(cancellationToken: ct);
    }

    /// <summary>
    /// /register エンドポイント。WebSocket 経由で board push を受け取る銘柄を 1 件登録する。
    /// 単一銘柄の便利メソッド。複数銘柄を登録する時は <see cref="RegisterSymbolsAsync"/> を使う。
    /// </summary>
    public Task RegisterSymbolAsync(string symbol, int exchange, CancellationToken ct = default)
        => RegisterSymbolsAsync(new[] { (symbol, exchange) }, ct);

    /// <summary>
    /// /register エンドポイント。Symbols 配列で複数銘柄を一括登録できる。
    /// kabu API リファレンスでは配列対応 ([dev-rules](../../../docs/dev-rules.md) §2)。
    /// ただし 2026-05-18〜19 に Mini+Micro 同時登録で **Micro 銘柄の push が来ない事象** を
    /// 確認したため、現在の呼び出し元は <see cref="RegisterSymbolAsync"/> を per-symbol で
    /// 使う方式に戻している (<c>KabuAdapter.SubscribePricesAsync</c> 参照)。
    /// 失敗時は kabu の応答ボディも残す。
    /// </summary>
    public async Task RegisterSymbolsAsync(
        IEnumerable<(string Symbol, int Exchange)> entries, CancellationToken ct = default)
    {
        var list = entries.Where(e => !string.IsNullOrEmpty(e.Symbol)).ToList();
        if (list.Count == 0) return;

        var url = $"{_options.BaseUrl.TrimEnd('/')}/register";
        var body = new
        {
            Symbols = list.Select(e => new { Symbol = e.Symbol, Exchange = e.Exchange }).ToArray()
        };
        using var req = await BuildJsonRequestAsync(HttpMethod.Put, url, body, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.QueryTimeoutSeconds));
        using var response = await _http.SendAsync(req, cts.Token);
        var reqDesc = string.Join(",", list.Select(e => $"{e.Symbol}@{e.Exchange}"));
        if (!response.IsSuccessStatusCode)
        {
            var respBody = await response.Content.ReadAsStringAsync(cts.Token);
            _logger.LogWarning(
                "/register 失敗 status={Status} 要求={Req} 応答={Body}",
                (int)response.StatusCode, reqDesc, respBody);
            response.EnsureSuccessStatusCode();
        }
        _logger.LogInformation("/register 成功: {Req}", reqDesc);
    }

    /// <summary>
    /// /unregister エンドポイント。WebSocket push から銘柄を解除する。
    /// </summary>
    public async Task UnregisterSymbolAsync(string symbol, int exchange, CancellationToken ct = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/unregister";
        var body = new
        {
            Symbols = new[] { new { Symbol = symbol, Exchange = exchange } }
        };
        using var req = await BuildJsonRequestAsync(HttpMethod.Put, url, body, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.QueryTimeoutSeconds));
        using var response = await _http.SendAsync(req, cts.Token);
        response.EnsureSuccessStatusCode();
    }

    // ── 共通リクエスト組立 ──────────────────────────────────────

    private async Task<HttpRequestMessage> BuildJsonRequestAsync(
        HttpMethod method, string url, object body, CancellationToken ct)
    {
        var token = await _tokenService.GetTokenAsync(ct);
        var req = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Add("X-API-KEY", token);
        return req;
    }

    private async Task<HttpRequestMessage> BuildGetRequestAsync(string url, CancellationToken ct)
    {
        var token = await _tokenService.GetTokenAsync(ct);
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-API-KEY", token);
        return req;
    }

    /// <summary>
    /// 注文 JSON ログ用に "Password":"..." を "Password":"***" に置換する。
    /// 値中にエスケープされたダブルクオートは想定外 (取引暗証番号は通常英数のみ) なので単純な正規表現で十分。
    ///
    /// ⚠️ 運用上の注意:
    ///   - **kabu に実送信される body は生のパスワードのまま**。マスクするのはログ出力時だけ。
    ///   - これにより `n225brokerbridge-YYYYMMDD.log` には絶対に生パスワードが残らない。
    ///     ログを GitHub Issue / Slack / メール添付で共有する際の事故防止のため。
    ///   - 過去 (2026-05-22 以前のバージョン) は生 body をそのまま LogInformation していたため、
    ///     ログに生パスワードが残ってしまった。本マスク実装以降は新規ログには残らないが、
    ///     **過去のログファイルには残っている**ので、共有前に必ず確認・削除すること。
    /// </summary>
    private static string MaskOrderPassword(string requestJson)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            requestJson,
            "(\"Password\"\\s*:\\s*\")[^\"]*(\")",
            "$1***$2");
    }
}
