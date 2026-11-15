using Shiftwall.Core.Data;

namespace Shiftwall.Core.Tests.Data;

public sealed class FortiOsVersionTests
{
    [Theory]
    [InlineData("7.0.10", "7.0.9")]
    [InlineData("7.2.0", "7.0.19")]
    [InlineData("8.0.0", "7.6.8")]
    public void Ordering_IsNumeric(string higher, string lower) =>
        Assert.True(FortiOsVersion.Parse(higher) > FortiOsVersion.Parse(lower));

    [Theory]
    [InlineData("7.6")]
    [InlineData("v7.6.1")]
    [InlineData("7.6.x")]
    [InlineData("7.6.1.2")]
    [InlineData("-7.6.1")]
    [InlineData(null)]
    public void InvalidText_IsRejected(string? text) => Assert.False(FortiOsVersion.TryParse(text, out _));

    [Fact]
    public void Branch_IsMajorMinor() => Assert.Equal("7.4", FortiOsVersion.Parse("7.4.13").Branch);
}
