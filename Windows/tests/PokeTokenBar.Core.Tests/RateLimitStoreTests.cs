using PokeTokenBar.Core;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class RateLimitStoreTests
{
    [Fact]
    public void Claude_credentials_read_nested_oauth_metadata_without_exposing_the_token()
    {
        const string json = """
            {
              "claudeAiOauth": {
                "accessToken": "oauth-secret-fixture",
                "expiresAt": 1893456000000,
                "subscriptionType": "max",
                "rateLimitTier": "default_claude_max_20x"
              }
            }
            """;

        var metadata = ClaudeRateLimitsProvider.ParseCredentialMetadata(json);

        Assert.True(metadata.HasAccessToken);
        Assert.Equal("max", metadata.SubscriptionType);
        Assert.Equal("default_claude_max_20x", metadata.RateLimitTier);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1893456000000), metadata.ExpiresAt);
        Assert.DoesNotContain("oauth-secret-fixture", metadata.ToString());
        Assert.False(ClaudeRateLimitsProvider.ParseCredentialMetadata(
            "{\"claudeAiOauth\":{\"accessToken\":\"\",\"expiresAt\":1893456000000}}").HasAccessToken);
    }

    [Fact]
    public void Claude_parser_maps_legacy_and_scoped_windows_without_duplicates()
    {
        const string json = """
            {
              "five_hour": { "utilization": 12.5, "resets_at": "2026-09-08T12:00:00Z" },
              "seven_day": { "utilization": 33.3, "resets_at": "2026-09-14T12:00:00Z" },
              "seven_day_opus": { "utilization": 44.4, "resets_at": "2026-09-14T12:00:00Z" },
              "limits": [
                { "kind": "session", "group": "all", "percent": 12.5, "resets_at": "2026-09-08T12:00:00Z" },
                { "kind": "weekly_all", "group": "all", "percent": 33.3, "resets_at": "2026-09-14T12:00:00Z" },
                { "kind": "weekly_scoped", "group": "sonnet", "percent": 55.5,
                  "resets_at": "2026-09-14T12:00:00Z", "scope": { "model": { "display_name": "Claude Sonnet 4" } } },
                { "kind": "weekly_scoped", "group": "opus", "percent": 99,
                  "resets_at": "2026-09-14T12:00:00Z", "is_active": false }
              ]
            }
            """;

        var fetchedAt = new DateTimeOffset(2026, 9, 8, 6, 0, 0, TimeSpan.Zero);
        var parsed = ClaudeRateLimitParser.Parse(json, fetchedAt, "max", "default_claude_max_20x");

        Assert.NotNull(parsed);
        Assert.Equal("Max 20x", parsed!.PlanType);
        Assert.Equal(fetchedAt, parsed.FetchedAt);
        Assert.Equal(4, parsed.Windows.Count);
        Assert.Equal("5시간 세션", parsed.Windows[0].DisplayName);
        Assert.Equal(12.5, parsed.Windows[0].UsedPercent);
        Assert.Equal("주간", parsed.Windows[1].DisplayName);
        Assert.Equal("주간 (Opus)", parsed.Windows[2].DisplayName);
        Assert.Equal("주간 (Claude Sonnet 4)", parsed.Windows[3].DisplayName);
        Assert.Equal(55.5, parsed.Windows[3].UsedPercent);
        Assert.Equal(55.5, parsed.MaxUsedPercent);
    }

    [Fact]
    public void Antigravity_credentials_accept_direct_and_nested_google_tokens()
    {
        const string direct = """
            { "token": "google-access-fixture" }
            """;
        const string nested = """
            {
              "token": {
                "access_token": "google-access-fixture",
                "refresh_token": "google-refresh-fixture",
                "expiry": "2030-01-01T00:00:00Z"
              }
            }
            """;

        var directMetadata = AntigravityRateLimitsProvider.ParseCredentialMetadata(direct);
        var nestedMetadata = AntigravityRateLimitsProvider.ParseCredentialMetadata(nested);

        Assert.True(directMetadata.HasAccessToken);
        Assert.True(nestedMetadata.HasAccessToken);
        Assert.False(nestedMetadata.IsExpired);
        Assert.Equal(DateTimeOffset.Parse("2030-01-01T00:00:00Z"), nestedMetadata.ExpiresAt);
        Assert.DoesNotContain("google-access-fixture", nestedMetadata.ToString());
        Assert.False(AntigravityRateLimitsProvider.ParseCredentialMetadata(
            "{\"token\":\"\"}").HasAccessToken);
    }

    [Fact]
    public void Antigravity_parser_maps_group_windows_and_remaining_fraction()
    {
        const string json = """
            {
              "groups": [
                {
                  "displayName": "Gemini Models",
                  "buckets": [
                    { "bucketId": "gemini-weekly", "window": "weekly",
                      "resetTime": "2026-09-14T12:00:00Z", "remainingFraction": 0.94 },
                    { "bucketId": "gemini-5h", "window": "5h",
                      "resetTime": "2026-09-08T12:00:00Z", "remainingFraction": 0.85 }
                  ]
                },
                {
                  "displayName": "Claude and GPT models",
                  "buckets": [
                    { "bucketId": "3p-5h", "window": "5h",
                      "resetTime": "2026-09-08T12:00:00Z", "remainingFraction": 0.50 }
                  ]
                }
              ]
            }
            """;

        var parsed = AntigravityRateLimitParser.Parse(
            json,
            new DateTimeOffset(2026, 9, 8, 6, 0, 0, TimeSpan.Zero));

        Assert.NotNull(parsed);
        Assert.Equal(3, parsed!.Windows.Count);
        Assert.Equal("Gemini 주간", parsed.Windows[0].DisplayName);
        Assert.Equal(6, parsed.Windows[0].UsedPercent, 5);
        Assert.Equal("Gemini 5시간 세션", parsed.Windows[1].DisplayName);
        Assert.Equal(15, parsed.Windows[1].UsedPercent, 5);
        Assert.Equal("외부 모델 5시간 세션", parsed.Windows[2].DisplayName);
        Assert.Equal(50, parsed.Windows[2].UsedPercent, 5);
        Assert.Equal(50, parsed.MaxUsedPercent);
    }

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
