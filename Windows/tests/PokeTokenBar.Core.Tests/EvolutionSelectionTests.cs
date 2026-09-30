using PokeTokenBar.Core;

namespace PokeTokenBar.Core.Tests;

public sealed class EvolutionSelectionTests
{
    private static readonly PokemonEvolutionNode Tree = new(1, [new(2, [new(3, []), new(4, [])])]);
    private static readonly DateOnly Today = new(2026, 9, 30);

    [Fact]
    public void Fresh_finals_are_preferred_at_each_depth_and_all_collected_falls_back_to_random()
    {
        Assert.Equal(new[] { 1, 2, 4 }, EvolutionPlanner.Build(Tree, new HashSet<int> { 3 }, new FirstRandom()));
        Assert.Equal(new[] { 1, 2, 3 }, EvolutionPlanner.Build(Tree, new HashSet<int> { 3, 4 }, new FirstRandom()));
        var nested = new PokemonEvolutionNode(133, [new(134, []), new(135, [new(136, [])])]);
        Assert.Equal(new[] { 133, 135, 136 }, EvolutionPlanner.Build(nested, new HashSet<int> { 134 }, new FirstRandom()));
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 3)]
    public async Task Hatch_uses_completed_final_history_but_not_released_companions(bool released, int expected)
    {
        using var fixture = new Fixture();
        var state = new CompanionState
        {
            InstallBaselineSet = true, LastDate = "2026-09-30",
            ClaimedTodayTokensByProvider = new() { ["codex"] = 0 },
            EggUsage = PokemonBalance.EggHatchThreshold,
            Dex = [new PokemonDexEntry { BaseId = 1, FinalId = 3, ChainOrder = [1, 2, 3],
                ReleasedAt = released ? DateTimeOffset.Now : null }],
        };
        var store = fixture.Store(state);
        Assert.True(await store.EnsureHatchedAsync());
        Assert.Equal(expected, store.ExportStateSnapshot().ActivePokemon!.PlannedPathIds[^1]);
    }

    [Fact]
    public async Task Missing_plan_is_rebuilt_after_current_stage_preserving_progress_and_identity()
    {
        using var fixture = new Fixture();
        var store = fixture.Store(ActiveState([1, 2]));
        Assert.True(await store.EnsureHatchedAsync());
        var active = store.ExportStateSnapshot().ActivePokemon!;
        Assert.Equal(new[] { 1, 2 }, active.PathIds);
        Assert.Equal(new[] { 1, 2, 3 }, active.PlannedPathIds);
        Assert.Equal(2, store.CurrentSpeciesId);
        Assert.Equal(123, active.UsedAtStage);
        Assert.True(active.IsShiny);
        Assert.Equal(PokemonNature.Jolly, active.Nature);
    }

    [Fact]
    public async Task Valid_saved_plan_is_never_rerolled_even_when_other_branches_are_fresh()
    {
        using var fixture = new Fixture();
        var state = ActiveState([1, 2, 4]);
        state.Dex = [new PokemonDexEntry { BaseId = 1, FinalId = 4, ChainOrder = [1, 2, 4] }];
        var store = fixture.Store(state, random: new ForbiddenRandom());
        Assert.False(await store.EnsureHatchedAsync());
        Assert.Equal(new[] { 1, 2, 4 }, store.ExportStateSnapshot().ActivePokemon!.PlannedPathIds);
        var restarted = fixture.Reload(new ForbiddenRandom());
        Assert.False(await restarted.EnsureHatchedAsync());
        Assert.Equal(new[] { 1, 2, 4 }, restarted.ExportStateSnapshot().ActivePokemon!.PlannedPathIds);
    }

    [Fact]
    public async Task Failed_tree_lookup_retains_growth_instead_of_graduating_a_truncated_plan()
    {
        using var fixture = new Fixture();
        var provider = new Provider { Fail = true };
        var state = ActiveState([1, 2]);
        state.ActivePokemon!.UsedAtStage = 0;
        var store = fixture.Store(state, provider);
        store.Update(new Dictionary<string, long> { ["codex"] = 500_000_000 }, Today, true);
        Assert.False(await store.EnsureHatchedAsync());
        Assert.Equal(2, store.CurrentSpeciesId);
        Assert.Equal(500_000_000, store.ActiveUsedAtStage);
        Assert.Equal(0, store.DexCount);
        provider.Fail = false;
        Assert.True(await store.EnsureHatchedAsync());
        Assert.Equal(3, store.CurrentSpeciesId);
        Assert.Equal(250_000_000, store.ActiveUsedAtStage);
        Assert.Equal(0, store.DexCount);
    }

    [Fact]
    public async Task Metadata_that_cannot_explain_current_form_does_not_roll_back_the_save()
    {
        using var fixture = new Fixture();
        var state = ActiveState([1, 4]);
        state.ActivePokemon!.PathIds = [1, 4];
        var store = fixture.Store(state);
        Assert.False(await store.EnsureHatchedAsync());
        Assert.Equal(4, store.CurrentSpeciesId);
        Assert.Equal(123, store.ActiveUsedAtStage);
    }

    private static CompanionState ActiveState(List<int> plan) => new()
    {
        InstallBaselineSet = true, LastDate = "2026-09-30",
        ClaimedTodayTokensByProvider = new() { ["codex"] = 0 },
        ActivePokemon = new PokemonMonState
        {
            BaseId = 1, PathIds = [1, 2], PlannedPathIds = plan, StageIndex = 1,
            TotalForms = plan.Count, UsedAtStage = 123, Rarity = PokemonRarity.Common,
            IsShiny = true, Nature = PokemonNature.Jolly,
        },
    };
    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "PTB-Evolution-" + Guid.NewGuid().ToString("N"));
        private string FilePath => Path.Combine(_directory, "state.json");
        public CompanionStore Store(CompanionState state, Provider? provider = null, IRandomSource? random = null)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(state,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
            return new(FilePath, provider ?? new Provider(), random ?? new FirstRandom());
        }
        public CompanionStore Reload(IRandomSource random) => new(FilePath, new Provider(), random);
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
    private sealed class FirstRandom : IRandomSource { public long NextInt64(long maximum) => 0; }
    private sealed class ForbiddenRandom : IRandomSource
    { public long NextInt64(long maximum) => throw new InvalidOperationException("A complete saved route must not be rerolled."); }
    private sealed class Provider : IPokemonProvider
    {
        public bool Fail { get; set; }
        public Task<IReadOnlyList<BasePokemonSpecies>> GetBaseSpeciesIndexAsync(CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<BasePokemonSpecies>>([new(1, 255)]);
        public Task<BasePokemonSpecies?> GetBaseSpeciesAsync(int id, CancellationToken token = default) =>
            Task.FromResult<BasePokemonSpecies?>(new(id, 255));
        public Task<PokemonEvolutionLine> GetEvolutionLineAsync(int id, CancellationToken token = default) => Fail
            ? Task.FromException<PokemonEvolutionLine>(new IOException("offline"))
            : Task.FromResult(new PokemonEvolutionLine(1, Tree, PokemonRarity.Common,
                new Dictionary<int, string> { [1] = "A", [2] = "B", [3] = "C", [4] = "D" }));
    }
}
