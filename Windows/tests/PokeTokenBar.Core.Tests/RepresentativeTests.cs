using System.Text.Json;
using PokeTokenBar.Core;

namespace PokeTokenBar.Core.Tests;

public sealed class RepresentativeTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    [Fact]
    public void Default_tracks_current_form_and_pin_does_not_redirect_growth_or_ledger()
    {
        using var fixture = new Fixture();
        var store = fixture.Store(State());
        Assert.False(store.Representative.IsPinned);
        Assert.Equal(1, store.Representative.SpeciesId);
        Assert.True(store.SetRepresentativeSpecies(6));
        store.Update(new Dictionary<string, long> { ["codex"] = 125_000_000 }, Today, true);
        Assert.Equal(2, store.CurrentSpeciesId);
        Assert.Equal(123, store.ActiveUsedAtStage);
        Assert.Equal(6, store.Representative.SpeciesId);
        Assert.Equal("리자몽", store.Representative.Name);
        Assert.Equal(9_125_000_000, store.UsedSinceInstall);
        Assert.Equal(125_000_000, store.ClaimedTodayTokensByProvider!["codex"]);
        Assert.True(store.SetRepresentativeSpecies(null));
        Assert.Equal(2, store.Representative.SpeciesId);
    }

    [Fact]
    public void Selection_accepts_reached_forms_but_rejects_future_or_unknown_forms_without_clearing_pin()
    {
        using var fixture = new Fixture();
        var store = fixture.Store(State());
        Assert.True(store.SetRepresentativeSpecies(6));
        Assert.False(store.SetRepresentativeSpecies(2));
        Assert.False(store.SetRepresentativeSpecies(132));
        Assert.False(store.SetRepresentativeSpecies(999));
        Assert.Equal(6, store.RepresentativeSpeciesId);
        Assert.True(store.SetRepresentativeSpecies(1));
        Assert.Equal(1, store.RepresentativeSpeciesId);
    }

    [Fact]
    public async Task Pin_survives_restart_transfer_and_new_egg_while_active_companion_is_archived()
    {
        using var fixture = new Fixture();
        var store = fixture.Store(State());
        Assert.True(store.SetRepresentativeSpecies(1));
        Assert.True(store.BuyFreshEgg(FreshEggTier.Basic));
        Assert.False(store.HasActivePokemon);
        Assert.True(store.Representative.IsPinned);
        Assert.Equal(1, store.Representative.SpeciesId);
        store = fixture.Reload();
        Assert.Equal(1, store.RepresentativeSpeciesId);
        var envelope = SaveTransfer.Decode(SaveTransfer.Encode(store.ExportStateSnapshot(), "test", "test", DateTimeOffset.Now));
        using var target = new Fixture();
        var imported = target.Store(new CompanionState());
        await imported.ImportStateAsync(envelope.State, new Dictionary<string, long>(), Today, false, DateTimeOffset.Now);
        Assert.Equal(1, imported.Representative.SpeciesId);
        Assert.True(imported.Representative.IsPinned);
        imported.SetRepresentativeSpecies(null);
        Assert.Null(imported.Representative.SpeciesId);
    }

    [Fact]
    public void Invalid_loaded_pin_falls_back_without_changing_progress_and_shiny_ownership_is_aggregated()
    {
        using var fixture = new Fixture();
        var state = State();
        state.RepresentativeSpeciesId = 999;
        state.Dex.Add(new PokemonDexEntry { BaseId = 4, FinalId = 6, ChainOrder = [4, 5, 6], IsShiny = true });
        var store = fixture.Store(state);
        Assert.Null(store.RepresentativeSpeciesId);
        Assert.Equal(123, store.ActiveUsedAtStage);
        Assert.True(store.SetRepresentativeSpecies(6));
        Assert.True(store.Representative.IsShiny);
        Assert.True(store.DexSpecies.Single(item => item.SpeciesId == 6).HasNormal);
        Assert.True(store.DexSpecies.Single(item => item.SpeciesId == 6).IsShiny);
    }

    [Fact]
    public void Dex_marks_only_the_current_form_as_raising_even_when_the_species_was_collected_before()
    {
        using var fixture = new Fixture();
        var state = State();
        state.ActivePokemon!.PathIds = [1, 2];
        state.ActivePokemon.StageIndex = 1;
        state.Dex.Add(new PokemonDexEntry { BaseId = 1, FinalId = 3, ChainOrder = [1, 2, 3] });
        var store = fixture.Store(state);
        Assert.Equal(2, Assert.Single(store.DexSpecies, item => item.IsRaising).SpeciesId);
    }

    [Fact]
    public void Failed_pin_save_does_not_change_the_existing_selection_or_progress()
    {
        using var fixture = new Fixture();
        var store = fixture.Store(State());
        Assert.True(store.SetRepresentativeSpecies(1));
        using (var locked = new FileStream(fixture.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(store.SetRepresentativeSpecies(6));
            Assert.Equal(1, store.RepresentativeSpeciesId);
            Assert.Equal(123, store.ActiveUsedAtStage);
        }
        Assert.Equal(1, fixture.Reload().RepresentativeSpeciesId);
        Assert.True(store.SetRepresentativeSpecies(6));
        Assert.Equal(6, fixture.Reload().RepresentativeSpeciesId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ditto_reveal_clears_only_a_pin_whose_apparent_species_is_no_longer_owned(bool archivedDisguise)
    {
        using var fixture = new Fixture();
        var state = State();
        state.Dex.Clear();
        if (archivedDisguise) state.Dex.Add(new PokemonDexEntry { BaseId = 1, FinalId = 2, ChainOrder = [1, 2] });
        var mon = state.ActivePokemon!;
        mon.PlannedPathIds = [1, 2];
        mon.TotalForms = 2;
        mon.DittoDisguise = 1;
        mon.IsShiny = true;
        mon.UsedAtStage = PokemonBalance.PhaseThreshold(PokemonRarity.Common, 2, 0) + 7;
        var store = fixture.Store(state, new Provider());
        Assert.True(store.SetRepresentativeSpecies(1));
        Assert.False(store.Representative.IsShiny); // No hidden shiny identity leaks through pinning.
        Assert.True(await store.EnsureHatchedAsync());
        Assert.Equal(132, store.CurrentSpeciesId);
        Assert.Equal(7, store.ActiveUsedAtStage);
        Assert.Equal(archivedDisguise ? 1 : 132, store.Representative.SpeciesId);
        Assert.Equal(archivedDisguise, store.Representative.IsPinned);
    }

    private static CompanionState State() => new()
    {
        InstallBaselineSet = true, LastDate = "2026-10-06", UsedSinceInstall = 9_000_000_000,
        ClaimedTodayTokensByProvider = new() { ["codex"] = 0 },
        ActivePokemon = new PokemonMonState
        {
            BaseId = 1, PathIds = [1], PlannedPathIds = [1, 2, 3], TotalForms = 3,
            Rarity = PokemonRarity.Common, UsedAtStage = 123, Names = new() { [1] = "이상해씨", [2] = "이상해풀", [3] = "이상해꽃" },
        },
        Dex = [new PokemonDexEntry { BaseId = 4, FinalId = 6, ChainOrder = [4, 5, 6],
            Names = new() { [4] = "파이리", [5] = "리자드", [6] = "리자몽" } }],
    };

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "PTB-Representative-" + Guid.NewGuid().ToString("N"));
        public string FilePath => Path.Combine(_directory, "state.json");
        public CompanionStore Store(CompanionState state, IPokemonProvider? provider = null)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return new(FilePath, provider);
        }
        public CompanionStore Reload() => new(FilePath);
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    private sealed class Provider : IPokemonProvider
    {
        public Task<IReadOnlyList<BasePokemonSpecies>> GetBaseSpeciesIndexAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<BasePokemonSpecies>>([]);
        public Task<BasePokemonSpecies?> GetBaseSpeciesAsync(int id, CancellationToken token = default) => Task.FromResult<BasePokemonSpecies?>(null);
        public Task<PokemonEvolutionLine> GetEvolutionLineAsync(int id, CancellationToken token = default) => Task.FromResult(
            new PokemonEvolutionLine(id, id == 132 ? new(id, []) : new(id, [new(2, [])]),
                id == 132 ? PokemonRarity.Rare : PokemonRarity.Common, new Dictionary<int, string>()));
    }
}
