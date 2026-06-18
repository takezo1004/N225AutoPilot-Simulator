using N225BrokerBridge.Application.Orders;
using N225BrokerBridge.Application.Positions;
using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace N225BrokerBridge.Application.Signals;

/// <summary>
/// シグナル受信時のオーケストレーター。
///
/// インフラ層 (TcpWebhookServer) が <see cref="SignalPayload"/> をパースしてこのクラスを呼ぶ。
/// 本クラスの責務:
///   1. 自動売買トグル (グローバル ON/OFF) の確認
///   2. パスフレーズ認証
///   3. 戦略の有効性チェック (alert_name + interval の登録 + IsEnabled)
///   4. 自動売買対象銘柄 (IAutoTradeInstrumentProvider) の解決確認
///   5. SignalInterpreter で SignalIntent に変換 (発注先銘柄は provider 由来)
///   6. Intent 型に応じて適切な UseCase へ振り分け
///   7. 結果を返却 (呼び出し側がログ・通知)
///
/// ⚠️ 運用上の注意:
///   - payload.SymbolTicker (TV ティッカー) は発注に**一切使わない**。
///     ブリッジで選択中の銘柄 (IAutoTradeInstrumentProvider.ResolvedSymbolCode) を発注先として採用する。
///   - 起動直後で銘柄が未解決のときはシグナルを Ignored_ で拒否する (発注経路を停止)。
///     未解決のまま投げると kabu API が Code=4002001 "銘柄が見つからない" を返すため、
///     ブリッジ側で先に止める方が安全。
///   - 詳細は docs/architecture.md §3.5 を参照。
/// </summary>
public sealed class SignalHandler
{
    private readonly ISignalAuthenticator _authenticator;
    private readonly IAutoTradeGate _autoTradeGate;
    private readonly IAutoTradeInstrumentProvider _instrumentProvider;
    private readonly IStrategyRegistry _strategyRegistry;
    private readonly PlaceNewOrderUseCase _placeNew;
    private readonly ClosePositionUseCase _close;
    private readonly DotenUseCase _doten;
    private readonly ILogger<SignalHandler> _logger;

    public SignalHandler(
        ISignalAuthenticator authenticator,
        IAutoTradeGate autoTradeGate,
        IAutoTradeInstrumentProvider instrumentProvider,
        IStrategyRegistry strategyRegistry,
        PlaceNewOrderUseCase placeNew,
        ClosePositionUseCase close,
        DotenUseCase doten,
        ILogger<SignalHandler> logger)
    {
        _authenticator = authenticator;
        _autoTradeGate = autoTradeGate;
        _instrumentProvider = instrumentProvider;
        _strategyRegistry = strategyRegistry;
        _placeNew = placeNew;
        _close = close;
        _doten = doten;
        _logger = logger;
    }

    public async Task<SignalHandleOutcome> HandleAsync(
        SignalPayload payload,
        TradeMode tradeMode,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // 0. 自動売買グローバルゲート (UI トグル) が OFF なら即停止
        if (!_autoTradeGate.IsEnabled)
        {
            _logger.LogInformation(
                "Signal skipped: 自動売買 OFF alert={AlertName}", payload.AlertName);
            return SignalHandleOutcome.AutoTradeDisabled(payload.AlertName);
        }

        // 1. 認証
        if (!_authenticator.Authenticate(payload.Passphrase))
        {
            _logger.LogWarning(
                "Signal rejected: passphrase mismatch alert={AlertName}", payload.AlertName);
            return SignalHandleOutcome.AuthenticationFailed(payload.AlertName);
        }

        // 2. 戦略の有効性チェック (旧 StrategyManager.IsTrade 相当)
        if (!_strategyRegistry.IsEnabled(payload.AlertName, payload.Interval))
        {
            _logger.LogInformation(
                "Signal skipped: strategy not enabled alert={AlertName} interval={Interval}",
                payload.AlertName, payload.Interval);
            // 記録だけはしておく (戦略が登録済みなら最終受信時刻を更新)
            await _strategyRegistry.MarkSignalReceivedAsync(
                payload.AlertName, payload.Interval, DateTime.UtcNow, ct);
            return SignalHandleOutcome.Ignored(
                $"Strategy '{payload.AlertName}' (interval={payload.Interval}) is not enabled");
        }
        await _strategyRegistry.MarkSignalReceivedAsync(
            payload.AlertName, payload.Interval, DateTime.UtcNow, ct);

        // 3. 発注対象銘柄の決定。
        //    ⚠️ 運用ルール: payload.SymbolTicker (TV ティッカー、例: "NK225M1!") は使わない。
        //                  ブリッジで選択中の銘柄の Resolved Symbol Code (kabu 数値コード、例: "161060023") を使う。
        //                  これは "TV では Mini を見ながら kabu では Micro を発注" のような運用を許容する設計。
        //    安全側: 起動直後で銘柄未解決 (kabu /symbolname/future 応答前) の場合は発注経路を停止する。
        //            null のまま投げると kabu が Code=4002001 で蹴り、ログだけが汚れる結果になるため。
        var resolvedSymbolCode = _instrumentProvider.ResolvedSymbolCode;
        if (string.IsNullOrEmpty(resolvedSymbolCode))
        {
            var reason = $"自動売買対象銘柄が未解決 (起動直後/kabu API 応答待ち) のためシグナルを拒否: alert={payload.AlertName}";
            _logger.LogWarning(reason);
            return SignalHandleOutcome.Ignored(reason);
        }
        SymbolCode targetSymbol;
        try
        {
            targetSymbol = new SymbolCode(resolvedSymbolCode);
        }
        catch (InvalidValueObjectException ex)
        {
            _logger.LogError(ex,
                "自動売買対象銘柄コード不正: code={Code} alert={AlertName}",
                resolvedSymbolCode, payload.AlertName);
            return SignalHandleOutcome.InterpretationFailed(payload.AlertName, ex.Message);
        }

        // 4. 解釈 (payload.SymbolTicker は使わず、上記の targetSymbol を発注先銘柄として使う)
        SignalIntent intent;
        try
        {
            intent = SignalInterpreter.Interpret(payload, tradeMode, targetSymbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Signal interpretation failed alert={AlertName}", payload.AlertName);
            return SignalHandleOutcome.InterpretationFailed(payload.AlertName, ex.Message);
        }

        // UI 戦略一覧の「最終受信シグナル」コンテキストを更新
        // (旧 N225OrderBridge の StrategyViewList DateTime/TradeType/Side/Price 表示相当)
        var lastSideDisplay = payload.OrderAction?.Trim().ToLowerInvariant() switch
        {
            "buy" => "買",
            "sell" => "売",
            _ => payload.OrderAction ?? string.Empty
        };
        var lastTradeTypeDisplay = intent switch
        {
            NewOrderIntent => "新規",
            ExitOrderIntent => "返済",
            DotenIntent => "ドテン",
            _ => "—"
        };
        await _strategyRegistry.UpdateLastSignalAsync(
            payload.AlertName, payload.Interval, DateTime.UtcNow,
            lastTradeTypeDisplay, lastSideDisplay, payload.OrderPrice, ct);

        // 5. 振り分け
        switch (intent)
        {
            case NewOrderIntent n:
                _logger.LogInformation(
                    "Signal → NewOrder strategy={Strategy} symbol={Symbol} side={Side} qty={Qty}",
                    n.Strategy, n.Symbol, n.Side, n.Quantity);
                var placeResult = await _placeNew.ExecuteAsync(n, ct);
                return SignalHandleOutcome.NewOrderDispatched(placeResult);

            case ExitOrderIntent e:
                _logger.LogInformation(
                    "Signal → ExitOrder strategy={Strategy} symbol={Symbol} originalSide={OriginalSide} qty={Qty}",
                    e.Strategy, e.Symbol, e.OriginalSide, e.Quantity);
                var closeResult = await _close.ExecuteAsync(e, ct);
                return SignalHandleOutcome.ExitOrderDispatched(closeResult);

            case DotenIntent d:
                _logger.LogInformation(
                    "Signal → Doten strategy={Strategy} symbol={Symbol} originalSide={OriginalSide} exit={ExitQty} new={NewQty}",
                    d.Strategy, d.Symbol, d.OriginalSide, d.ExitQuantity, d.NewQuantity);
                var dotenResult = await _doten.ExecuteAsync(d, ct);
                return SignalHandleOutcome.DotenDispatched(dotenResult);

            case IgnoreIntent i:
                _logger.LogInformation(
                    "Signal ignored strategy={Strategy} reason={Reason}", i.Strategy, i.Reason);
                return SignalHandleOutcome.Ignored(i.Reason);

            default:
                throw new InvalidOperationException($"Unknown SignalIntent: {intent.GetType().Name}");
        }
    }
}

/// <summary>
/// シグナル処理の結果。判別共用体。
/// </summary>
public abstract record SignalHandleOutcome
{
    public sealed record AutoTradeDisabled_(string AlertName) : SignalHandleOutcome;
    public sealed record Authenticated_Failed(string AlertName) : SignalHandleOutcome;
    public sealed record Interpretation_Failed(string AlertName, string ErrorMessage) : SignalHandleOutcome;
    public sealed record Ignored_(string Reason) : SignalHandleOutcome;
    public sealed record NewOrderDispatched_(PlaceNewOrderResult Result) : SignalHandleOutcome;
    public sealed record ExitOrderDispatched_(ClosePositionResult Result) : SignalHandleOutcome;
    public sealed record DotenDispatched_(DotenResult Result) : SignalHandleOutcome;

    public static SignalHandleOutcome AutoTradeDisabled(string alert) => new AutoTradeDisabled_(alert);
    public static SignalHandleOutcome AuthenticationFailed(string alert) => new Authenticated_Failed(alert);
    public static SignalHandleOutcome InterpretationFailed(string alert, string err) => new Interpretation_Failed(alert, err);
    public static SignalHandleOutcome Ignored(string reason) => new Ignored_(reason);
    public static SignalHandleOutcome NewOrderDispatched(PlaceNewOrderResult r) => new NewOrderDispatched_(r);
    public static SignalHandleOutcome ExitOrderDispatched(ClosePositionResult r) => new ExitOrderDispatched_(r);
    public static SignalHandleOutcome DotenDispatched(DotenResult r) => new DotenDispatched_(r);
}
