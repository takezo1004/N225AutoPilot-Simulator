using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Orders;

/// <summary>
/// 注文集約の永続化抽象。Domain 層に置き、Infrastructure 層で実装する (依存性逆転)。
/// </summary>
public interface IOrderRepository
{
    /// <summary>新しい注文を保存。</summary>
    Task AddAsync(Order order, CancellationToken ct = default);

    /// <summary>既存注文の状態更新を保存。</summary>
    Task UpdateAsync(Order order, CancellationToken ct = default);

    /// <summary>内部 ID で注文を取得。なければ null。</summary>
    Task<Order?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>ブローカー OrderId で注文を取得。なければ null。</summary>
    Task<Order?> FindByBrokerOrderIdAsync(BrokerCode brokerCode, OrderId brokerOrderId, CancellationToken ct = default);

    /// <summary>未終端 (アクティブ) な注文一覧。</summary>
    Task<IReadOnlyList<Order>> FindActiveAsync(CancellationToken ct = default);
}
