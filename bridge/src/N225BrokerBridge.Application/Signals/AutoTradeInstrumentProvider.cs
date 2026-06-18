namespace N225BrokerBridge.Application.Signals;

/// <summary>
/// 自動売買で発注に使う「対象銘柄のシンボルコード」(kabu の数値銘柄コード) を保持する。
/// UI のグローバル銘柄選択 (MainViewModel.ManualOrderInstrument) と SignalHandler を疎結合に接続する。
///
/// ────────────────────────────────────────────────────────────────────
/// ⚠️ 運用上の注意 (本ブリッジを触る開発者は必ず読むこと)
/// ────────────────────────────────────────────────────────────────────
///
/// 1. **TradingView Webhook の SymbolTicker は発注に一切使われない**。
///    payload.SymbolTicker (例: "OSE:NK225M1!") は SignalHandler/SignalInterpreter で完全に無視される。
///    発注先銘柄は常にこの provider が保持する ResolvedSymbolCode (kabu の数値コード) になる。
///
/// 2. **TV のチャート銘柄と発注先銘柄が一致しない運用を許容する**。
///    例: TV では Mini (NK225M1!) を見ながら、kabu 口座資金の都合で Micro (161060023) を発注する。
///    この場合 TV 側で Pine 戦略のシンボル設定は変えなくてよい (本ブリッジが発注先を上書きするため)。
///
/// 3. **手動発注パネルの選択銘柄 = 自動売買の発注先銘柄** (兼用設計)。
///    MainViewModel.ManualOrderInstrument を切り替えると、同時に自動売買の発注先も切り替わる。
///    手動で Mini を試し打ちしながら自動売買は Micro を維持、といった運用は **本バージョンではできない**。
///    必要なら将来 ManualOrderInstrument と AutoTradeInstrument を分離する拡張を入れる。
///
/// 4. **戦略ごとに発注銘柄を分けることはできない** (1 セッション 1 銘柄固定)。
///    Mini と Micro を同時並行で自動売買したい場合は、本 interface を「戦略 → 銘柄」マップに拡張する必要がある。
///    現状の StrategyEntry は AlertName と IsEnabled のみで銘柄フィールドを持たない。
///
/// 5. **起動直後 (kabu /symbolname/future 応答前) は ResolvedSymbolCode が null**。
///    この間に届いた自動売買シグナルは SignalHandler が拒否し、発注経路を停止する (安全側)。
///    null のまま発注を投げると kabu API が Code=4002001 "銘柄が見つからない" を返すため、
///    そもそも投げないのが正解。
///
/// 詳細: docs/architecture.md §3.5 / docs/troubleshooting.md §6 / docs/adapters/kabu.md §8
/// ────────────────────────────────────────────────────────────────────
/// </summary>
public interface IAutoTradeInstrumentProvider
{
    /// <summary>kabu の数値銘柄コード (例: "161060023")。未解決時 null。</summary>
    string? ResolvedSymbolCode { get; }

    /// <summary>UI 表示用の銘柄名 (例: "日経225Micro")。ログ出力に使う。</summary>
    string? DisplayName { get; }

    /// <summary>限月ラベル (例: "2026年6月限")。ログ出力に使う。</summary>
    string? ContractMonth { get; }

    /// <summary>
    /// 対象銘柄を上書きする。UI 側 (MainViewModel) が銘柄解決完了時・選択変更時に呼ぶ。
    /// 値が変化したときに <see cref="Changed"/> イベントが発火する。
    /// </summary>
    void SetInstrument(string? resolvedSymbolCode, string? displayName, string? contractMonth);

    /// <summary>対象銘柄が変更されたときに発火 (ログ出力用)。</summary>
    event EventHandler? Changed;
}

public sealed class AutoTradeInstrumentProvider : IAutoTradeInstrumentProvider
{
    private readonly object _gate = new();
    private string? _resolvedSymbolCode;
    private string? _displayName;
    private string? _contractMonth;

    public string? ResolvedSymbolCode
    {
        get { lock (_gate) return _resolvedSymbolCode; }
    }

    public string? DisplayName
    {
        get { lock (_gate) return _displayName; }
    }

    public string? ContractMonth
    {
        get { lock (_gate) return _contractMonth; }
    }

    public void SetInstrument(string? resolvedSymbolCode, string? displayName, string? contractMonth)
    {
        bool changed;
        lock (_gate)
        {
            changed = !string.Equals(_resolvedSymbolCode, resolvedSymbolCode, StringComparison.Ordinal)
                   || !string.Equals(_displayName, displayName, StringComparison.Ordinal)
                   || !string.Equals(_contractMonth, contractMonth, StringComparison.Ordinal);
            _resolvedSymbolCode = resolvedSymbolCode;
            _displayName = displayName;
            _contractMonth = contractMonth;
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;
}
