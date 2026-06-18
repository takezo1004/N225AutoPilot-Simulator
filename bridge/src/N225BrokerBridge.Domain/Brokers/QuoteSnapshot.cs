using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Brokers;

/// <summary>
/// 銘柄の現在気配 (照会用、単発スナップショット)。
/// 連続価格は <see cref="PriceTick"/> ストリーム経由で取得する。
/// </summary>
/// <remarks>
/// kabu アダプタでは BID/ASK の命名が通常と逆 (kabu BidPrice = 売り板 = トレーダー目線 ASK)。
/// 本 record の <c>BidPrice</c>/<c>AskPrice</c> は kabu API の生フィールド値をそのまま保持する。
/// 詳細は <c>docs/adapters/kabu.md §1</c>。
/// </remarks>
/// <param name="BrokerCode">気配元のブローカー。</param>
/// <param name="Symbol">銘柄。</param>
/// <param name="LastPrice">最終約定価格。</param>
/// <param name="BidPrice">kabu の BidPrice (= 売り板最良気配、通常用語の ASK)。</param>
/// <param name="AskPrice">kabu の AskPrice (= 買い板最良気配、通常用語の BID)。</param>
/// <param name="BidQuantity">BidPrice の数量。</param>
/// <param name="AskQuantity">AskPrice の数量。</param>
/// <param name="At">この気配のタイムスタンプ。</param>
public sealed record QuoteSnapshot(
    BrokerCode BrokerCode,
    SymbolCode Symbol,
    Price LastPrice,
    Price BidPrice,
    Price AskPrice,
    Quantity BidQuantity,
    Quantity AskQuantity,
    DateTime At);
