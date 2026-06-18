namespace N225BrokerBridge.Application.Common;

/// <summary>
/// 銘柄コード → 損益計算用の倍率 (日経225Micro=10 / Mini=100 / Large=1000) を解決する。
///
/// 倍率の正本は UI の <c>InstrumentDefinition.ProfitMultiplier</c> だが、限月切替で
/// 解決済み銘柄コードが変わるため静的な対応表にはできない。UI が銘柄解決完了時に
/// <see cref="ContractMultiplierRegistry.Set"/> で (resolvedSymbolCode → 倍率) を登録し、
/// <see cref="N225BrokerBridge.Application.Orders.ExecutionApplier"/> が決済記録時に参照する。
/// 詳細: docs/position-history-spec.md §4-3。
/// </summary>
public interface IContractMultiplierResolver
{
    /// <summary>
    /// 倍率を返す。未登録の銘柄コードは <paramref name="fallback"/> を返す。
    /// </summary>
    int Resolve(string symbolCode, int fallback = 1);
}
