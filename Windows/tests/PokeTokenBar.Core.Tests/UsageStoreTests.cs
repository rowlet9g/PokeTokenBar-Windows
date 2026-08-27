namespace PokeTokenBar.Core.Tests;

public sealed class UsageStoreTests
{
    [Fact]
    public async Task Refresh_aggregates_provider_snapshots()
    {
        var now = DateTimeOffset.Now;
        using var store = new UsageStore(
        [
            new FakeProvider("codex", 12_345, 50_000, 200_000),
            new FakeProvider("claude", 7_655, 25_000, 100_000),
        ]);

        await store.RefreshAsync();

        Assert.Equal(20_000, store.TodayTotalTokens);
        Assert.Equal(75_000, store.WeekTotalTokens);
        Assert.Equal(300_000, store.MonthTotalTokens);
        Assert.Equal(2, store.Snapshots.Count);
        Assert.NotNull(store.LastUpdated);
        Assert.Null(store.LastErrorDescription);
        Assert.False(store.IsRefreshing);
    }

    [Fact]
    public async Task Failed_refresh_preserves_the_previous_snapshot()
    {
        var provider = new MutableProvider();
        using var store = new UsageStore([provider]);
        await store.RefreshAsync();
        provider.ShouldFail = true;

        await store.RefreshAsync();

        Assert.Equal(123, store.TodayTotalTokens);
        Assert.Contains("codex:", store.LastErrorDescription);
    }

    private sealed class FakeProvider(
        string id,
        long today,
        long week,
        long month) : IUsageProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public bool ReportsCost => false;

        public Task<ProviderSnapshot?> FetchAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderSnapshot?>(Snapshot(Id, today, week, month, now));
    }

    private sealed class MutableProvider : IUsageProvider
    {
        public string Id => "codex";
        public string DisplayName => "Codex";
        public bool ReportsCost => false;
        public bool ShouldFail { get; set; }

        public Task<ProviderSnapshot?> FetchAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                throw new IOException("synthetic failure");
            }

            return Task.FromResult<ProviderSnapshot?>(Snapshot(Id, 123, 456, 789, now));
        }
    }

    private static ProviderSnapshot Snapshot(
        string id,
        long today,
        long week,
        long month,
        DateTimeOffset now) =>
        new(
            id,
            id,
            new DailyUsage("2026-08-27", 0, today, 0, 0, today, 0),
            null,
            new PeriodUsage("2026-08-24", week, 0),
            new PeriodUsage("2026-08", month, 0),
            now,
            false);
}
