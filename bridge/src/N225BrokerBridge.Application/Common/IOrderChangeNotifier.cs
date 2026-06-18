using N225BrokerBridge.Domain.Orders;

namespace N225BrokerBridge.Application.Common;

/// <summary>
/// IOrderRepository への変更を購読するための通知抽象。
/// UI (MainViewModel) が注文一覧を最新状態に保つために使う。
/// </summary>
public interface IOrderChangeNotifier
{
    event EventHandler<OrderChangedEventArgs>? Changed;
}

public enum OrderChangeKind
{
    Added,
    Updated
    // 旧 N225OrderBridge と同じく、注文は終端状態でも履歴として表示し続けるため Removed は今は無し
}

public sealed class OrderChangedEventArgs : EventArgs
{
    public OrderChangeKind Kind { get; }
    public Order Order { get; }

    private OrderChangedEventArgs(OrderChangeKind kind, Order order)
    {
        Kind = kind;
        Order = order;
    }

    public static OrderChangedEventArgs Added(Order o) =>
        new(OrderChangeKind.Added, o);

    public static OrderChangedEventArgs Updated(Order o) =>
        new(OrderChangeKind.Updated, o);
}
