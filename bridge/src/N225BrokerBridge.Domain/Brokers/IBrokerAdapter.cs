using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Brokers;

/// <summary>
/// ブローカーアダプタの統一インターフェース。
///
/// 各証券会社固有の API (REST / WebSocket / COM / TCP 等) をこのインターフェースで隠蔽する。
/// 実装は Infrastructure 層に置く (例: KabuAdapter, RakutenAdapter)。
/// ドメイン層から見える「ブローカー」はこのインターフェースだけ。
///
/// 設計原則:
///   - 同期レスポンス系は async/await
///   - 非同期通知系 (約定通知・価格ティック) は IObservable&lt;T&gt; (Rx.NET)
///   - すべてのリクエストには CorrelationId が含まれ、応答との突合に使う
///   - エラーはアダプタ内で適切にラップし、ドメイン用語の例外に変換する
/// </summary>
public interface IBrokerAdapter
{
    /// <summary>ブローカー識別。</summary>
    BrokerCode BrokerCode { get; }

    /// <summary>現在ブローカーに接続中か。</summary>
    bool IsConnected { get; }

    /// <summary>新規注文を発注する。</summary>
    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default);

    /// <summary>建玉を返済する (部分返済可)。</summary>
    Task<OrderResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken ct = default);

    /// <summary>注文を取消する。</summary>
    Task<OrderResult> CancelOrderAsync(OrderId brokerOrderId, CancellationToken ct = default);

    /// <summary>現在保有中の建玉一覧を取得する。</summary>
    Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(CancellationToken ct = default);

    /// <summary>現在の注文一覧を取得する。</summary>
    Task<IReadOnlyList<OrderSnapshot>> GetOrdersAsync(CancellationToken ct = default);

    /// <summary>銘柄の現在気配を取得する。</summary>
    Task<QuoteSnapshot> GetQuoteAsync(SymbolCode symbol, CancellationToken ct = default);

    /// <summary>約定通知ストリーム (全注文の約定が流れてくる)。</summary>
    IObservable<ExecutionEvent> ExecutionStream { get; }

    /// <summary>価格ティックストリーム (購読中銘柄のティックが流れてくる)。</summary>
    IObservable<PriceTick> PriceStream { get; }

    /// <summary>価格ストリームに銘柄を追加購読する (1 件)。</summary>
    Task SubscribePriceAsync(SymbolCode symbol, CancellationToken ct = default);

    /// <summary>価格ストリームに複数銘柄を一括追加購読する (API 最適化: 1 リクエストで複数登録)。</summary>
    Task SubscribePricesAsync(IEnumerable<SymbolCode> symbols, CancellationToken ct = default);

    /// <summary>価格ストリームから銘柄を解除する。</summary>
    Task UnsubscribePriceAsync(SymbolCode symbol, CancellationToken ct = default);

    /// <summary>
    /// 先物コード (例: "NK225mini") から指定の限月の具体銘柄コードを解決する。
    /// derivMonth=0 で現月。起動時に 1 回呼んで銘柄選択 UI を確定する用途。
    /// 解決失敗時は null を返す (ブローカー未接続等)。
    /// </summary>
    Task<ResolvedSymbol?> ResolveFutureSymbolAsync(
        string futureCode, int derivMonth = 0, CancellationToken ct = default);
}
