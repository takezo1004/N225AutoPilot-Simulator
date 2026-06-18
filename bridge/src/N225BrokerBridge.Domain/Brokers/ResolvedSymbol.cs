using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Brokers;

/// <summary>
/// 先物コード (例: "NK225mini") から現月コードを解決した結果。
/// 起動時に <see cref="IBrokerAdapter.ResolveFutureSymbolAsync"/> で取得し、
/// 銘柄選択 UI / 発注時に使う具体銘柄コードを確定する。
/// </summary>
/// <param name="Symbol">具体銘柄コード (例: "167060019")。</param>
/// <param name="DisplayName">表示名 (例: "日経225mini 26/06")。</param>
/// <param name="ContractMonthLabel">限月の日本語表記 (例: "2026年6月限")。</param>
public sealed record ResolvedSymbol(
    SymbolCode Symbol,
    string DisplayName,
    string ContractMonthLabel);
