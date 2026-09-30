using PokeTokenBar.Core;

namespace PokeTokenBar.Core.Tests;

public sealed class ModelUsageTests
{
    private static readonly DateOnly Today = new(2026, 9, 30);
    private static UsageEntry Entry(string id, string model, DateOnly day, long input, long output,
        long write = 0, long read = 0) => new(id, DateTimeOffset.Now, day, model, input, output, write, read);

    [Fact]
    public void Daily_groups_real_models_including_cache_and_sorts_largest_first()
    {
        var daily = UsageAggregation.Daily([
            Entry("a", "sonnet", Today, 10, 20, 30, 40),
            Entry("b", "sonnet", Today, 20, 10),
            Entry("c", "opus", Today, 200, 1),
            Entry("d", "opus", Today.AddDays(-1), 999, 999),
        ], Today)!;
        Assert.Equal(331, daily.TotalTokens);
        Assert.Equal(new[] { "opus", "sonnet" }, daily.Models!.Select(model => model.Model));
        Assert.Equal(daily.TotalTokens, daily.Models!.Sum(model => model.TotalTokens));
        var sonnet = daily.Models![1];
        Assert.Equal((30L, 30L, 30L, 40L),
            (sonnet.InputTokens, sonnet.OutputTokens, sonnet.CacheCreationTokens, sonnet.CacheReadTokens));
    }

    [Fact]
    public void Period_filters_dates_and_retains_unidentified_model_usage()
    {
        var period = UsageAggregation.Period([
            Entry("a", "", Today, 5, 5),
            Entry("b", "unknown", Today.AddDays(-1), 10, 10),
            Entry("c", "model", Today.AddDays(-8), 999, 999),
            Entry("d", "zero", Today, 0, 0),
        ], "week", Today.AddDays(-6), Today);
        Assert.Equal(30, period.TotalTokens);
        var model = Assert.Single(period.Models!);
        Assert.Equal("unknown", model.Model);
        Assert.Equal(30, model.TotalTokens);
    }

    [Fact]
    public void Saturation_and_single_pass_enumeration_preserve_totals()
    {
        var entries = new[] { Entry("a", "x", Today, long.MaxValue, 100) };
        var period = UsageAggregation.Period(entries, "month", Today, Today);
        Assert.Equal(long.MaxValue, period.TotalTokens);
        Assert.Equal(long.MaxValue, Assert.Single(period.Models!).TotalTokens);
    }
}
