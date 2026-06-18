using CommunityToolkit.Mvvm.ComponentModel;

namespace N225BrokerBridge.UI.ViewModels;

/// <summary>
/// 取引対象銘柄の定義。
/// FutureCode は kabu API の /symbolname/future で使う先物コード (例: "NK225mini", "NK225micro")。
/// ResolvedSymbolCode は起動時に現月で解決された具体銘柄コード。
///
/// 将来 n225 以外の銘柄 (TOPIX 先物・グロース先物等) を追加する場合は本クラスのインスタンスを増やす。
/// </summary>
public sealed partial class InstrumentDefinition : ObservableObject
{
    public string DisplayName { get; init; } = string.Empty;
    public string FutureCode { get; init; } = string.Empty;

    /// <summary>
    /// 損益計算用の倍率。
    /// 旧 N225OrderBridge と同じ:
    ///   日経225ラージ = 1000、日経225Mini = 100、日経225Micro = 10
    /// </summary>
    public int ProfitMultiplier { get; init; } = 1;

    [ObservableProperty]
    private string? _resolvedSymbolCode;     // 起動時に現月コードを解決して入れる

    [ObservableProperty]
    private string? _contractMonth;          // 例: "2026年6月限"

    // ── リアルタイム価格 (kabu 板情報 push で更新) ──────────────
    [ObservableProperty] private decimal _lastPrice;    // 現在値
    [ObservableProperty] private decimal _bidPrice;
    [ObservableProperty] private int _bidQty;
    [ObservableProperty] private decimal _askPrice;
    [ObservableProperty] private int _askQty;

    public override string ToString() =>
        ResolvedSymbolCode is null
            ? DisplayName
            : $"{DisplayName} ({ContractMonth ?? ResolvedSymbolCode})";
}
