using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;
using Xunit;

namespace N225BrokerBridge.Domain.Tests.ValueObjects;

public class QuantityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public void Construct_NonNegative_Succeeds(int value)
    {
        var qty = new Quantity(value);
        Assert.Equal(value, qty.Value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Construct_Negative_Throws(int value)
    {
        Assert.Throws<InvalidValueObjectException>(() => new Quantity(value));
    }

    [Fact]
    public void Zero_IsZero()
    {
        Assert.True(Quantity.Zero.IsZero);
        Assert.False(Quantity.Zero.IsPositive);
    }

    [Fact]
    public void Equality_SameValue_Equal()
    {
        Assert.Equal(new Quantity(3), new Quantity(3));
    }

    [Fact]
    public void Addition_Works()
    {
        Assert.Equal(new Quantity(5), new Quantity(2) + new Quantity(3));
    }

    [Fact]
    public void Subtraction_Works()
    {
        Assert.Equal(new Quantity(1), new Quantity(3) - new Quantity(2));
    }

    [Fact]
    public void Subtraction_ToNegative_Throws()
    {
        Assert.Throws<InvalidValueObjectException>(() => new Quantity(2) - new Quantity(3));
    }

    [Fact]
    public void Comparison_Works()
    {
        Assert.True(new Quantity(2) < new Quantity(3));
        Assert.True(new Quantity(3) > new Quantity(2));
        Assert.True(new Quantity(3) <= new Quantity(3));
        Assert.True(new Quantity(3) >= new Quantity(3));
    }

    [Fact]
    public void Min_ReturnsSmaller()
    {
        // 跨ぎ消化計算で多用するシナリオ:
        // 残要求 = 1、建玉残数量 = 2 → このループで消化するのは min(1, 2) = 1
        var remaining = new Quantity(1);
        var leaveQty = new Quantity(2);
        Assert.Equal(new Quantity(1), Quantity.Min(remaining, leaveQty));

        // 残要求 = 3、建玉残数量 = 2 → このループで消化するのは min(3, 2) = 2 (建玉全消化)
        Assert.Equal(new Quantity(2), Quantity.Min(new Quantity(3), new Quantity(2)));
    }
}
