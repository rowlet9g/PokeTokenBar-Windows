using PokeTokenBar.Core;

namespace PokeTokenBar.Core.Tests;

public sealed class DittoTests
{
    private static readonly DateOnly Today = new(2026, 9, 30);

    [Theory]
    [InlineData(PokemonRarity.Common, 2, 0, true)]
    [InlineData(PokemonRarity.Common, 3, 128, true)]
    [InlineData(PokemonRarity.Common, 3, 127, false)]
    [InlineData(PokemonRarity.Common, 1, 0, false)]
    [InlineData(PokemonRarity.Rare, 3, 0, false)]
    public void Disguise_roll_is_restricted_to_common_evolving_species(PokemonRarity rarity,
        int forms, long roll, bool hit) => Assert.Equal(hit, CompanionStore.DittoDisguiseHit(rarity, forms, roll));

    [Fact]
    public async Task Hatch_hides_shiny_and_restart_reveals_at_first_threshold_with_overflow()
    {
        using var fixture = new Fixture();
        var store = fixture.Store();
        store.Update(new Dictionary<string, long> { ["codex"] = 0 }, Today, true);
        store.Update(new Dictionary<string, long> { ["codex"] = PokemonBalance.EggHatchThreshold }, Today, true);
        Assert.True(await store.EnsureHatchedAsync());
        Assert.Equal(1, store.CurrentSpeciesId);
        Assert.True(store.ExportStateSnapshot().ActivePokemon!.IsShiny);
        Assert.False(store.IsCurrentPokemonShiny);
        Assert.False(Assert.Single(store.DexSpecies).IsShiny);
        Assert.False(Assert.Single(store.CollectionEntries).IsShiny);
        Assert.All(await store.GetEvolutionPreviewAsync(), stage => Assert.False(stage.IsShiny));

        store = fixture.Store();
        var threshold = PokemonBalance.PhaseThreshold(PokemonRarity.Common, 2, 0);
        store.Update(new Dictionary<string, long> { ["codex"] = PokemonBalance.EggHatchThreshold + threshold + 123 }, Today, true);
        Assert.Equal(1, store.CurrentSpeciesId); // Not a real evolution.
        Assert.True(await store.EnsureHatchedAsync());
        Assert.Equal(132, store.CurrentSpeciesId);
        Assert.True(store.IsCurrentPokemonShiny);
        var revealed = store.ExportStateSnapshot().ActivePokemon!;
        Assert.Equal(123, revealed.UsedAtStage);
        Assert.Equal(PokemonRarity.Rare, revealed.Rarity);
        Assert.Equal(1, revealed.TotalForms);
        Assert.Equal(new[] { 132 }, revealed.PathIds);
        Assert.Equal(new[] { 132 }, store.DexSpecies.Select(species => species.SpeciesId));
        var nature = revealed.Nature;
        store = fixture.Store();
        Assert.True(store.IsCurrentPokemonRevealedDitto);
        Assert.Equal(nature, store.CurrentPokemonNature);
        Assert.False(await store.EnsureHatchedAsync());
        var imported = SaveTransfer.Decode(SaveTransfer.Encode(store.ExportStateSnapshot(), "test", "test", DateTimeOffset.Now));
        Assert.True(imported.State.ActivePokemon!.DittoRevealed);
        Assert.Equal(123, imported.State.ActivePokemon.UsedAtStage);
    }

    [Fact]
    public async Task Failed_metadata_request_keeps_progress_and_retries_without_evolution()
    {
        using var fixture = new Fixture();
        var provider = new Provider { FailDitto = true };
        var store = fixture.Store(provider);
        await fixture.ImportDisguise(store);
        Assert.False(await store.EnsureHatchedAsync());
        Assert.Equal(1, store.CurrentSpeciesId);
        Assert.Equal(7, store.ExportStateSnapshot().ActivePokemon!.UsedAtStage - fixture.Threshold);
        provider.FailDitto = false;
        Assert.True(await store.EnsureHatchedAsync());
        Assert.Equal(132, store.CurrentSpeciesId);
        Assert.Equal(7, store.ExportStateSnapshot().ActivePokemon!.UsedAtStage);
    }

    [Fact]
    public async Task Reveal_does_not_overwrite_subject_replaced_during_request()
    {
        using var fixture = new Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<PokemonEvolutionLine>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider { PendingDitto = result, Started = started };
        var store = fixture.Store(provider);
        await fixture.ImportDisguise(store);
        var reveal = store.EnsureHatchedAsync();
        await started.Task;
        // A shop egg purchase may replace the subject without taking the hatch gate.
        Assert.True(store.BuyFreshEgg(FreshEggTier.Basic));
        result.SetResult(Provider.Ditto);
        Assert.False(await reveal);
        Assert.False(store.HasActivePokemon);
        Assert.DoesNotContain(store.DexSpecies, species => species.SpeciesId == 132);
    }

    [Fact]
    public void Reveal_has_its_own_once_only_milestone()
    {
        var disguised = new CompanionMilestoneSnapshot(1, "이상해씨", 1, false, 0);
        var revealed = new CompanionMilestoneSnapshot(132, "메타몽", 1, true, 0, true);
        var tracker = new CompanionMilestoneTracker(disguised);
        Assert.Equal(CompanionMilestoneKind.DittoRevealed, tracker.Observe(revealed)!.Kind);
        Assert.Null(tracker.Observe(revealed));
        Assert.Null(new CompanionMilestoneTracker(revealed).Observe(revealed));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "PTB-Ditto-" + Guid.NewGuid().ToString("N"));
        public long Threshold => PokemonBalance.PhaseThreshold(PokemonRarity.Common, 2, 0);
        public CompanionStore Store(Provider? provider = null) => new(Path.Combine(_directory, "state.json"),
            provider ?? new Provider(), new ZeroRandom(), dittoDisguiseRollingEnabled: true);
        public Task ImportDisguise(CompanionStore store) => store.ImportStateAsync(new CompanionState
        {
            UsedSinceInstall = 10_000_000_000,
            ActivePokemon = new PokemonMonState
            {
                BaseId = 1, PathIds = [1], PlannedPathIds = [1, 2], TotalForms = 2,
                Rarity = PokemonRarity.Common, DittoDisguise = 1, IsShiny = true,
                Nature = PokemonNature.Jolly, UsedAtStage = Threshold + 7,
            },
        }, new Dictionary<string, long> { ["codex"] = 0 }, Today, true, DateTimeOffset.Now);
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    private sealed class ZeroRandom : IRandomSource { public long NextInt64(long maxExclusive) => 0; }
    private sealed class Provider : IPokemonProvider
    {
        public bool FailDitto { get; set; }
        public TaskCompletionSource<PokemonEvolutionLine>? PendingDitto { get; init; }
        public TaskCompletionSource? Started { get; init; }
        public static PokemonEvolutionLine Ditto => new(132, new(132, []), PokemonRarity.Rare,
            new Dictionary<int, string> { [132] = "메타몽" });
        public Task<IReadOnlyList<BasePokemonSpecies>> GetBaseSpeciesIndexAsync(CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<BasePokemonSpecies>>([new(1, 255)]);
        public Task<BasePokemonSpecies?> GetBaseSpeciesAsync(int id, CancellationToken token = default) =>
            Task.FromResult<BasePokemonSpecies?>(new(id, 255));
        public Task<PokemonEvolutionLine> GetEvolutionLineAsync(int id, CancellationToken token = default)
        {
            if (id != 132) return Task.FromResult(new PokemonEvolutionLine(1, new(1, [new(2, [])]),
                PokemonRarity.Common, new Dictionary<int, string> { [1] = "이상해씨", [2] = "이상해풀" }));
            Started?.TrySetResult();
            return PendingDitto?.Task ?? (FailDitto
                ? Task.FromException<PokemonEvolutionLine>(new IOException("offline")) : Task.FromResult(Ditto));
        }
    }
}
