using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using N225BrokerBridge.Application.Brokers;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Infrastructure.Brokers.Kabu;

/// <summary>
/// kabu ステーション (au カブコム証券) アダプタ。
/// <see cref="IBrokerAdapter"/> を実装し、Domain から見たブローカー操作を kabu API に橋渡しする。
///
/// 注意:
///   - ExecutionStream / PriceStream の購読源は <see cref="KabuWebSocketStream"/> (Phase 5 残)
///     当面は内部 Subject に手動で OnNext する形 (実 WebSocket 接続は次フェーズで)
///   - 注文パスワードは KabuOptions.ApiPassword とは別の "注文パスワード" が必要。
///     ここでは Options から取得する想定。
/// </summary>
public sealed class KabuAdapter : IBrokerAdapter, IDisposable
{
    private readonly KabuApiClient _client;
    private readonly KabuOptions _options;
    private readonly ILogger<KabuAdapter> _logger;
    private readonly Subject<ExecutionEvent> _executionStream = new();
    private readonly Subject<PriceTick> _priceStream = new();

    /// <inheritdoc/>
    public BrokerCode BrokerCode => BrokerCode.Kabu;
    /// <inheritdoc/>
    public bool IsConnected { get; private set; } = true;   // REST は state-less 扱い

    /// <summary>
    /// kabu アダプタを生成する。kabu API クライアントとオプションを DI で受け取る。
    /// </summary>
    /// <param name="client">kabu API REST クライアント (Singleton 必須)。</param>
    /// <param name="options">kabu 接続オプション (Exchange / OrderPassword 等)。</param>
    /// <param name="logger">ログ出力。</param>
    public KabuAdapter(
        KabuApiClient client,
        IOptions<KabuOptions> options,
        ILogger<KabuAdapter> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderPassword = _options.OrderPassword ?? string.Empty;
        var exchange = GetActiveSessionExchange();
        _logger.LogInformation("発注 Exchange 自動判定: {Exchange} ({Session})",
            exchange, exchange == 23 ? "日中" : "夜間");
        var kabuReq = KabuMappers.ToKabuRequest(request, orderPassword, exchange);
        try
        {
            var kabuResp = await _client.SendOrderAsync(kabuReq, ct);
            return KabuMappers.ToOrderResult(kabuResp, request.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "KabuAdapter.PlaceOrder failed corr={Corr}", request.CorrelationId);
            return new OrderResult(
                request.CorrelationId,
                OrderResultStatus.NetworkError,
                BrokerOrderId: null,
                ErrorCode: ex.GetType().Name,
                ErrorMessage: ex.Message,
                ReceivedAt: DateTime.UtcNow);
        }
    }

    /// <inheritdoc/>
    public async Task<OrderResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderPassword = _options.OrderPassword ?? string.Empty;
        var exchange = GetActiveSessionExchange();
        _logger.LogInformation("返済 Exchange 自動判定: {Exchange} ({Session})",
            exchange, exchange == 23 ? "日中" : "夜間");
        var kabuReq = KabuMappers.ToKabuRequest(request, orderPassword, exchange);
        try
        {
            var kabuResp = await _client.SendOrderAsync(kabuReq, ct);
            return KabuMappers.ToOrderResult(kabuResp, request.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "KabuAdapter.ClosePosition failed corr={Corr} target={Target}",
                request.CorrelationId, request.TargetExecutionId);
            return new OrderResult(
                request.CorrelationId,
                OrderResultStatus.NetworkError,
                BrokerOrderId: null,
                ErrorCode: ex.GetType().Name,
                ErrorMessage: ex.Message,
                ReceivedAt: DateTime.UtcNow);
        }
    }

    /// <inheritdoc/>
    public async Task<OrderResult> CancelOrderAsync(OrderId brokerOrderId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(brokerOrderId);

        var orderPassword = _options.OrderPassword ?? string.Empty;
        try
        {
            var resp = await _client.CancelOrderAsync(brokerOrderId.Value, orderPassword, ct);
            return KabuMappers.ToOrderResult(resp, Guid.Empty);
        }
        catch (Exception ex)
        {
            return new OrderResult(
                Guid.Empty, OrderResultStatus.NetworkError, brokerOrderId,
                ex.GetType().Name, ex.Message, DateTime.UtcNow);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(CancellationToken ct = default)
    {
        var raw = await _client.GetPositionsAsync(ct);
        return raw.Select(d => KabuMappers.ToPositionSnapshot(d, BrokerCode)).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OrderSnapshot>> GetOrdersAsync(CancellationToken ct = default)
    {
        var raw = await _client.GetOrdersAsync(ct);
        return raw.Select(d => KabuMappers.ToOrderSnapshot(d, BrokerCode)).ToList();
    }

    /// <inheritdoc/>
    public async Task<QuoteSnapshot> GetQuoteAsync(SymbolCode symbol, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        var raw = await _client.GetBoardAsync(symbol.Value, ct);
        if (raw is null)
            throw new InvalidOperationException($"Board returned null for {symbol}");
        return KabuMappers.ToQuoteSnapshot(raw, BrokerCode);
    }

    /// <inheritdoc/>
    public IObservable<ExecutionEvent> ExecutionStream => _executionStream;
    /// <inheritdoc/>
    public IObservable<PriceTick> PriceStream => _priceStream;

    /// <summary>
    /// 内部 Subject へ ExecutionEvent を発火する公開フック (KabuOrderPollingService から呼ぶ)。
    /// </summary>
    /// <param name="ev">発火するイベント。</param>
    public void PushExecution(ExecutionEvent ev) => _executionStream.OnNext(ev);

    /// <summary>
    /// 内部 Subject へ PriceTick を発火する公開フック (KabuBoardWebSocketService から呼ぶ)。
    /// </summary>
    /// <param name="tick">発火するティック。</param>
    public void PushPriceTick(PriceTick tick) => _priceStream.OnNext(tick);

    /// <inheritdoc/>
    public async Task SubscribePriceAsync(SymbolCode symbol, CancellationToken ct = default)
    {
        try
        {
            await _client.RegisterSymbolAsync(symbol.Value, _options.Exchange, ct);
            _logger.LogInformation("価格 push 購読登録 (1 件): {Symbol}", symbol);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "価格 push 購読登録失敗 {Symbol}", symbol);
        }
    }

    /// <inheritdoc/>
    public async Task SubscribePricesAsync(IEnumerable<SymbolCode> symbols, CancellationToken ct = default)
    {
        // 旧 N225OrderBridge と同じ「1 銘柄ずつ register」方式に戻す。
        // 2026-05-18 に「効率化」で bulk register に変更したところ、kabu Station 側で
        // Micro 銘柄の push が来なくなる事象が発生 (Mini=数千件 / Micro=1件) したため、
        // 動作実績のあるシリアル登録に揃える (2026-05-19)。
        foreach (var symbol in symbols)
        {
            try
            {
                await _client.RegisterSymbolAsync(symbol.Value, _options.Exchange, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "価格 push 個別購読登録失敗 {Symbol}", symbol);
            }
        }
    }

    /// <inheritdoc/>
    public async Task UnsubscribePriceAsync(SymbolCode symbol, CancellationToken ct = default)
    {
        try
        {
            await _client.UnregisterSymbolAsync(symbol.Value, _options.Exchange, ct);
            _logger.LogInformation("KabuAdapter.UnsubscribePrice: {Symbol}", symbol);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KabuAdapter.UnsubscribePrice failed {Symbol}", symbol);
        }
    }

    /// <inheritdoc/>
    public async Task<ResolvedSymbol?> ResolveFutureSymbolAsync(
        string futureCode, int derivMonth = 0, CancellationToken ct = default)
    {
        try
        {
            // 限月が指定されていれば、その月で直接取得 (旧 SymbolRequest.Request 系)
            if (derivMonth > 0)
            {
                return await GetFutureBySpecifiedMonthAsync(futureCode, derivMonth, ct);
            }

            // 現月 (derivMonth=0) リクエスト → 限月計算経由で正しい「現取引対象限月」を解決する。
            // 旧 N225OrderBridge.SymbolRequest.Request1 ロジック:
            //   1. ラージ (NK225) の current から TradeEnd を取得
            //   2. DerivMonthCalculator で SQ 日前日大引け後・夜間セッション補正をかける
            //   3. 補正された限月で futureCode を解決
            var activeMonth = await CalculateActiveDerivMonthAsync(ct);
            if (activeMonth == 0)
            {
                _logger.LogWarning(
                    "銘柄解決失敗: ラージ NK225 の TradeEnd 取得失敗のため限月計算不能 (futureCode={FutureCode})",
                    futureCode);
                return null;
            }

            return await GetFutureBySpecifiedMonthAsync(futureCode, activeMonth, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ResolveFutureSymbol failed futureCode={FutureCode}", futureCode);
            return null;
        }
    }

    /// <summary>
    /// ラージ NK225 の中心限月 TradeEnd を取得し、限月計算で「現取引対象限月」を求める。
    /// 失敗時は 0 を返す (検証ポート空応答等)。
    /// </summary>
    private async Task<int> CalculateActiveDerivMonthAsync(CancellationToken ct)
    {
        var largeDto = await _client.GetSymbolNameAsync("NK225", 0, ct);
        if (largeDto is null || string.IsNullOrEmpty(largeDto.Symbol))
        {
            _logger.LogWarning(
                "限月計算: ラージ NK225 の /symbolname/future 応答が空 (token 不一致 or kabu API エラー)");
            return 0;
        }

        // ラージは大阪取引所コード 2 (Exchange=2 = 日中)
        var symInfo = await _client.GetSymbolFutureAsync(largeDto.Symbol, 2, ct);
        if (symInfo is null || symInfo.TradeEnd <= 0)
        {
            _logger.LogWarning(
                "限月計算: ラージ NK225 ({Symbol}) の銘柄情報取得失敗 (TradeEnd={TradeEnd})",
                largeDto.Symbol, symInfo?.TradeEnd);
            return 0;
        }

        var active = DerivMonthCalculator.CalculateActiveDerivMonth(symInfo.TradeEnd, DateTime.Now);
        _logger.LogInformation(
            "限月計算: ラージ TradeEnd={TradeEnd} DerivMonth(kabu)={KabuMonth} → 現取引対象限月={Active}",
            symInfo.TradeEnd, symInfo.DerivMonth, active);
        return active;
    }

    private async Task<ResolvedSymbol?> GetFutureBySpecifiedMonthAsync(
        string futureCode, int derivMonth, CancellationToken ct)
    {
        var dto = await _client.GetSymbolNameAsync(futureCode, derivMonth, ct);
        if (dto is null || string.IsNullOrEmpty(dto.Symbol))
        {
            _logger.LogWarning(
                "銘柄解決失敗 (応答が空): 先物コード={FutureCode} 限月={Month} ※検証ポートでは仕様により常に空応答",
                futureCode, derivMonth);
            return null;
        }

        // /symbol/{symbol}@{exchange}?info=true で限月情報 (DerivMonth = "YYYY/MM") を取得し
        // ユーザーに分かりやすい日本語ラベル "YYYY年M月限" に整形する。
        // kabu API が返す DerivMonth が決定的な「現取引対象限月」(別途計算済) と一致するはずだが、
        // 安全のため API レスポンスを優先表示する。
        string label;
        try
        {
            var info = await _client.GetSymbolFutureAsync(dto.Symbol, _options.Exchange, ct);
            label = FormatContractMonthLabel(info?.DerivMonth, derivMonth);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "限月ラベル取得失敗 (フォールバック値で継続): symbol={Symbol}", dto.Symbol);
            label = FormatContractMonthLabel(null, derivMonth);
        }

        _logger.LogInformation(
            "銘柄解決成功: 先物コード={FutureCode} (限月={Month}) → 銘柄コード={Symbol} \"{Name}\" {Label}",
            futureCode, derivMonth, dto.Symbol, dto.SymbolName, label);
        return new ResolvedSymbol(
            Symbol: new SymbolCode(dto.Symbol),
            DisplayName: dto.SymbolName ?? futureCode,
            ContractMonthLabel: label);
    }

    /// <summary>
    /// kabu の DerivMonth ("YYYY/MM") またはフォールバック整数 (YYYYMM) を
    /// 日本語表記 "YYYY年M月限" に整形する。両方欠落時は "現月" を返す。
    /// </summary>
    private static string FormatContractMonthLabel(string? derivMonthApi, int fallbackYyyymm)
    {
        if (!string.IsNullOrEmpty(derivMonthApi))
        {
            var parts = derivMonthApi.Split('/');
            if (parts.Length == 2 && int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m))
                return $"{y}年{m}月限";
        }
        if (fallbackYyyymm > 0)
        {
            var y = fallbackYyyymm / 100;
            var m = fallbackYyyymm % 100;
            return $"{y}年{m}月限";
        }
        return "現月";
    }

    /// <summary>
    /// 現時刻のセッションに応じた kabu 市場コード (Exchange) を返す。
    /// 旧 N225OrderBridge の <c>Exchange.GetDerivExchange</c> 同等:
    ///   06:00〜15:45 → 23 (日中)
    ///   それ以外    → 24 (夜間)
    /// /sendorder/future は仕様で「日中/夜間」のいずれかを正しく指定する必要がある
    /// (Exchange 不一致で「パラメータ不正:値段指定エラー」等が返る)。
    /// </summary>
    private static int GetActiveSessionExchange()
    {
        var now = DateTime.Now.TimeOfDay;
        var dayEnd = new TimeSpan(15, 45, 0);
        var nightEnd = new TimeSpan(6, 0, 0);
        if (now > nightEnd && now <= dayEnd) return 23;   // 日中
        return 24;                                         // 夜間
    }

    /// <summary>
    /// 内部 Subject を完了させてリソースを解放する。
    /// REST クライアントは外部 DI ライフサイクル管理のためここでは閉じない。
    /// </summary>
    public void Dispose()
    {
        _executionStream.OnCompleted();
        _priceStream.OnCompleted();
        _executionStream.Dispose();
        _priceStream.Dispose();
    }
}
