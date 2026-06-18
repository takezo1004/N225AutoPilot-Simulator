using N225BrokerBridge.Domain.Brokers;

namespace N225BrokerBridge.Application.Common;

/// <summary>
/// kabu 等のブローカーから定期的に取得した <see cref="OrderSnapshot"/> の全件 push 通知。
///
/// 旧 N225OrderBridge では InquiryTimer が 1 秒ごとに /orders を叩いて全注文を UI に流していた。
/// 新ブリッジでは KabuOrderPollingService がポーリング結果を本通知で再発火し、
/// UI (MainViewModel) が注文一覧 DataGrid を更新する。
///
/// 全件 push のため、購読側は受信ごとに一覧を全クリア + 再投入する想定。
/// </summary>
public interface IOrderSnapshotNotifier
{
    event EventHandler<OrderSnapshotsEventArgs>? SnapshotsUpdated;

    /// <summary>
    /// 最後に通知したスナップショット一覧。
    /// 起動順の都合で MainViewModel 生成より前に InitialFetchOrdersAsync が走り
    /// SnapshotsUpdated を取りこぼす場合があるため、後発の購読者がここから現状を取り直せるようにする。
    /// </summary>
    IReadOnlyList<OrderSnapshot> LatestSnapshots { get; }
}

public sealed class OrderSnapshotsEventArgs : EventArgs
{
    public IReadOnlyList<OrderSnapshot> Snapshots { get; }
    public DateTime At { get; }

    public OrderSnapshotsEventArgs(IReadOnlyList<OrderSnapshot> snapshots, DateTime at)
    {
        Snapshots = snapshots;
        At = at;
    }
}
