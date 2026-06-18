using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.ValueObjects;
using N225BrokerBridge.Infrastructure.Brokers.Kabu;
using N225BrokerBridge.Infrastructure.Brokers.Kabu.Dto;
using Xunit;

namespace N225BrokerBridge.Infrastructure.Tests.Brokers.Kabu;

public class KabuMappersTests
{
    // ── Domain → kabu (新規注文) ─────────────────────────────────

    [Fact]
    public void ToKabuRequest_NewOrder_BuildsCorrectShape()
    {
        var req = new OrderRequest(
            CorrelationId: Guid.NewGuid(),
            Strategy: new StrategyName("V7-7"),
            Interval: 5,
            TradeMode: TradeMode.Auto,
            Symbol: new SymbolCode("167060019"),
            Side: Side.Buy,
            OrderType: OrderType.BestMarket,
            TimeInForce: TimeInForce.FAS,
            Quantity: new Quantity(3),
            LimitPrice: Price.Zero,
            StopPrice: Price.Zero);

        var kabu = KabuMappers.ToKabuRequest(req, orderPassword: "pwd", exchange: 23);

        Assert.Equal("pwd", kabu.Password);
        Assert.Equal("167060019", kabu.Symbol);
        Assert.Equal(23, kabu.Exchange);
        Assert.Equal(1, kabu.TradeType);            // 1=新規
        Assert.Equal(1, kabu.TimeInForce);          // 1=FAS
        Assert.Equal("2", kabu.Side);               // kabu 仕様: 1=売 / 2=買
        Assert.Equal(3, kabu.Qty);
        Assert.Null(kabu.ClosePositions);
        Assert.Equal(20, kabu.FrontOrderType);      // BestMarket (先物用)
        Assert.Equal(0.0, kabu.Price);
    }

    // ── Domain → kabu (返済注文) ─────────────────────────────────

    [Fact]
    public void ToKabuRequest_Close_BuildsClosePositions()
    {
        var req = new ClosePositionRequest(
            CorrelationId: Guid.NewGuid(),
            Strategy: new StrategyName("V7-7"),
            Interval: 5,
            TradeMode: TradeMode.Auto,
            Symbol: new SymbolCode("167060019"),
            OriginalSide: Side.Buy,                 // 建玉 Buy → 返済発注 Sell
            TargetExecutionId: new ExecutionId("HOLD-001"),
            Quantity: new Quantity(2),
            OrderType: OrderType.BestMarket,
            TimeInForce: TimeInForce.FAS,
            LimitPrice: Price.Zero,
            StopPrice: Price.Zero);

        var kabu = KabuMappers.ToKabuRequest(req, orderPassword: "pwd", exchange: 23);

        Assert.Equal(2, kabu.TradeType);            // 2=返済
        Assert.Equal("1", kabu.Side);               // Buy 建玉の返済 → Sell → kabu "1"
        Assert.Equal(2, kabu.Qty);
        Assert.NotNull(kabu.ClosePositions);
        Assert.Single(kabu.ClosePositions!);
        Assert.Equal("HOLD-001", kabu.ClosePositions![0].HoldID);
        Assert.Equal(2, kabu.ClosePositions[0].Qty);
    }

    // ── kabu Response → OrderResult ──────────────────────────────

    [Fact]
    public void ToOrderResult_SuccessResponse_IsAccepted()
    {
        var dto = new KabuSendOrderResponse { Result = 0, OrderId = "BO-001", Code = null };
        var corr = Guid.NewGuid();
        var result = KabuMappers.ToOrderResult(dto, corr);

        Assert.Equal(OrderResultStatus.Accepted, result.Status);
        Assert.Equal(corr, result.CorrelationId);
        Assert.Equal(new OrderId("BO-001"), result.BrokerOrderId);
    }

    [Fact]
    public void ToOrderResult_WithErrorCode_IsRejected()
    {
        var dto = new KabuSendOrderResponse
        {
            Code = 4001012,
            Message = "Margin insufficient"
        };
        var result = KabuMappers.ToOrderResult(dto, Guid.NewGuid());

        Assert.Equal(OrderResultStatus.Rejected, result.Status);
        Assert.Null(result.BrokerOrderId);
        Assert.Equal("4001012", result.ErrorCode);
        Assert.Equal("Margin insufficient", result.ErrorMessage);
    }

    // ── kabu Position → PositionSnapshot ─────────────────────────

    [Fact]
    public void ToPositionSnapshot_BuyPosition_Succeeds()
    {
        var dto = new KabuPositionDto
        {
            ExecutionID = "E001",
            Symbol = "167060019",
            Side = "2",                 // 買建
            LeavesQty = 3,
            HoldQty = 1,
            Price = 38000.0,
            ExecutionDay = 20260517
        };

        var snap = KabuMappers.ToPositionSnapshot(dto, BrokerCode.Kabu);

        Assert.Equal(BrokerCode.Kabu, snap.BrokerCode);
        Assert.Equal(new ExecutionId("E001"), snap.PositionId);
        Assert.Equal(Side.Buy, snap.Side);
        Assert.Equal(new Quantity(3), snap.LeaveQuantity);
        Assert.Equal(new Quantity(1), snap.HoldQuantity);
        Assert.Equal(new Price(38000m), snap.EntryPrice);
    }

    // ── kabu Order → OrderSnapshot ───────────────────────────────

    [Fact]
    public void ToOrderSnapshot_FilledOrder_ReturnsFilledState()
    {
        var dto = new KabuOrderDto
        {
            ID = "BO-001",
            State = 3,            // 処理済
            Symbol = "167060019",
            Side = "1",           // 売
            CashMargin = 3,       // 返済
            OrderQty = 1,
            CumQty = 1
        };
        var snap = KabuMappers.ToOrderSnapshot(dto, BrokerCode.Kabu);

        Assert.Equal(OrderState.Filled, snap.State);
        Assert.Equal(Side.Sell, snap.Side);
        Assert.Equal(TradeType.ExitOrder, snap.TradeType);
        Assert.Equal(new Quantity(1), snap.ExecutedQuantity);
    }

    [Fact]
    public void ToOrderSnapshot_PartiallyFilled_ReturnsPartialState()
    {
        var dto = new KabuOrderDto
        {
            ID = "BO-002",
            State = 3,
            Symbol = "S",
            Side = "2",
            CashMargin = 2,
            OrderQty = 3,
            CumQty = 1
        };
        var snap = KabuMappers.ToOrderSnapshot(dto, BrokerCode.Kabu);
        Assert.Equal(OrderState.PartiallyFilled, snap.State);
    }

    // ── kabu Board → QuoteSnapshot ───────────────────────────────

    [Fact]
    public void ToQuoteSnapshot_Succeeds()
    {
        var dto = new KabuBoardDto
        {
            Symbol = "167060019",
            CurrentPrice = 38050.0,
            BidPrice = 38040.0,
            BidQty = 10,
            AskPrice = 38060.0,
            AskQty = 8,
            CurrentPriceTime = "2026-05-17T09:00:00+09:00"
        };
        var snap = KabuMappers.ToQuoteSnapshot(dto, BrokerCode.Kabu);

        Assert.Equal(new Price(38050m), snap.LastPrice);
        Assert.Equal(new Price(38040m), snap.BidPrice);
        Assert.Equal(new Price(38060m), snap.AskPrice);
        Assert.Equal(new Quantity(10), snap.BidQuantity);
        Assert.Equal(new Quantity(8), snap.AskQuantity);
    }

    // ── TimeInForce / OrderType マッピング ──────────────────────

    [Theory]
    [InlineData(TimeInForce.FAS, 1)]
    [InlineData(TimeInForce.FAK, 2)]
    [InlineData(TimeInForce.FOK, 3)]
    public void TifMapping_IsCorrect(TimeInForce tif, int expected)
    {
        var req = new OrderRequest(Guid.NewGuid(), new StrategyName("X"), 5, TradeMode.Auto,
            new SymbolCode("S"), Side.Buy, OrderType.Market, tif,
            new Quantity(1), Price.Zero, Price.Zero);
        var kabu = KabuMappers.ToKabuRequest(req, "p", 23);
        Assert.Equal(expected, kabu.TimeInForce);
    }

    [Theory]
    [InlineData(OrderType.Market, 120)]    // 先物 成行
    [InlineData(OrderType.BestMarket, 20)] // 先物 通常注文 (最良気配)
    [InlineData(OrderType.Limit, 20)]      // 先物 指値 (通常注文)
    [InlineData(OrderType.Stop, 30)]       // 先物 逆指値
    public void OrderTypeMapping_IsCorrect(OrderType type, int expected)
    {
        var req = new OrderRequest(Guid.NewGuid(), new StrategyName("X"), 5, TradeMode.Auto,
            new SymbolCode("S"), Side.Buy, type, TimeInForce.FAS,
            new Quantity(1), Price.Zero, Price.Zero);
        var kabu = KabuMappers.ToKabuRequest(req, "p", 23);
        Assert.Equal(expected, kabu.FrontOrderType);
    }
}
