using PokeTokenBar.Core;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class RateLimitStoreTests
{
    [Fact]
    public void Codex_parser_maps_windows_and_deduplicates_the_primary_bucket()
    {
        const string json = """
            {
              "rateLimits": {
                "limitId": "codex",
                "limitName": "codex",
                "primary": { "usedPercent": 37, "windowDurationMins": 300, "resetsAt": 1893456000 },
                "secondary": { "usedPercent": 61, "windowDurationMins": 10080, "resetsAt": 1894060800 },
                "planType": "pro"
              },
              "rateLimitsByLimitId": {
                "codex": {
                  "limitId": "codex",
                  "primary": { "usedPercent": 37, "windowDurationMins": 300, "resetsAt": 1893456000 }
                },
                "codex_other": {
                  "limitId": "codex_other",
                  "limitName": "codex_other",
                  "primary": { "usedPercent": 12, "windowDurationMins": 300, "resetsAt": 1893459600 }
                }
              }
            }
            """;

        var fetchedAt = new DateTimeOffset(2026, 9, 8, 6, 0, 0, TimeSpan.Zero);
        var parsed = CodexRateLimitParser.Parse(json, fetchedAt);
        Assert.NotNull(parsed);
        var snapshot = parsed!;

        Assert.Equal("codex", snapshot.ProviderId);
        Assert.Equal("pro", snapshot.PlanType);
        Assert.Equal(fetchedAt, snapshot.FetchedAt);
        Assert.Equal(3, snapshot.Windows.Count);
        Assert.Equal("5시간 세션", snapshot.Windows[0].DisplayName);
        Assert.Equal(37, snapshot.Windows[0].UsedPercent);
        Assert.Equal(61, snapshot.Windows[1].UsedPercent);
        Assert.Equal("codex_other:primary", snapshot.Windows[2].Id);
        Assert.Equal(12, snapshot.Windows[2].UsedPercent);
        Assert.Equal(61, snapshot.MaxUsedPercent);
    }

    [Fact]
    public async Task Refresh_failure_preserves_the_last_successful_snapshot()
    {
        var provider = new FakeRateLimitProvider(
            new ProviderRateLimitSnapshot(
                "codex",
                "Codex",
                "pro",
                [new RateLimitWindow(
                    "codex:primary",
                    "5시간 세션",
                    42,
                    DateTimeOffset.UtcNow.AddHours(2),
                    300)],
                DateTimeOffset.UtcNow));
        using var store = new RateLimitStore([provider]);

        await store.RefreshAsync();
        provider.ThrowOnNextFetch = true;
        await store.RefreshAsync();

        var preserved = Assert.Single(store.Snapshots);
        Assert.Equal(42, preserved.Windows[0].UsedPercent);
        Assert.Contains("codex", store.LastErrorDescription);
    }

    private sealed class FakeRateLimitProvider(ProviderRateLimitSnapshot snapshot)
        : IRateLimitProvider
    {
        public bool ThrowOnNextFetch { get; set; }

        public string Id => "codex";

        public string DisplayName => "Codex";

        public Task<ProviderRateLimitSnapshot?> FetchAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnNextFetch)
            {
                ThrowOnNextFetch = false;
                throw new InvalidOperationException("fixture failure");
            }

            return Task.FromResult<ProviderRateLimitSnapshot?>(snapshot with { FetchedAt = now });
        }
    }
}
