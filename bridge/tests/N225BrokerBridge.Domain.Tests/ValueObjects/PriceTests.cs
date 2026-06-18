using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;
using Xunit;

namespace N225BrokerBridge.Domain.Tests.ValueObjects;

public class PriceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1.0)]
    [InlineData(38000)]
    [InlineData(38000.5)]
    public void Construct_NonNegative_Succeeds(decimal value)
    {
        var price = new Price(value);
        Assert.Equal(value, price.Value);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1000)]
    public void Construct_Negative_Throws(decimal value)
    {
        Assert.Throws<InvalidValueObjectException>(() => new Price(value));
    }

    [Fact]
    public void Equality_SameValue_Equal()
    {
        Assert.Equal(new Price(38000m), new Price(38000m));
    }

    [Fact]
    public void Addition_Works()
    {
        Assert.Equal(new Price(38100m), new Price(38000m) + new Price(100m));
    }

    [Fact]
    public void Subtraction_Works()
    {
        Assert.Equal(new Price(100m), new Price(38100m) - new Price(38000m));
    }

    [Fact]
    public void Subtraction_ToNegative_Throws()
    {
        Assert.Throws<InvalidValueObjectException>(() => new Price(100m) - new Price(200m));
    }
}
