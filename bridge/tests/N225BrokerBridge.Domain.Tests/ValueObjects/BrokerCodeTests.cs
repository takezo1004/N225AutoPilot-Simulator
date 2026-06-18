using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;
using Xunit;

namespace N225BrokerBridge.Domain.Tests.ValueObjects;

public class BrokerCodeTests
{
    [Fact]
    public void Kabu_IsPredefined()
    {
        Assert.Equal("kabu", BrokerCode.Kabu.Value);
    }

    [Fact]
    public void Rakuten_IsPredefined()
    {
        Assert.Equal("rakuten", BrokerCode.Rakuten.Value);
    }

    [Fact]
    public void Of_CustomCode_Succeeds()
    {
        var custom = BrokerCode.Of("sbi");
        Assert.Equal("sbi", custom.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Of_EmptyOrWhitespace_Throws(string? value)
    {
        Assert.Throws<InvalidValueObjectException>(() => BrokerCode.Of(value!));
    }

    [Fact]
    public void Equality_PredefinedSameInstance_Equal()
    {
        Assert.Equal(BrokerCode.Kabu, BrokerCode.Of("kabu"));
    }
}
