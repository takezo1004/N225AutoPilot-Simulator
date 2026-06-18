using N225BrokerBridge.Domain.ValueObjects;
using Xunit;

namespace N225BrokerBridge.Domain.Tests.ValueObjects;

public class SideTests
{
    [Fact]
    public void Opposite_Buy_ReturnsSell()
    {
        Assert.Equal(Side.Sell, Side.Buy.Opposite());
    }

    [Fact]
    public void Opposite_Sell_ReturnsBuy()
    {
        Assert.Equal(Side.Buy, Side.Sell.Opposite());
    }

    [Fact]
    public void ToDisplay_ReturnsJapanese()
    {
        Assert.Equal("買", Side.Buy.ToDisplay());
        Assert.Equal("売", Side.Sell.ToDisplay());
    }

    [Fact]
    public void ToKabuCode_ReturnsExpectedInts()
    {
        // kabu API: 1=売, 2=買 (Side フィールド)
        Assert.Equal(2, Side.Buy.ToKabuCode());
        Assert.Equal(1, Side.Sell.ToKabuCode());
    }
}
