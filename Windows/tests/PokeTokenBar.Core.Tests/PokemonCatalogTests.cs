using System.Text.Json;

namespace PokeTokenBar.Core.Tests;

public sealed class PokemonCatalogTests
{
    private static BundledPokemonCatalog Catalog => BundledPokemonCatalog.Default;

    [Fact]
    public async Task All_species_are_localized_and_reachable_from_an_offline_base_tree()
    {
        Assert.Equal(1025, Catalog.Species.Count);
        Assert.Equal(Enumerable.Range(1, 9), Catalog.Species.Select(s => s.Generation).Distinct().Order());
        var reachable = new HashSet<int> { 132 };
        var index = await Catalog.GetBaseSpeciesIndexAsync();
        foreach (var entry in index)
        {
            var line = await Catalog.GetEvolutionLineAsync(entry.Id);
            Assert.Equal(PokemonBalance.RarityFrom(entry.CaptureRate, entry.IsLegendary, entry.IsMythical), line.Rarity);
            foreach (var id in line.Tree.SpeciesIds())
            {
                Assert.True(reachable.Add(id), $"Duplicated ancestry for #{id}");
                Assert.NotEqual($"#{id}", line.NameFor(id));
            }
        }
        Assert.Equal(Enumerable.Range(1, 1025), reachable.Order());
        Assert.Null(await Catalog.GetBaseSpeciesAsync(132));
        Assert.Null(await Catalog.GetBaseSpeciesAsync(1026));
    }

    [Theory]
    [InlineData(650, 652, "도치마론", PokemonRarity.Rare)]
    [InlineData(722, 724, "나몰빼미", PokemonRarity.Rare)]
    [InlineData(810, 812, "흥나숭", PokemonRarity.Rare)]
    [InlineData(906, 908, "나오하", PokemonRarity.Rare)]
    [InlineData(1007, 1007, "코라이돈", PokemonRarity.Legendary)]
    [InlineData(1025, 1025, "복숭악동", PokemonRarity.Legendary)]
    public async Task New_generations_use_their_real_names_evolution_paths_and_rarity(int root, int final,
        string name, PokemonRarity rarity)
    {
        var line = await Catalog.GetEvolutionLineAsync(root);
        Assert.Equal(name, line.NameFor(root));
        Assert.Equal(rarity, line.Rarity);
        Assert.Contains(final, line.Tree.SpeciesIds());
    }

    [Fact]
    public async Task Every_normal_and_shiny_sprite_is_bundled_and_served_without_network()
    {
        using var http = new HttpClient(new OfflineHandler());
        var root = Path.Combine(Path.GetTempPath(), $"PokeCatalog-{Guid.NewGuid():N}");
        try
        {
            var sprites = new PokemonSpriteStore(http, root, Catalog);
            for (var id = 1; id <= 1025; id++)
                foreach (var shiny in new[] { false, true })
                {
                    var bytes = await sprites.GetSpriteAsync(id, shiny);
                    Assert.NotNull(bytes);
                    Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.Take(8));
                    Assert.Equal(96, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
                    Assert.Equal(96, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
                }
            Assert.Null(await sprites.GetSpriteAsync(1026, false));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Existing_bisharp_plan_and_growth_survive_new_kingambit_metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PokeLegacy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "state.json");
            var old = new CompanionState { InstallBaselineSet = true, LastDate = "2026-10-07", ClaimedTodayTokensByProvider = new() { ["codex"] = 10 },
                ActivePokemon = new PokemonMonState { BaseId = 624, PathIds = [624, 625], PlannedPathIds = [624, 625], StageIndex = 1,
                    TotalForms = 2, Rarity = PokemonRarity.Rare, UsedAtStage = 750_000_000, Names = new() { [624] = "자망칼", [625] = "절각참" } } };
            File.WriteAllText(path, JsonSerializer.Serialize(old));
            var store = new CompanionStore(path, Catalog);
            await store.EnsureHatchedAsync();
            var actual = store.ExportStateSnapshot().ActivePokemon!;
            Assert.Equal(new[] { 624, 625 }, actual.PlannedPathIds);
            Assert.Equal(750_000_000, actual.UsedAtStage);
            Assert.Equal(1_250_000_000, store.TokensToNextStage);
            var expanded = await Catalog.GetEvolutionLineAsync(624);
            Assert.Contains(983, expanded.Tree.SpeciesIds());
            var transfer = SaveTransfer.Decode(SaveTransfer.Encode(store.ExportStateSnapshot(), "1.0.0", "test", DateTimeOffset.UtcNow));
            Assert.Equal(649, transfer.State.ActivePokemon!.EvolutionCatalogMaximumSpeciesId);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(650)] [InlineData(722)] [InlineData(810)] [InlineData(906)] [InlineData(1025)]
    public void New_species_survive_save_export_import_and_reload(int id)
    {
        var state = new CompanionState { ActivePokemon = new PokemonMonState { BaseId = id, PathIds = [id], PlannedPathIds = [id], TotalForms = 1,
            EvolutionCatalogMaximumSpeciesId = 1025, Rarity = PokemonRarity.Rare, UsedAtStage = 12_345 },
            Dex = [new PokemonDexEntry { BaseId = id, FinalId = id, ChainOrder = [id], Rarity = PokemonRarity.Rare, Names = new() { [id] = "test" } }] };
        var restored = SaveTransfer.Decode(SaveTransfer.Encode(state, "1.0.0", "test", DateTimeOffset.UtcNow)).State;
        Assert.Equal(id, restored.ActivePokemon!.BaseId);
        Assert.Equal(12_345, restored.ActivePokemon.UsedAtStage);
        Assert.Equal(1025, restored.ActivePokemon.EvolutionCatalogMaximumSpeciesId);
        Assert.Single(restored.Dex);
    }

    [Theory]
    [InlineData(650)] [InlineData(722)] [InlineData(810)] [InlineData(906)] [InlineData(1007)] [InlineData(1025)]
    public async Task New_generations_hatch_evolve_graduate_and_gain_repeat_bonus(int id)
    {
        var root = Path.Combine(Path.GetTempPath(), $"PokeLifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var date = new DateOnly(2026, 10, 7);
            var path = Path.Combine(root, "state.json");
            var state = new CompanionState { InstallBaselineSet = true, LastDate = "2026-10-07",
                ClaimedTodayTokensByProvider = new() { ["codex"] = 0 }, EggUsage = 5_000_000, PendingHatchId = id };
            File.WriteAllText(path, JsonSerializer.Serialize(state));
            var store = new CompanionStore(path, Catalog);
            Assert.True(await store.EnsureHatchedAsync());
            Assert.Equal(id, store.CurrentSpeciesId);
            Assert.Equal(1025, store.ExportStateSnapshot().ActivePokemon!.EvolutionCatalogMaximumSpeciesId);
            var rarity = store.CurrentPokemonRarity!.Value;
            long used = 0;
            var forms = store.TotalForms;
            for (var stage = 1; stage <= forms; stage++)
            {
                used += store.TokensToNextStage;
                store.Update(new Dictionary<string, long> { ["codex"] = used }, date, true);
                if (stage < forms) Assert.Equal(stage + 1, store.CurrentStage);
            }
            Assert.False(store.HasActivePokemon);
            Assert.Equal(1, store.DexCount);
            Assert.Equal(PokemonBalance.GraduationTotal(rarity), used);
            var graduated = store.ExportStateSnapshot();
            graduated.EggUsage = 5_000_000;
            graduated.PendingHatchId = id;
            File.WriteAllText(path, JsonSerializer.Serialize(graduated));
            var repeated = new CompanionStore(path, Catalog);
            Assert.True(await repeated.EnsureHatchedAsync());
            Assert.Equal(2, repeated.CurrentGrowthMultiplier);
            Assert.Equal(PokemonBalance.PhaseThreshold(rarity, repeated.TotalForms, 0, 2), repeated.TokensToNextStage);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => throw new HttpRequestException("Network must never be called for bundled catalog assets.");
    }
}
