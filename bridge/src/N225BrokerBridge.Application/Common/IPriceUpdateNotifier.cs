using N225BrokerBridge.Domain.Brokers;

namespace N225BrokerBridge.Application.Common;

/// <summary>
/// 価格ティック更新の購読用イベント抽象。
/// UI (MainViewModel) が IBrokerAdapter.PriceStream に Rx 依存することなく購読できるよう、
/// HostedService が IObservable&lt;PriceTick&gt; をブリッジしてここで再発火する。
/// </summary>
public interface IPriceUpdateNotifier
{
    event EventHandler<PriceTick>? PriceUpdated;
}
