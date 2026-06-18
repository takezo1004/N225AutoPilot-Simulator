namespace N225BrokerBridge.Application.Sync;

/// <summary>
/// 未約定の注文 ID リストを追跡する抽象。
///
/// 旧 N225OrderBridge の <c>OrderInquiryList</c> (Domain.StaticVlues) 相当。
/// このブリッジから発注した注文だけを追跡し、約定 / 取消 / 期限切れで Untrack する。
///
/// 用途:
///   - <c>KabuOrderPollingService</c> は Tracker が空のときは /orders を叩かない (旧 InquiryTimer 準拠)
///   - 空でなければ追跡中の OrderID だけ個別照会して約定検出する
///
/// 設計判断 (旧との差異):
///   - 旧は global static (OrderInquiryList) だった
///   - 新は DI で注入される interface (テスト容易、複数ブローカー対応)
///   - 旧は OrderContractEntity を保持していたが、新は OrderID 文字列のみ
///     (詳細データは IOrderRepository / IOrderMetadataStore 側に分離)
/// </summary>
public interface IPendingOrderTracker
{
    /// <summary>発注成功時に呼ぶ。OrderID を追跡対象に追加。</summary>
    void Track(string brokerOrderId);

    /// <summary>約定 / 取消 / 期限切れ確認時に呼ぶ。OrderID を追跡対象から外す。</summary>
    void Untrack(string brokerOrderId);

    /// <summary>現時点で追跡中の全 OrderID (snapshot)。</summary>
    IReadOnlyList<string> GetAll();

    /// <summary>追跡対象が空 (= 約定待ち注文なし) か。</summary>
    bool IsEmpty { get; }
}
