using System.Text.Json;
using System.Text.Json.Serialization;
using PokeTokenBar.Core;

namespace PokeTokenBar.Core.Tests;

public sealed class CollectionBalanceTests
{
    private static readonly DateOnly Today = new(2026, 10, 2);
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    [Theory]
    [InlineData(255, true, 127)]
    [InlineData(3, true, 1)]
    [InlineData(1, true, 1)]
    [InlineData(255, false, 255)]
    public void Duplicate_weights_use_integer_halves_and_never_remove_a_candidate(int weight, bool collected, int expected) =>
        Assert.Equal(expected, CollectionWeight.Adjusted(weight, collected));

    [Theory]
    [InlineData(false, 126, 1, 382)]
    [InlineData(false, 127, 7, 382)]
    [InlineData(false, 381, 7, 382)]
    [InlineData(true, 254, 1, 510)]
    [InlineData(true, 255, 7, 510)]
    public async Task Weighted_hatch_uses_completed_history_and_ignores_released_entries(
        bool released, long roll, int expectedSpecies, long expectedWeight)
    {
        using var fixture = new Fixture();
        var state = ReadyEgg(released);
        var random = new HatchRandom(roll);
        var store = fixture.Store(state, random);
        Assert.True(await store.EnsureHatchedAsync());
        Assert.Equal(expectedWeight, random.FirstMaximum);
        Assert.Equal(expectedSpecies, store.CurrentSpeciesId);
        Assert.Equal(!released && expectedSpecies == 1, store.ExportStateSnapshot().ActivePokemon!.HasGrowthBoost);
    }

    [Fact]
    public async Task All_collected_species_still_hatch_with_one_history_weight_per_base()
    {
        using var fixture = new Fixture();
        var state = ReadyEgg();
        state.Dex.Add(Completed(1)); // Multiple individuals must not halve the weight again.
        state.Dex.Add(Completed(7));
        var random = new HatchRandom(127);
        var store = fixture.Store(state, random);
        Assert.True(await store.EnsureHatchedAsync());
        Assert.Equal(254, random.FirstMaximum);
        Assert.Equal(7, store.CurrentSpeciesId);
        Assert.Equal(2, store.CurrentGrowthMultiplier);
    }

    [Theory]
    [InlineData(PokemonRarity.Common)]
    [InlineData(PokemonRarity.Uncommon)]
    [InlineData(PokemonRarity.Rare)]
    [InlineData(PokemonRarity.Legendary)]
    public void Repeat_growth_halves_each_rounded_stage_cost(PokemonRarity rarity)
    {
        for (var forms = 1; forms <= 4; forms++)
        {
            for (var stage = 0; stage < forms; stage++)
            {
                var standard = PokemonBalance.PhaseThreshold(rarity, forms, stage);
                var expected = (long)Math.Round(standard / 2d, MidpointRounding.AwayFromZero);
                Assert.Equal(expected, PokemonBalance.PhaseThreshold(rarity, forms, stage, 2));
            }
        }
    }

    [Fact]
    public async Task Boosted_growth_preserves_overflow_and_graduates_without_doubling_usage_or_wallet()
    {
        using var fixture = new Fixture();
        var state = ReadyEgg();
        state.EggUsage += 7;
        state.UsedSinceInstall = state.EggUsage;
        var store = fixture.Store(state);
        Assert.True(await store.EnsureHatchedAsync());
        Assert.Equal(2, store.CurrentGrowthMultiplier);
        Assert.Equal(7, store.ActiveUsedAtStage);
        Assert.Equal(62_500_000 - 7, store.TokensToNextStage);
        Assert.Equal(7 / 62_500_000d, store.GrowthProgress);

        long used = 0;
        Update(store, used += store.TokensToNextStage);
        Assert.Equal(2, store.CurrentSpeciesId);
        Assert.Equal(0, store.ActiveUsedAtStage);
        Assert.Equal(125_000_000, store.TokensToNextStage);
        Update(store, used += store.TokensToNextStage);
        Assert.Equal(3, store.CurrentSpeciesId);
        Assert.Equal(187_500_000, store.TokensToNextStage);
        Update(store, used += store.TokensToNextStage);
        Assert.False(store.HasActivePokemon);
        Assert.Equal(2, store.DexCount);
        Assert.Equal(PokemonBalance.EggHatchThreshold + 375_000_000, store.UsedSinceInstall);
        Assert.Equal(store.UsedSinceInstall, store.AvailableTokens);
        Assert.Equal(used, store.ClaimedTodayTokensByProvider!["codex"]);
    }

    [Fact]
    public async Task Bonus_and_progress_survive_restart_and_save_transfer()
    {
        using var fixture = new Fixture();
        var store = fixture.Store(ReadyEgg());
        await store.EnsureHatchedAsync();
        Update(store, 62_500_000 + 123);
        store = fixture.Reload();
        await store.EnsureHatchedAsync();
        Assert.Equal(2, store.CurrentSpeciesId);
        Assert.Equal(123, store.ActiveUsedAtStage);
        Assert.Equal(125_000_000 - 123, store.TokensToNextStage);
        var imported = SaveTransfer.Decode(SaveTransfer.Encode(store.ExportStateSnapshot(), "test", "test", DateTimeOffset.Now));
        using var target = new Fixture();
        var restored = target.Store(imported.State);
        await restored.EnsureHatchedAsync();
        Assert.Equal(2, restored.CurrentGrowthMultiplier);
        Assert.Equal(123, restored.ActiveUsedAtStage);
        Assert.Equal(125_000_000 - 123, restored.TokensToNextStage);
    }

    [Fact]
    public async Task Old_active_save_keeps_original_cost_and_progress_even_with_completed_history()
    {
        using var fixture = new Fixture();
        var state = ReadyEgg();
        state.EggUsage = 0;
        state.ActivePokemon = new PokemonMonState
        {
            BaseId = 1, PathIds = [1, 2], PlannedPathIds = [1, 2, 3], StageIndex = 1,
            TotalForms = 3, Rarity = PokemonRarity.Common, UsedAtStage = 123,
        };
        var store = fixture.Store(state);
        Assert.DoesNotContain("hasGrowthBoost", File.ReadAllText(fixture.FilePath));
        Assert.False(await store.EnsureHatchedAsync());
        Assert.Equal(1, store.CurrentGrowthMultiplier);
        Assert.Equal(2, store.CurrentSpeciesId);
        Assert.Equal(123, store.ActiveUsedAtStage);
        Assert.Equal(250_000_000 - 123, store.TokensToNextStage);
    }

    [Fact]
    public async Task Guaranteed_egg_keeps_its_rarity_filter_when_completed_weights_are_applied()
    {
        using var fixture = new Fixture();
        var state = ReadyEgg();
        state.EggGuarantee = PokemonRarity.Rare;
        state.Dex.Add(Completed(7));
        var random = new HatchRandom(0);
        var store = fixture.Store(state, random, new Provider(rareSecond: true));
        Assert.True(await store.EnsureHatchedAsync());
        Assert.Equal(22, random.FirstMaximum); // Rare capture_rate 45 / 2, common excluded.
        Assert.Equal(7, store.CurrentSpeciesId);
        Assert.Equal(PokemonRarity.Rare, store.CurrentPokemonRarity);
        Assert.Equal(2, store.CurrentGrowthMultiplier);
    }

    [Fact]
    public async Task Ditto_reveal_keeps_hatch_bonus_and_uses_boosted_cost_for_overflow()
    {
        using var fixture = new Fixture();
        var store = fixture.Store(ReadyEgg(), ditto: true);
        Assert.True(await store.EnsureHatchedAsync());
        Assert.True(store.ExportStateSnapshot().ActivePokemon!.IsDittoDisguised);
        Update(store, 62_500_000 + 123);
        Assert.True(await store.EnsureHatchedAsync());
        Assert.Equal(132, store.CurrentSpeciesId);
        Assert.Equal(2, store.CurrentGrowthMultiplier);
        Assert.Equal(123, store.ActiveUsedAtStage);
        Assert.Equal(1_500_000_000 - 123, store.TokensToNextStage);
    }

    private static void Update(CompanionStore store, long tokens) =>
        store.Update(new Dictionary<string, long> { ["codex"] = tokens }, Today, true);

    private static PokemonDexEntry Completed(int baseId, bool released = false) => new()
    {
        BaseId = baseId, FinalId = baseId + 2, ChainOrder = [baseId, baseId + 1, baseId + 2],
        ReleasedAt = released ? DateTimeOffset.Now : null,
    };

    private static CompanionState ReadyEgg(bool released = false) => new()
    {
        InstallBaselineSet = true, LastDate = "2026-10-02",
        ClaimedTodayTokensByProvider = new() { ["codex"] = 0 },
        EggUsage = PokemonBalance.EggHatchThreshold, Dex = [Completed(1, released)],
    };

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "PTB-Collection-" + Guid.NewGuid().ToString("N"));
        public string FilePath => Path.Combine(_directory, "state.json");
        public CompanionStore Store(CompanionState state, HatchRandom? random = null, Provider? provider = null, bool ditto = false)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state, Options));
            return new(FilePath, provider ?? new Provider(), random ?? new HatchRandom(0), dittoDisguiseRollingEnabled: ditto);
        }
        public CompanionStore Reload() => new(FilePath, new Provider(), new HatchRandom(0));
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    private sealed class HatchRandom(long firstRoll) : IRandomSource
    {
        public long? FirstMaximum { get; private set; }
        public long NextInt64(long maximum)
        {
            if (FirstMaximum is not null) return 0;
            FirstMaximum = maximum;
            Assert.InRange(firstRoll, 0, maximum - 1);
            return firstRoll;
        }
    }

    private sealed class Provider(bool rareSecond = false) : IPokemonProvider
    {
        public Task<IReadOnlyList<BasePokemonSpecies>> GetBaseSpeciesIndexAsync(CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<BasePokemonSpecies>>([new(1, 255), new(7, rareSecond ? 45 : 255)]);
        public Task<BasePokemonSpecies?> GetBaseSpeciesAsync(int id, CancellationToken token = default) =>
            Task.FromResult<BasePokemonSpecies?>(new(id, 255));
        public Task<PokemonEvolutionLine> GetEvolutionLineAsync(int id, CancellationToken token = default) =>
            Task.FromResult(new PokemonEvolutionLine(id,
                id == 132 ? new(id, []) : new(id, [new(id + 1, [new(id + 2, [])])]),
                id == 132 || rareSecond && id == 7 ? PokemonRarity.Rare : PokemonRarity.Common,
                new Dictionary<int, string> { [id] = "A", [id + 1] = "B", [id + 2] = "C" }));
    }
}
