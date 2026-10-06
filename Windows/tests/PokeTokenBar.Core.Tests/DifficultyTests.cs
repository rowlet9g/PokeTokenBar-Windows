using System.Text.Json;
using PokeTokenBar.Core;

namespace PokeTokenBar.Core.Tests;

public sealed class DifficultyTests
{
    [Theory]
    [InlineData(0, 0.1)]
    [InlineData(-1, 0.1)]
    [InlineData(9, 2)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    public void Settings_normalize_invalid_difficulty(double raw, double expected)
    {
        var settings = new AppSettings { GrowthDifficulty = raw, ShopDifficulty = raw }.Normalize();
        Assert.Equal(expected, settings.GrowthDifficulty);
        Assert.Equal(expected, settings.ShopDifficulty);
    }

    [Fact]
    public void Logarithmic_sliders_match_upstream_endpoints_rounding_and_default_snap()
    {
        foreach (var value in new[] { 0.1, 0.25, 0.75, 1, 1.5, 2 })
            Assert.Equal(value, PokemonBalance.DifficultyAtPosition(PokemonBalance.DifficultyPosition(value)), 12);
        Assert.Equal(1, PokemonBalance.DifficultyAtPosition(PokemonBalance.DifficultyPosition(1) + 0.009));
        Assert.Equal(0.1, PokemonBalance.DifficultyAtPosition(-10));
        Assert.Equal(2, PokemonBalance.DifficultyAtPosition(10));
    }

    [Fact]
    public void Legacy_settings_and_progress_keep_default_costs_without_rewriting_state()
    {
        using var f = new Fixture();
        File.WriteAllText(f.SettingsPath, "{}");
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(f.StatePath))!.AsObject();
        legacy.Remove("growthDifficultyBasis");
        File.WriteAllText(f.StatePath, legacy.ToJsonString());
        var original = File.ReadAllBytes(f.StatePath);
        var store = f.Store();
        Assert.Equal(1, store.GrowthDifficulty);
        Assert.Equal(1, f.Settings.Current.ShopDifficulty);
        Assert.Equal(125_000_000 - 50_000_000, store.TokensToNextStage);
        Assert.Equal(500_000_000, store.Price(CompanionItemKind.RareCandy));
        Assert.Equal(original, File.ReadAllBytes(f.StatePath));
    }

    [Fact]
    public void Egg_save_preserves_fraction_ledgers_and_ready_hatch_identity()
    {
        using var f = new Fixture(new CompanionState { EggUsage = 2_000_000, PendingHatchId = 1 });
        var store = f.Store();
        store.SaveSettings(f.Settings, f.Settings.Current with { GrowthDifficulty = 0.75 });
        Assert.Equal(0.4, store.EggProgress);
        Assert.Equal(1_500_000, store.EggUsage);
        Assert.Equal(3_750_000, store.EggHatchThreshold);
        Assert.False(store.ReadyToHatch);
        Assert.Equal(1, store.ExportStateSnapshot().PendingHatchId);
        Assert.Equal(750_000_000, store.UsedSinceInstall);
        Assert.Equal(123, store.SpentTokens);
        Assert.Equal(456, store.ClaimedTodayTokensByProvider!["codex"]);
        Assert.Equal(0.75, new AppSettingsStore(f.SettingsPath).Current.GrowthDifficulty);
        Assert.Equal(store.EggUsage, f.Store().EggUsage);
    }

    [Theory]
    [InlineData(false, 0.75, 37_500_000, 93_750_000)]
    [InlineData(true, 0.75, 37_500_000, 46_875_000)]
    [InlineData(false, 2, 100_000_000, 250_000_000)]
    public void Stage_save_keeps_fraction_and_repeat_bonus_without_evolving(
        bool boosted, double difficulty, long expectedCredits, long expectedThreshold)
    {
        using var f = new Fixture();
        f.State.ActivePokemon!.HasGrowthBoost = boosted;
        f.WriteState();
        var store = f.Store();
        var fraction = store.GrowthProgress;
        store.SaveSettings(f.Settings, f.Settings.Current with { GrowthDifficulty = difficulty });
        Assert.Equal(expectedCredits, store.ActiveUsedAtStage);
        Assert.Equal(expectedThreshold - expectedCredits, store.TokensToNextStage);
        Assert.Equal(fraction, store.GrowthProgress);
        Assert.Equal(1, store.CurrentSpeciesId);
        Assert.Equal(0, store.ExportStateSnapshot().ActivePokemon!.StageIndex);
        Assert.Equal(boosted ? 2 : 1, store.CurrentGrowthMultiplier);
        var restored = f.Store();
        Assert.Equal(expectedCredits, restored.ActiveUsedAtStage);
        Assert.Equal(fraction, restored.GrowthProgress);
    }

    [Fact]
    public void Rounding_never_completes_an_unfinished_stage_and_overflow_remains_banked()
    {
        using var f = new Fixture();
        f.State.ActivePokemon!.UsedAtStage = 124_999_999;
        f.WriteState();
        var store = f.Store();
        store.SaveSettings(f.Settings, f.Settings.Current with { GrowthDifficulty = 0.1 });
        Assert.Equal(1, store.TokensToNextStage);
        Assert.Equal(1, store.CurrentSpeciesId);
        using var overflow = new Fixture();
        overflow.State.ActivePokemon!.UsedAtStage = 150_000_000;
        overflow.WriteState();
        var pending = overflow.Store();
        pending.SaveSettings(overflow.Settings, overflow.Settings.Current with { GrowthDifficulty = 0.5 });
        Assert.Equal(75_000_000, pending.ActiveUsedAtStage);
        Assert.Equal(1, pending.CurrentSpeciesId);
    }

    [Fact]
    public void Shop_prices_affordability_and_debits_share_the_same_independent_multiplier()
    {
        using var f = new Fixture();
        var store = f.Store();
        store.SaveSettings(f.Settings, f.Settings.Current with { ShopDifficulty = 0.5 });
        foreach (var item in Enum.GetValues<CompanionItemKind>())
            Assert.Equal(CompanionItemRules.Price(item) / 2, store.Price(item));
        foreach (var tier in Enum.GetValues<FreshEggTier>())
            Assert.Equal(CompanionItemRules.FreshEggPrice(tier) / 2, store.FreshEggPrice(tier));
        Assert.True(store.CanBuyItem(CompanionItemKind.RareCandy));
        Assert.False(store.CanBuyFreshEgg(FreshEggTier.Uncommon));
        Assert.True(store.BuyItem(CompanionItemKind.RareCandy));
        Assert.Equal(250_000_123, store.SpentTokens);
        Assert.Equal(50_000_000, store.ActiveUsedAtStage);
        f.State.UsedSinceInstall = 2_000_000_000;
        f.WriteState();
        store = f.Store();
        Assert.True(store.BuyFreshEgg(FreshEggTier.Uncommon));
        Assert.Equal(1_250_000_123, store.SpentTokens);
        Assert.Equal(2_000_000_000, store.UsedSinceInstall);
    }

    [Fact]
    public void Candy_still_grants_100M_and_carries_over_against_scaled_stage_thresholds()
    {
        using var f = new Fixture();
        f.State.ActivePokemon!.UsedAtStage = 10_000_000;
        f.State.Inventory["rareCandy"] = 1;
        f.WriteState();
        var store = f.Store();
        store.SaveSettings(f.Settings, f.Settings.Current with { GrowthDifficulty = 0.5 });
        Assert.Equal(RareCandyUseResult.Evolved, store.UseRareCandy());
        Assert.Equal(2, store.CurrentSpeciesId);
        Assert.Equal(42_500_000, store.ActiveUsedAtStage);
        Assert.Equal(750_000_000, store.UsedSinceInstall);
        Assert.Equal(123, store.SpentTokens);
    }

    [Fact]
    public async Task Hatching_uses_scaled_threshold_and_retains_overflow()
    {
        using var f = new Fixture(new CompanionState { EggUsage = 6_000_000, PendingHatchId = 1 });
        var store = f.Store(new Species());
        store.SaveSettings(f.Settings, f.Settings.Current with { GrowthDifficulty = 0.5 });
        Assert.False(store.HasActivePokemon);
        Assert.Equal(3_000_000, store.EggUsage);
        Assert.True(await store.EnsureHatchedAsync());
        Assert.Equal(500_000, store.ActiveUsedAtStage);
        Assert.Equal(62_000_000, store.TokensToNextStage);
    }

    [Fact]
    public async Task Ditto_reveal_consumes_scaled_disguise_threshold_and_keeps_overflow()
    {
        using var f = new Fixture();
        f.State.ActivePokemon!.DittoDisguise = 1;
        f.State.ActivePokemon.UsedAtStage = 150_000_000;
        f.WriteState();
        var store = f.Store(new Species());
        store.SaveSettings(f.Settings, f.Settings.Current with { GrowthDifficulty = 0.5 });
        Assert.True(await store.EnsureHatchedAsync());
        Assert.Equal(132, store.CurrentSpeciesId);
        Assert.Equal(12_500_000, store.ActiveUsedAtStage);
        Assert.Equal(375_000_000 - 12_500_000, store.TokensToNextStage);
    }

    [Fact]
    public async Task Save_import_rebases_growth_to_recipient_preferences()
    {
        using var source = new Fixture();
        var sender = source.Store();
        sender.SaveSettings(source.Settings, source.Settings.Current with { GrowthDifficulty = 0.5 });
        var imported = SaveTransfer.Decode(SaveTransfer.Encode(sender.ExportStateSnapshot(), "test", "test", DateTimeOffset.Now));
        using var target = new Fixture();
        target.Settings.Save(new AppSettings { GrowthDifficulty = 1.5, ShopDifficulty = 1.75 });
        var receiver = target.Store();
        await receiver.ImportStateAsync(imported.State, new Dictionary<string, long> { ["codex"] = 999 }, new(2026, 10, 6), true, DateTimeOffset.Now);
        Assert.Equal(1.5, receiver.GrowthDifficulty);
        Assert.Equal(1.75, receiver.ShopDifficulty);
        Assert.Equal(75_000_000, receiver.ActiveUsedAtStage);
        Assert.Equal(0.4, receiver.GrowthProgress);
        Assert.Equal(1.5, receiver.ExportStateSnapshot().GrowthDifficultyBasis);
        Assert.Equal(750_000_000, receiver.UsedSinceInstall);
        Assert.Equal(999, receiver.ClaimedTodayTokensByProvider!["codex"]);
    }

    [Theory]
    [InlineData(0.5, 1, 25_000_000, 50_000_000)]
    [InlineData(1, 0.5, 50_000_000, 25_000_000)]
    public void Interrupted_two_file_save_recovers_the_same_fraction_once(
        double basis, double preference, long credits, long expected)
    {
        using var f = new Fixture();
        f.State.GrowthDifficultyBasis = basis;
        f.State.ActivePokemon!.UsedAtStage = credits;
        f.WriteState();
        f.Settings.Save(new AppSettings { GrowthDifficulty = preference });
        var recovered = f.Store();
        Assert.Equal(expected, recovered.ActiveUsedAtStage);
        Assert.Equal(0.4, recovered.GrowthProgress);
        Assert.Equal(expected, f.Store().ActiveUsedAtStage);
        Assert.Equal(750_000_000, recovered.UsedSinceInstall);
    }

    [Fact]
    public void Failed_settings_write_restores_original_progress_and_basis()
    {
        using var f = new Fixture();
        var store = f.Store();
        var before = store.ExportStateSnapshot();
        File.Delete(f.SettingsPath);
        Directory.CreateDirectory(f.SettingsPath);
        var error = Record.Exception(() => store.SaveSettings(f.Settings, f.Settings.Current with { GrowthDifficulty = 0.5, ShopDifficulty = 2 }));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(1, store.GrowthDifficulty);
        Assert.Equal(1, store.ShopDifficulty);
        Assert.Equal(before.ActivePokemon!.UsedAtStage, store.ActiveUsedAtStage);
        Assert.Equal(1, store.ExportStateSnapshot().GrowthDifficultyBasis);
        Assert.Equal(1, f.Settings.Current.GrowthDifficulty);
        Assert.Equal(50_000_000, new CompanionStore(f.StatePath).ActiveUsedAtStage);
    }

    [Fact]
    public void Failed_progress_write_does_not_save_difficulty_preferences()
    {
        using var f = new Fixture();
        var store = f.Store();
        var settingsBytes = File.ReadAllBytes(f.SettingsPath);
        using (var held = new FileStream(f.StatePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => store.SaveSettings(f.Settings, f.Settings.Current with { GrowthDifficulty = 0.5 }));
        Assert.Equal(50_000_000, store.ActiveUsedAtStage);
        Assert.Equal(1, store.GrowthDifficulty);
        Assert.Equal(settingsBytes, File.ReadAllBytes(f.SettingsPath));
        Assert.Equal(50_000_000, f.Store().ActiveUsedAtStage);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"ptb-difficulty-{Guid.NewGuid():N}");
        private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        public string StatePath => Path.Combine(_root, "companion-state.json");
        public string SettingsPath => Path.Combine(_root, "settings.json");
        public CompanionState State { get; }
        public AppSettingsStore Settings { get; }
        public Fixture(CompanionState? state = null)
        {
            Directory.CreateDirectory(_root);
            State = state ?? new CompanionState { ActivePokemon = new PokemonMonState
            {
                BaseId = 1, PathIds = [1], PlannedPathIds = [1,2,3], TotalForms = 3,
                Rarity = PokemonRarity.Common, UsedAtStage = 50_000_000,
                Names = new Dictionary<int,string> { [1] = "이상해씨", [2] = "이상해풀", [3] = "이상해꽃" },
            } };
            State.InstallBaselineSet = true;
            State.UsedSinceInstall = 750_000_000;
            State.SpentTokens = 123;
            State.ClaimedTodayTokensByProvider = new() { ["codex"] = 456 };
            State.LastDate = "2026-10-06";
            WriteState();
            Settings = new AppSettingsStore(SettingsPath);
            Settings.Save(new AppSettings());
        }
        public void WriteState() => File.WriteAllText(StatePath, JsonSerializer.Serialize(State, Options));
        public CompanionStore Store(IPokemonProvider? provider = null) => new(StatePath, provider, settings: new AppSettingsStore(SettingsPath).Current);
        public void Dispose() => Directory.Delete(_root, true);
    }

    private sealed class Species : IPokemonProvider
    {
        public Task<IReadOnlyList<BasePokemonSpecies>> GetBaseSpeciesIndexAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<BasePokemonSpecies>>([]);
        public Task<BasePokemonSpecies?> GetBaseSpeciesAsync(int id, CancellationToken token = default) => Task.FromResult<BasePokemonSpecies?>(null);
        public Task<PokemonEvolutionLine> GetEvolutionLineAsync(int id, CancellationToken token = default) => Task.FromResult(
            id == 132 ? new PokemonEvolutionLine(132, new(132, []), PokemonRarity.Common, new Dictionary<int,string> { [132] = "메타몽" })
            : new PokemonEvolutionLine(1, new(1, [new(2, [new(3, [])])]), PokemonRarity.Common,
                new Dictionary<int,string> { [1] = "이상해씨", [2] = "이상해풀", [3] = "이상해꽃" }));
    }
}
