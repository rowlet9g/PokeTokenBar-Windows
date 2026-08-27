using System.Globalization;

namespace PokeTokenBar.Core.Tests;

public sealed class TokenFormatterTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(987, "987")]
    [InlineData(12_345, "12.3K")]
    [InlineData(190_612_940, "190.6M")]
    [InlineData(1_240_000_000, "1.24B")]
    [InlineData(1_000_000, "1M")]
    [InlineData(-12_345, "-12.3K")]
    public void Compact_matches_the_Swift_contract(long value, string expected)
    {
        Assert.Equal(expected, TokenFormatter.Compact(value));
    }

    [Fact]
    public void Detailed_formats_match_the_Swift_contract()
    {
        Assert.Equal("253,412,890", TokenFormatter.Grouped(253_412_890, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("253.412.890", TokenFormatter.Grouped(253_412_890, CultureInfo.GetCultureInfo("es-ES")));
        Assert.Equal("$48.10", TokenFormatter.Cost(48.104));
        Assert.Equal("$9.5", TokenFormatter.CostCompact(9.54));
        Assert.Equal("$311", TokenFormatter.CostCompact(311.4));
        Assert.Equal("$12.3K", TokenFormatter.CostCompact(12_340));
        Assert.Equal("88%", TokenFormatter.Percent(88));
        Assert.Equal("88.3%", TokenFormatter.Percent(88.35));
    }
}
