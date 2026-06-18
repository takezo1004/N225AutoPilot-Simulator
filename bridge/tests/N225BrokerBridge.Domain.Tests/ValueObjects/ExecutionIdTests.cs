using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;
using Xunit;

namespace N225BrokerBridge.Domain.Tests.ValueObjects;

public class ExecutionIdTests
{
    [Fact]
    public void Construct_NonEmpty_Succeeds()
    {
        var id = new ExecutionId("E20260517001");
        Assert.Equal("E20260517001", id.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Construct_EmptyOrWhitespace_Throws(string? value)
    {
        Assert.Throws<InvalidValueObjectException>(() => new ExecutionId(value!));
    }

    [Fact]
    public void Equality_SameValue_Equal()
    {
        Assert.Equal(new ExecutionId("E001"), new ExecutionId("E001"));
    }

    [Fact]
    public void Equality_DifferentValue_NotEqual()
    {
        Assert.NotEqual(new ExecutionId("E001"), new ExecutionId("E002"));
    }
}
