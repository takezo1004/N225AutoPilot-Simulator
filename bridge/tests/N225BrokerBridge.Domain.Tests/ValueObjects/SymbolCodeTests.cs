using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.ValueObjects;
using Xunit;

namespace N225BrokerBridge.Domain.Tests.ValueObjects;

public class SymbolCodeTests
{
    [Theory]
    [InlineData("167060019")]      // kabu: 日経225ミニ
    [InlineData("OSE:NK225M1!")]   // TV: 日経225ミニ連続
    [InlineData("7203")]           // 株式コード例
    public void Construct_NonEmpty_Succeeds(string code)
    {
        var symbol = new SymbolCode(code);
        Assert.Equal(code, symbol.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(null)]
    public void Construct_EmptyOrWhitespace_Throws(string? code)
    {
        Assert.Throws<InvalidValueObjectException>(() => new SymbolCode(code!));
    }

    [Fact]
    public void Equality_SameValue_Equal()
    {
        Assert.Equal(new SymbolCode("167060019"), new SymbolCode("167060019"));
    }

    [Fact]
    public void Equality_DifferentValue_NotEqual()
    {
        Assert.NotEqual(new SymbolCode("167060019"), new SymbolCode("OSE:NK225M1!"));
    }
}
