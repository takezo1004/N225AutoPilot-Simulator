namespace N225BrokerBridge.Application.Brokers;

/// <summary>
/// 日経225 先物の限月計算ロジック。
///
/// 旧 N225OrderBridge の <c>SymbolRequest.Request1</c> ロジックを純粋関数として移植。
/// kabu API の <c>/symbolname/future?DerivMonth=0</c> は SQ 日まで「当月」を返すが、
/// **SQ 日前日 (取引最終日) の大引け以降 + 夜間セッション** では既に「次の限月」が
/// 実際に取引対象となっている。kabu API はこの境界を自動で切り替えてくれないため、
/// ブリッジ側で計算して正しい限月を取得する必要がある。
///
/// アルゴリズム:
///   1. ラージ (NK225) の中心限月の <c>TradeEnd</c> (取引最終日) を kabu API で取得
///   2. 今日と TradeEnd を比較:
///       - TradeEnd &lt; 今日 → 既に SQ 過ぎ、次の限月
///       - TradeEnd == 今日 かつ 現在時刻 &gt; 大引け時刻 → 夜間セッション切替済み、次の限月
///       - それ以外 → 当月をそのまま使う
///   3. 次の限月の場合: 3→6→9→12→翌年3 で順送り (日経 SQ 月は 3/6/9/12 のみ)
/// </summary>
public static class DerivMonthCalculator
{
    /// <summary>日中セッションの大引け時刻 (旧 <c>Shared.EndTime</c>)。デフォルト 15:45。</summary>
    public static readonly TimeSpan DefaultDayCloseTime = new(15, 45, 0);

    /// <summary>
    /// ラージの中心限月情報から、現時点での「次の取引対象限月」を計算する。
    /// </summary>
    /// <param name="tradeEnd">kabu API <c>/symbol</c> で取得した <c>TradeEnd</c> (yyyyMMdd 形式の整数、例 20260611)</param>
    /// <param name="now">現在時刻 (ローカル時刻、JST 想定)</param>
    /// <param name="dayCloseTime">大引け時刻 (デフォルト 15:45)</param>
    /// <returns>計算された限月 (yyyyMM 整数、例 202606)</returns>
    public static int CalculateActiveDerivMonth(int tradeEnd, DateTime now, TimeSpan? dayCloseTime = null)
    {
        if (tradeEnd <= 0)
            throw new ArgumentException("tradeEnd must be positive yyyyMMdd integer.", nameof(tradeEnd));

        var closeTime = dayCloseTime ?? DefaultDayCloseTime;
        var tradeEndDate = DateTime.ParseExact(
            tradeEnd.ToString("00000000"), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

        int year = tradeEndDate.Year;
        int month = tradeEndDate.Month;

        var diffDays = (tradeEndDate.Date - now.Date).TotalDays;
        bool useNext =
            (diffDays == 0 && now.TimeOfDay > closeTime)   // SQ 日前日大引け後
            || diffDays < 0;                                // SQ 日以降

        if (useNext)
        {
            month += 3;   // 3→6→9→12 で順送り
            if (month > 12)
            {
                month -= 12;
                year += 1;
            }
        }

        return year * 100 + month;
    }
}
