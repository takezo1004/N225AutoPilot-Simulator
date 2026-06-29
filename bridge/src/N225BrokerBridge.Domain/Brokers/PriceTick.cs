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
    DateTime VolumeAt = default)
{
    // ── 拡張 (2026-06-29): board push に含まれる OHLC ＋ 歩み値系フィールド。──
    //   いずれも同じ board push に含まれる (追加 API 呼び出しなし)。未提供は null/0/default ＝後方互換。
    //   ★始値/高値/安値は「当日セッション累積値」。受信側 (LocalEngine) が *At の時刻で当該バーに割当てる。

    /// <summary>始値 (OpeningPrice・当日寄付)。未提供は null。</summary>
    public Price? Open { get; init; }
    /// <summary>始値時刻 (OpeningPriceTime, UTC)。</summary>
    public DateTime OpenAt { get; init; }
    /// <summary>高値 (HighPrice・当日累積)。未提供は null。</summary>
    public Price? High { get; init; }
    /// <summary>高値時刻 (HighPriceTime, UTC)。</summary>
    public DateTime HighAt { get; init; }
    /// <summary>安値 (LowPrice・当日累積)。未提供は null。</summary>
    public Price? Low { get; init; }
    /// <summary>安値時刻 (LowPriceTime, UTC)。</summary>
    public DateTime LowAt { get; init; }

    /// <summary>kabu BidQty を生で保持 (= 売り板数量＝慣習の ASK 数量。BidPrice と同じ kabu 命名流儀)。
    /// 転送時に AiTickForwarder が慣習へ正規化 (ask_qty ← BidQty)。歩み値/簡易板圧力用。</summary>
    public decimal BidQty { get; init; }
    /// <summary>kabu AskQty を生で保持 (= 買い板数量＝慣習の BID 数量)。転送時に bid_qty ← AskQty へ正規化。</summary>
    public decimal AskQty { get; init; }
    /// <summary>当日 VWAP。未提供は 0。</summary>
    public decimal Vwap { get; init; }
    /// <summary>現値前値比較コード (CurrentPriceChangeStatus・0057=UP/0058=DOWN/0059=寄り初値 等)。約定方向推定/寄付引け判定用。</summary>
    public string ChangeStatus { get; init; } = "";
}
