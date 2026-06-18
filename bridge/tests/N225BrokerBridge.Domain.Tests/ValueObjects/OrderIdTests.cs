using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;
using Xunit;

namespace N225BrokerBridge.Domain.Tests.ValueObjects;

public class OrderIdTests
{
    [Fact]
    public void Construct_NonEmpty_Succeeds()
    {
        var id = new OrderId("20260517A11N03012345");
        Assert.Equal("20260517A11N03012345", id.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Construct_EmptyOrWhitespace_Throws(string? value)
    {
        Assert.Throws<InvalidValueObjectException>(() => new OrderId(value!));
    }

    [Fact]
    public void Equality_SameValue_Equal()
    {
        Assert.Equal(new OrderId("X"), new OrderId("X"));
    }
}
