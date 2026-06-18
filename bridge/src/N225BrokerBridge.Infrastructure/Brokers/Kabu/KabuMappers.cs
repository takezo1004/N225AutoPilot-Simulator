using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.ValueObjects;
using N225BrokerBridge.Infrastructure.Brokers.Kabu.Dto;

namespace N225BrokerBridge.Infrastructure.Brokers.Kabu;

/// <summary>
/// Domain ↔ kabu DTO 変換。1 か所に集約してテスト可能にする。
/// </summary>
public static class KabuMappers
{
    // ── Domain → kabu DTO ────────────────────────────────────────

    /// <summary>
    /// 新規発注 Domain リクエストを kabu /sendorder/future 用 DTO に変換する。
    /// 逆指値 (OrderType=Stop) の場合は ReverseLimitOrder ブロックも組み立てる。
    ///
    /// ⚠️ 運用上の注意:
    ///   req.Symbol.Value は **kabu の数値銘柄コード** (例: "161060023") でなければならない。
    ///   TradingView ティッカー文字列 (例: "NK225M1!") を渡すと kabu API が
    ///   HTTP 400 + Code=4002001 "銘柄が見つからない" で拒否する。
    ///   発注フローの上流 (SignalHandler) で payload.SymbolTicker を IAutoTradeInstrumentProvider
    ///   の ResolvedSymbolCode で置き換えてからここに辿り着く設計。詳細: docs/architecture.md §3.5。
    /// </summary>
    /// <param name="req">新規発注リクエスト (ドメイン)。Symbol は kabu 数値コード必須。</param>
    /// <param name="orderPassword">kabu 注文パスワード (API キーとは別の専用パスワード)。</param>
    /// <param name="exchange">市場コード (23=日中、24=夜間、2=日通し、他)。</param>
    /// <returns>kabu API へ POST する送信 DTO。</returns>
    public static KabuSendOrderRequest ToKabuRequest(OrderRequest req, string orderPassword, int exchange)
    {
        return new KabuSendOrderRequest
        {
            Password = orderPassword,
            Symbol = req.Symbol.Value,
            Exchange = exchange,
            TradeType = 1,   // 新規
            TimeInForce = ToKabuTif(req.TimeInForce),
            Side = req.Side.ToKabuCode().ToString(),
            Qty = req.Quantity.Value,
            ClosePositions = null,
            FrontOrderType = ToKabuFrontOrderType(req.OrderType),
            Price = (double)req.LimitPrice.Value,
            ExpireDay = 0,
            ReverseLimitOrder = req.OrderType == OrderType.Stop
                ? BuildReverseLimitOrder(req.Side, req.StopPrice)
                : null
        };
    }

    /// <summary>
    /// 返済発注 Domain リクエストを kabu /sendorder/future 用 DTO に変換する。
    /// TradeType=2 (返済) + ClosePositions[] (HoldID 指定) で kabu に送る。
    /// Side は <c>OriginalSide.Opposite()</c> で建玉サイドの反対が自動的に入る。
    /// </summary>
    /// <param name="req">返済発注リクエスト (ドメイン)。</param>
    /// <param name="orderPassword">kabu 注文パスワード。</param>
    /// <param name="exchange">市場コード。</param>
    /// <returns>kabu API へ POST する返済発注 DTO。</returns>
    public static KabuSendOrderRequest ToKabuRequest(ClosePositionRequest req, string orderPassword, int exchange)
    {
        // 返済の Side は建玉サイドの反対 (Long建玉 → Sell返済、Short建玉 → Buy返済)
        var exitSide = req.OriginalSide.Opposite();
        return new KabuSendOrderRequest
        {
            Password = orderPassword,
            Symbol = req.Symbol.Value,
            Exchange = exchange,
            TradeType = 2,   // 返済
            TimeInForce = ToKabuTif(req.TimeInForce),
            Side = exitSide.ToKabuCode().ToString(),
            Qty = req.Quantity.Value,
            ClosePositions = new[]
            {
                new ClosePosition
                {
                    HoldID = req.TargetExecutionId.Value,
                    Qty = req.Quantity.Value
                }
            },
            FrontOrderType = ToKabuFrontOrderType(req.OrderType),
            Price = (double)req.LimitPrice.Value,
            ExpireDay = 0,
            ReverseLimitOrder = req.OrderType == OrderType.Stop
                ? BuildReverseLimitOrder(exitSide, req.StopPrice)
                : null
        };
    }

    /// <summary>
    /// 逆指値ブロックを組み立てる (新規・返済共通)。
    /// UnderOver は発注 Side から自動決定:
    /// - Buy (買い逆指値) → 2 (以上、価格上昇でトリガ)
    /// - Sell (売り逆指値) → 1 (以下、価格下落でトリガ)
    /// AfterHitOrderType は成行 (1) 固定。逆指値+指値が必要になったら呼び出し側を拡張する。
    /// </summary>
    private static ReverseLimitOrder BuildReverseLimitOrder(Side side, Price triggerPrice)
    {
        return new ReverseLimitOrder
        {
            TriggerPrice = (double)triggerPrice.Value,
            UnderOver = side == Side.Buy ? 2 : 1,
            AfterHitOrderType = 1,   // 成行
            AfterHitPrice = 0         // 成行時は 0
        };
    }

    private static int ToKabuTif(TimeInForce tif) => tif switch
    {
        TimeInForce.FAS => 1,
        TimeInForce.FAK => 2,
        TimeInForce.FOK => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(tif))
    };

    /// <summary>
    /// kabu API /sendorder/future の FrontOrderType (先物用)。
    /// 旧 N225OrderBridge の N225.Domain.CommonConst.FrontOrderType と同じ値:
    ///   Market = 120 / BestMarket = 20 / Limit = 20 / Stop = 30
    /// 注: 現物用 (10, 13, 18, 27 等) とは異なる値。先物専用エンドポイントなので先物表を使う。
    /// 私が当初現物用を入れていたため kabu API が「パラメータ不正:FrontOrderType」を返した (2026-05-18 修正)。
    /// </summary>
    private static int ToKabuFrontOrderType(OrderType type) => type switch
    {
        OrderType.Market => 120,
        OrderType.BestMarket => 20,
        OrderType.Limit => 20,
        OrderType.Stop => 30,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    // ── kabu DTO → Domain ────────────────────────────────────────

    /// <summary>
    /// kabu /sendorder の応答を Domain の <see cref="OrderResult"/> に変換する。
    /// Code が null かつ OrderId が空でない場合のみ Accepted、それ以外は Rejected として扱う。
    /// </summary>
    /// <param name="dto">kabu の応答 DTO。</param>
    /// <param name="correlationId">送信時の相関 Id (Order 集約 Id)。</param>
    /// <returns>Domain の発注結果。</returns>
    public static OrderResult ToOrderResult(KabuSendOrderResponse dto, Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(dto);

        // Result=0 は kabu の正常応答。それ以外はエラー扱い (旧実装の DynamicJson "Code" 検査相当)
        if (dto.Code is null && !string.IsNullOrEmpty(dto.OrderId))
        {
            return new OrderResult(
                correlationId,
                OrderResultStatus.Accepted,
                new OrderId(dto.OrderId),
                ErrorCode: null,
                ErrorMessage: null,
                ReceivedAt: DateTime.UtcNow);
        }

        return new OrderResult(
            correlationId,
            OrderResultStatus.Rejected,
            BrokerOrderId: null,
            ErrorCode: dto.Code?.ToString(),
            ErrorMessage: dto.Message,
            ReceivedAt: DateTime.UtcNow);
    }

    /// <summary>
    /// kabu /positions の 1 件 DTO を Domain の <see cref="PositionSnapshot"/> に変換する。
    /// </summary>
    /// <param name="dto">kabu の建玉 DTO。ExecutionID 必須。</param>
    /// <param name="broker">紐付けるブローカーコード。</param>
    /// <returns>Domain の建玉スナップショット。</returns>
    /// <exception cref="InvalidOperationException">ExecutionID が空の場合。</exception>
    public static PositionSnapshot ToPositionSnapshot(KabuPositionDto dto, BrokerCode broker)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrEmpty(dto.ExecutionID))
            throw new InvalidOperationException("KabuPositionDto.ExecutionID is required.");

        return new PositionSnapshot(
            BrokerCode: broker,
            PositionId: new ExecutionId(dto.ExecutionID),
            Symbol: new SymbolCode(dto.Symbol ?? string.Empty),
            Side: ParseSide(dto.Side),
            LeaveQuantity: new Quantity((int)Math.Round(dto.LeavesQty)),
            HoldQuantity: new Quantity((int)Math.Round(dto.HoldQty)),
            EntryPrice: new Price((decimal)dto.Price),
            OpenedAt: ParseExecutionDay(dto.ExecutionDay));
    }

    /// <summary>
    /// kabu /orders の 1 件 DTO を Domain の <see cref="OrderSnapshot"/> に変換する。
    /// Details[*] に約定明細 (RecType=8) があれば加重平均約定価格を計算し Price に入れる。
    /// 約定無しの場合は注文時の指値 (dto.Price) を使う。
    /// </summary>
    /// <param name="dto">kabu の注文 DTO。ID 必須。</param>
    /// <param name="broker">紐付けるブローカーコード。</param>
    /// <returns>Domain の注文スナップショット。</returns>
    /// <exception cref="InvalidOperationException">ID が空の場合。</exception>
    public static OrderSnapshot ToOrderSnapshot(KabuOrderDto dto, BrokerCode broker)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrEmpty(dto.ID))
            throw new InvalidOperationException("KabuOrderDto.ID is required.");

        // 約定済 (Details[*] に RecType=8) があれば加重平均約定価格、無ければ注文価格 (dto.Price)。
        decimal effectivePrice = (decimal)dto.Price;
        if (dto.Details is { Length: > 0 })
        {
            double cumPrice = 0, cumQty = 0;
            foreach (var d in dto.Details)
            {
                if (d.RecType != 8) continue;   // 約定明細だけ集計
                if (d.Price is not double dp || d.Qty is not double dq) continue;
                cumPrice += dp * dq;
                cumQty += dq;
            }
            if (cumQty > 0)
                effectivePrice = (decimal)(cumPrice / cumQty);
        }

        return new OrderSnapshot(
            BrokerCode: broker,
            BrokerOrderId: new OrderId(dto.ID),
            State: MapState(dto.State, dto.OrderQty, dto.CumQty),
            Symbol: new SymbolCode(dto.Symbol ?? string.Empty),
            Side: ParseSide(dto.Side),
            TradeType: dto.CashMargin == 3 ? TradeType.ExitOrder : TradeType.NewOrder,
            RequestedQuantity: new Quantity((int)Math.Round(dto.OrderQty)),
            ExecutedQuantity: new Quantity((int)Math.Round(dto.CumQty)),
            Price: new Price(effectivePrice),
            CreatedAt: ParseIsoOrDefault(dto.RecvTime));
    }

    /// <summary>
    /// kabu /board の DTO を Domain の <see cref="QuoteSnapshot"/> に変換する。
    /// kabu の BidPrice / AskPrice は通常用語と逆の命名 (BidPrice=売り板=通常 ASK)。
    /// この変換ではフィールド値をそのまま保持し、利用側 (発注ロジック) で正しく解釈する。
    /// </summary>
    /// <param name="dto">kabu の板情報 DTO。</param>
    /// <param name="broker">紐付けるブローカーコード。</param>
    /// <returns>Domain の気配スナップショット。</returns>
    public static QuoteSnapshot ToQuoteSnapshot(KabuBoardDto dto, BrokerCode broker)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new QuoteSnapshot(
            BrokerCode: broker,
            Symbol: new SymbolCode(dto.Symbol ?? string.Empty),
            LastPrice: new Price((decimal)dto.CurrentPrice),
            BidPrice: new Price((decimal)dto.BidPrice),
            AskPrice: new Price((decimal)dto.AskPrice),
            BidQuantity: new Quantity((int)Math.Round(dto.BidQty)),
            AskQuantity: new Quantity((int)Math.Round(dto.AskQty)),
            At: ParseIsoOrDefault(dto.CurrentPriceTime));
    }

    // ── 細部 ────────────────────────────────────────────────────

    private static Side ParseSide(string? rawSide) => rawSide switch
    {
        "2" => Side.Buy,
        "1" => Side.Sell,
        _ => throw new InvalidOperationException($"Unknown kabu Side: '{rawSide}'")
    };

    private static OrderState MapState(int kabuState, double orderQty, double cumQty)
    {
        // kabu State 定義:
        //   1=待機（発注待機）, 2=処理中（発注送信中）, 3=処理済（発注済・訂正済）,
        //   4=訂正取消送信中,  5=終了（発注エラー・取消済・全約定・失効・期限切れ）
        //
        // 注意点:
        // - State=3 で cumQty=0 のときは「板に乗って約定待ち」= Submitted。
        //   cumQty 無視で PartiallyFilled に決めつけてはいけない。
        // - State=5 になると kabu は **OrderQty も 0 に書き換える** (キャンセル時に確認)。
        //   そのため (cumQty >= orderQty) で Filled/Cancelled を判定すると、
        //   未約定キャンセルが 0>=0 で誤って Filled に分類される。
        //   終了状態の Filled/Cancelled は **cumQty 単独で判定する**。
        return kabuState switch
        {
            1 or 2 => OrderState.Submitted,
            3 => cumQty < 0.001
                ? OrderState.Submitted
                : Math.Abs(orderQty - cumQty) < 0.001
                    ? OrderState.Filled
                    : OrderState.PartiallyFilled,
            4 => OrderState.Cancelled,
            5 => cumQty < 0.001 ? OrderState.Cancelled : OrderState.Filled,
            _ => OrderState.Submitted
        };
    }

    private static DateTime ParseIsoOrDefault(string? raw)
    {
        if (DateTime.TryParse(raw, out var dt))
            return dt.ToUniversalTime();
        return DateTime.UtcNow;
    }

    private static DateTime ParseExecutionDay(int yyyyMMdd)
    {
        if (yyyyMMdd <= 0) return DateTime.UtcNow;
        var s = yyyyMMdd.ToString();
        if (s.Length != 8) return DateTime.UtcNow;
        if (DateTime.TryParseExact(s, "yyyyMMdd", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            return dt.ToUniversalTime();
        return DateTime.UtcNow;
    }
}
