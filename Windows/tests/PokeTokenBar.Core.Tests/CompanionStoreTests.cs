using System.Text;

namespace PokeTokenBar.Core.Tests;

public sealed class CompanionStoreTests
{
    private static readonly DateOnly DayOne = new(2026, 8, 27);
    private static readonly DateOnly DayTwo = DayOne.AddDays(1);

    [Fact]
    public void First_valid_snapshot_seeds_a_baseline_without_retroactive_credit()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(temporary);

        Update(store, new Dictionary<string, long> { ["codex"] = 29_000_000 });

        Assert.True(store.InstallBaselineSet);
        Assert.Equal(0, store.UsedSinceInstall);
        Assert.Equal(0, store.EggUsage);
        Assert.Equal(29_000_000, store.ClaimedTodayTokensByProvider?["codex"]);
    }

    [Fact]
    public void Same_day_growth_credits_only_the_provider_delta()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(temporary);
        Update(store, new Dictionary<string, long> { ["codex"] = 100 });

        Update(store, new Dictionary<string, long> { ["codex"] = 350 });

        Assert.Equal(250, store.UsedSinceInstall);
        Assert.Equal(250, store.EggUsage);
    }

    [Fact]
    public void Provider_regression_rebases_only_that_line_and_continues_progress()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(temporary);
        Update(store, new Dictionary<string, long> { ["codex"] = 0 });
        Update(store, new Dictionary<string, long> { ["codex"] = 200 });

        Update(store, new Dictionary<string, long> { ["codex"] = 40 });
        Update(store, new Dictionary<string, long> { ["codex"] = 75 });

        Assert.Equal(235, store.UsedSinceInstall);
        Assert.Equal(235, store.EggUsage);
        Assert.Equal(75, store.ClaimedTodayTokensByProvider?["codex"]);
    }

    [Fact]
    public void Empty_snapshot_does_not_rebase_or_consume_a_date_boundary()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(temporary);
        Update(store, new Dictionary<string, long> { ["codex"] = 0 });
        Update(store, new Dictionary<string, long> { ["codex"] = 200 });

        store.Update(new Dictionary<string, long>(), DayTwo, hasUsageData: true);
        Update(store, new Dictionary<string, long> { ["codex"] = 100 }, DayTwo);

        Assert.Equal(300, store.UsedSinceInstall);
        Assert.Equal("2026-08-28", store.LastDate);
    }

    [Fact]
    public void Missing_provider_keeps_its_ledger_until_it_recovers()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(temporary);
        Update(store, new Dictionary<string, long> { ["claude_code"] = 0, ["codex"] = 0 });
        Update(store, new Dictionary<string, long> { ["claude_code"] = 1_000, ["codex"] = 500 });

        Update(store, new Dictionary<string, long> { ["claude_code"] = 1_000 });
        Update(store, new Dictionary<string, long> { ["claude_code"] = 1_000, ["codex"] = 500 });
        Update(store, new Dictionary<string, long> { ["claude_code"] = 1_000, ["codex"] = 700 });

        Assert.Equal(1_700, store.UsedSinceInstall);
        Assert.Equal(700, store.ClaimedTodayTokensByProvider?["codex"]);
    }

    [Fact]
    public void Date_rollover_opens_missing_provider_lines_at_zero()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(temporary);
        Update(store, new Dictionary<string, long> { ["claude_code"] = 0, ["codex"] = 0 });
        Update(store, new Dictionary<string, long> { ["claude_code"] = 1_000, ["codex"] = 500 });

        Update(store, new Dictionary<string, long> { ["claude_code"] = 100 }, DayTwo);
        Update(store, new Dictionary<string, long> { ["claude_code"] = 100, ["codex"] = 700 }, DayTwo);
        Update(store, new Dictionary<string, long> { ["claude_code"] = 100, ["codex"] = 900 }, DayTwo);

        Assert.Equal(2_500, store.UsedSinceInstall);
        Assert.Equal(
            new Dictionary<string, long> { ["claude_code"] = 100, ["codex"] = 900 },
            store.ClaimedTodayTokensByProvider);
    }

    [Fact]
    public void Newly_seen_provider_is_seeded_without_retroactive_credit()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(temporary);
        Update(store, new Dictionary<string, long> { ["codex"] = 100 });

        Update(store, new Dictionary<string, long> { ["codex"] = 100, ["claude_code"] = 1_000 });
        Update(store, new Dictionary<string, long> { ["codex"] = 100, ["claude_code"] = 1_250 });

        Assert.Equal(250, store.UsedSinceInstall);
    }

    [Fact]
    public void Egg_progress_is_persisted_and_restored()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(temporary);
        Update(store, new Dictionary<string, long> { ["codex"] = 0 });
        Update(store, new Dictionary<string, long> { ["codex"] = 2_000_000 });

        var reloaded = CreateStore(temporary);

        Assert.Equal(2_000_000, reloaded.EggUsage);
        Assert.Equal(0.4, reloaded.EggProgress, precision: 6);
        Assert.Equal(3_000_000, reloaded.EggTokensToHatch);
        Assert.False(reloaded.ReadyToHatch);
    }

    [Fact]
    public void Corrupt_state_is_backed_up_before_starting_fresh()
    {
        using var temporary = TemporaryDirectory.Create();
        var path = StatePath(temporary);
        File.WriteAllText(path, "{ definitely-not-json", new UTF8Encoding(false));

        var store = new CompanionStore(path);

        Assert.False(store.InstallBaselineSet);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists($"{path}.corrupt"));
    }

    [Fact]
    public void Loaded_token_values_are_clamped_at_the_trust_boundary()
    {
        using var temporary = TemporaryDirectory.Create();
        File.WriteAllText(
            StatePath(temporary),
            $$"""
            {
              "installBaselineSet": true,
              "usedSinceInstall": {{long.MaxValue}},
              "spentTokens": -1,
              "eggUsage": {{long.MaxValue}},
              "claimedTodayTokensByProvider": { "codex": -50 },
              "lastDate": "2026-08-27"
            }
            """,
            new UTF8Encoding(false));

        var store = CreateStore(temporary);

        Assert.Equal(PokemonBalance.MaxTokenValue, store.UsedSinceInstall);
        Assert.Equal(PokemonBalance.MaxTokenValue, store.EggUsage);
        Assert.Equal(0, store.ClaimedTodayTokensByProvider?["codex"]);
        Assert.Equal(PokemonBalance.MaxTokenValue, store.AvailableTokens);
    }

    private static CompanionStore CreateStore(TemporaryDirectory temporary) =>
        new(StatePath(temporary));

    private static string StatePath(TemporaryDirectory temporary) =>
        Path.Combine(temporary.Path, "companion-state.json");

    private static void Update(
        CompanionStore store,
        IReadOnlyDictionary<string, long> usage,
        DateOnly? date = null) =>
        store.Update(usage, date ?? DayOne, hasUsageData: true);

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PokeTokenBarCompanionTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
