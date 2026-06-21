using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Brokers;

/// <summary>
/// リアルタイム価格ティック (アダプタからのストリーム出力)。
/// 全銘柄を流す場合は SymbolCode で購読側がフィルタする。
/// </summary>
/// <remarks>
/// kabu アダプタでは BID/ASK の命名が通常と逆 (kabu BidPrice = 売り板 = トレーダー目線 ASK)。
/// <see cref="QuoteSnapshot"/> と同じく kabu の生値を保持する。
/// </remarks>
/// <param name="BrokerCode">ティック元のブローカー。</param>
/// <param name="Symbol">銘柄。</param>
/// <param name="LastPrice">最終約定価格。</param>
/// <param name="BidPrice">kabu の BidPrice (= 通常 ASK)。</param>
/// <param name="AskPrice">kabu の AskPrice (= 通常 BID)。</param>
/// <param name="At">ティックのタイムスタンプ (現値時刻 CurrentPriceTime, UTC)。</param>
/// <param name="Volume">kabu の当日累積売買高 (TradingVolume)。未提供は 0。転送側が売買高時刻の進行で per-bar 増分に変換する。</param>
/// <param name="VolumeAt">売買高時刻 (TradingVolumeTime, UTC)。未提供は MinValue。これが進んだ時だけ出来高が増えたとみなす。</param>
public sealed record PriceTick(
    BrokerCode BrokerCode,
    SymbolCode Symbol,
    Price LastPrice,
    Price BidPrice,
    Price AskPrice,
    DateTime At,
    decimal Volume = 0m,
    DateTime VolumeAt = default);
