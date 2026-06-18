namespace N225BrokerBridge.Application.Sync;

/// <summary>
/// 起動時の注文一覧初期取得抽象。
/// 旧 N225OrderBridge の <c>InitialOrdersLIstView</c> 相当。
/// Infrastructure 層 (KabuOrderPollingService) で実装、BrokerSessionInitializer から呼ぶ。
/// </summary>
public interface IOrderInitialFetcher
{
    /// <summary>
    /// kabu /orders を全件取得して UI に push する。返り値は取得件数。
    /// </summary>
    Task<int> InitialFetchOrdersAsync(CancellationToken ct = default);
}
