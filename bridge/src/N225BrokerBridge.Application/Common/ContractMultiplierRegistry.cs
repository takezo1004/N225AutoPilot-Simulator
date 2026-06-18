using System.Collections.Concurrent;

namespace N225BrokerBridge.Application.Common;

/// <summary>
/// <see cref="IContractMultiplierResolver"/> の既定実装。
///
/// UI (MainViewModel) が銘柄解決完了時に <see cref="Set"/> で
/// (resolvedSymbolCode → ProfitMultiplier) を登録し、ExecutionApplier が
/// <see cref="Resolve"/> で参照する。Singleton 1 個で全ライフタイム共有。スレッド安全。
/// 詳細: docs/position-history-spec.md §4-3。
/// </summary>
public sealed class ContractMultiplierRegistry : IContractMultiplierResolver
{
    private readonly ConcurrentDictionary<string, int> _map = new(StringComparer.Ordinal);

    /// <summary>銘柄コードに倍率を登録する (空コード・非正の倍率は無視)。</summary>
    public void Set(string? symbolCode, int multiplier)
    {
        if (string.IsNullOrEmpty(symbolCode) || multiplier <= 0) return;
        _map[symbolCode] = multiplier;
    }

    /// <inheritdoc/>
    public int Resolve(string symbolCode, int fallback = 1)
        => !string.IsNullOrEmpty(symbolCode) && _map.TryGetValue(symbolCode, out var m) ? m : fallback;
}
