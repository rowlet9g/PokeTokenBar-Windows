using PokeTokenBar.Core;

namespace PokeTokenBar.Core.Tests;

public sealed class LimitInteractionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-15T12:00:00+09:00");
    private static ProviderRateLimitSnapshot Snapshot(double used, string id = "codex:primary", int minutes = 300,
        string provider = "codex") => new(provider, provider, "plus",
            [new(id, id, used, Now.AddHours(5), minutes)], Now);

    [Fact]
    public void Fresh_install_at_full_limit_seeds_without_retroactive_candy()
    {
        using var dir = TemporaryDirectory.Create();
        var store = new CompanionStore(Path.Combine(dir.Path, "state.json"));
        Assert.Empty(store.ApplyOfficialLimits([Snapshot(100)], Now).Rewards);
        Assert.Empty(store.ApplyOfficialLimits([Snapshot(100)], Now).Rewards);
        Assert.Equal(0, store.ItemCount(CompanionItemKind.RareCandy));
    }

    [Fact]
    public void Session_and_weekly_rewards_survive_restart_and_rearm_after_reset()
    {
        using var dir = TemporaryDirectory.Create();
        var path = Path.Combine(dir.Path, "state.json");
        var store = new CompanionStore(path);
        store.ApplyOfficialLimits([Snapshot(70), Snapshot(70, "codex:secondary", 10080)], Now);
        var awarded = store.ApplyOfficialLimits([Snapshot(100), Snapshot(100, "codex:secondary", 10080)], Now);
        Assert.Equal(6, awarded.Rewards.Sum(r => r.Count));
        Assert.Equal(0, store.UsedSinceInstall);
        Assert.Equal(0, store.SpentTokens);
        store = new CompanionStore(path);
        Assert.Empty(store.ApplyOfficialLimits([Snapshot(100), Snapshot(100, "codex:secondary", 10080)], Now).Rewards);
        store.ApplyOfficialLimits([Snapshot(0)], Now);
        store = new CompanionStore(path);
        Assert.Single(store.ApplyOfficialLimits([Snapshot(100)], Now).Rewards);
        Assert.Equal(7, store.ItemCount(CompanionItemKind.RareCandy));
    }

    [Fact]
    public void Alerts_fire_on_rising_tiers_only_and_survive_restart()
    {
        using var dir = TemporaryDirectory.Create();
        var path = Path.Combine(dir.Path, "state.json");
        var store = new CompanionStore(path);
        Assert.Empty(store.ApplyOfficialLimits([Snapshot(79)], Now).Notices);
        Assert.Equal(1, Assert.Single(store.ApplyOfficialLimits([Snapshot(80)], Now).Notices).Tier);
        Assert.Empty(store.ApplyOfficialLimits([Snapshot(84)], Now).Notices);
        Assert.Equal(2, Assert.Single(store.ApplyOfficialLimits([Snapshot(95)], Now).Notices).Tier);
        store = new CompanionStore(path);
        Assert.Empty(store.ApplyOfficialLimits([Snapshot(100)], Now).Notices);
        Assert.Empty(store.ApplyOfficialLimits([Snapshot(90)], Now).Notices);
        store.ApplyOfficialLimits([Snapshot(20)], Now);
        Assert.Single(store.ApplyOfficialLimits([Snapshot(80)], Now).Notices);
    }

    [Theory]
    [InlineData("claude", "seven_day_opus", 10080)]
    [InlineData("claude", "scoped:sonnet", 10080)]
    [InlineData("codex", "codex:individual", 0)]
    [InlineData("antigravity", "other", 60)]
    public void Duplicate_or_spend_windows_never_award_candy(string provider, string id, int minutes)
    {
        using var dir = TemporaryDirectory.Create();
        var store = new CompanionStore(Path.Combine(dir.Path, "state.json"));
        store.ApplyOfficialLimits([Snapshot(50, id, minutes, provider)], Now);
        Assert.Empty(store.ApplyOfficialLimits([Snapshot(100, id, minutes, provider)], Now).Rewards);
    }

    [Fact]
    public void Stale_expired_and_invalid_values_do_not_rearm_or_reward()
    {
        using var dir = TemporaryDirectory.Create();
        var store = new CompanionStore(Path.Combine(dir.Path, "state.json"));
        store.ApplyOfficialLimits([Snapshot(100)], Now);
        store.ApplyOfficialLimits([Snapshot(0) with { FetchedAt = Now.AddMinutes(-3) }], Now);
        store.ApplyOfficialLimits([Snapshot(double.NaN)], Now);
        store.ApplyOfficialLimits([Snapshot(0)], Now.AddHours(6));
        Assert.Empty(store.ApplyOfficialLimits([Snapshot(100)], Now).Rewards);
    }

    [Fact]
    public void Moving_reset_timestamp_or_duplicate_rows_do_not_repeat_rewards()
    {
        using var dir = TemporaryDirectory.Create();
        var store = new CompanionStore(Path.Combine(dir.Path, "state.json"));
        store.ApplyOfficialLimits([Snapshot(50)], Now);
        Assert.Single(store.ApplyOfficialLimits([Snapshot(100), Snapshot(100)], Now).Rewards);
        var moved = Snapshot(100) with { Windows = [Snapshot(100).Windows[0] with { ResetsAt = Now.AddHours(8) }] };
        Assert.Empty(store.ApplyOfficialLimits([moved], Now).Rewards);
    }

    [Fact]
    public async Task Importing_older_save_cannot_reissue_an_already_granted_reward()
    {
        using var dir = TemporaryDirectory.Create();
        var store = new CompanionStore(Path.Combine(dir.Path, "state.json"));
        store.ApplyOfficialLimits([Snapshot(50)], Now);
        var oldSave = store.ExportStateSnapshot();
        store.ApplyOfficialLimits([Snapshot(100)], Now);
        await store.ImportStateAsync(oldSave, new Dictionary<string,long>(), new DateOnly(2026,9,15), false, Now);
        Assert.Empty(store.ApplyOfficialLimits([Snapshot(100)], Now).Rewards);
    }

    [Fact]
    public void Failed_save_rolls_back_reward_and_can_retry()
    {
        using var dir = TemporaryDirectory.Create();
        var path = Path.Combine(dir.Path, "state.json");
        var store = new CompanionStore(path);
        store.ApplyOfficialLimits([Snapshot(50)], Now);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => store.ApplyOfficialLimits([Snapshot(100)], Now));
        Assert.Equal(0, store.ItemCount(CompanionItemKind.RareCandy));
        Assert.Single(store.ApplyOfficialLimits([Snapshot(100)], Now).Rewards);
    }

    [Fact]
    public async Task Failed_provider_is_displayed_but_excluded_from_fresh_effects()
    {
        var provider = new TestProvider();
        using var store = new RateLimitStore([provider]);
        await store.RefreshAsync();
        Assert.Single(store.FreshSnapshots);
        provider.Fail = true;
        await store.RefreshAsync();
        Assert.Single(store.Snapshots);
        Assert.Empty(store.FreshSnapshots);
    }

    [Fact]
    public void Companion_status_uses_limit_before_activity_and_ignores_stale_limit()
    {
        Assert.Contains("쉬어", LimitInteraction.Mood(true, true, 100, 500000, [Snapshot(95)], Now));
        Assert.Contains("집중", LimitInteraction.Mood(true, true, 100, 500000, [Snapshot(80)], Now));
        Assert.Contains("집중", LimitInteraction.Mood(true, true, 100, 500000, [Snapshot(95)], Now.AddMinutes(3)));
        Assert.Contains("잠들", LimitInteraction.Mood(true, true, 0, 0, [], Now));
        Assert.Contains("만남", LimitInteraction.Mood(false, false, 0, 0, [Snapshot(100)], Now));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ptb-limits-" + Guid.NewGuid().ToString("N"));
        public static TemporaryDirectory Create()
        {
            var value = new TemporaryDirectory();
            Directory.CreateDirectory(value.Path);
            return value;
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class TestProvider : IRateLimitProvider
    {
        public bool Fail { get; set; }
        public string Id => "codex";
        public string DisplayName => "Codex";
        public Task<ProviderRateLimitSnapshot?> FetchAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Fail ? Task.FromException<ProviderRateLimitSnapshot?>(new IOException("offline"))
                : Task.FromResult<ProviderRateLimitSnapshot?>(Snapshot(100) with { FetchedAt = now });
    }
}
