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

    [Fact]
    public async Task Ready_egg_hatches_into_a_real_pokemon_and_persists_it()
    {
        using var temporary = TemporaryDirectory.Create();
        var provider = FakePokemonProvider.BulbasaurLine();
        var store = new CompanionStore(
            StatePath(temporary),
            provider,
            new ConstantRandomSource(1));
        Update(store, new Dictionary<string, long> { ["codex"] = 0 });
        Update(store, new Dictionary<string, long>
        {
            ["codex"] = PokemonBalance.EggHatchThreshold,
        });

        var hatched = await store.EnsureHatchedAsync();
        var reloaded = CreateStore(temporary);

        Assert.True(hatched);
        Assert.True(store.HasActivePokemon);
        Assert.Equal(1, store.CurrentSpeciesId);
        Assert.Equal("이상해씨", store.CurrentPokemonName);
        Assert.False(store.IsCurrentPokemonShiny);
        Assert.Equal(0, store.EggUsage);
        Assert.True(reloaded.HasActivePokemon);
        Assert.Equal("이상해씨", reloaded.CurrentPokemonName);
    }

    [Fact]
    public async Task Active_line_hides_future_species_and_dex_exposes_only_realized_forms()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = new CompanionStore(
            StatePath(temporary),
            FakePokemonProvider.BulbasaurLine(),
            new ConstantRandomSource(1));
        Update(store, new Dictionary<string, long> { ["codex"] = 0 });
        Update(store, new Dictionary<string, long>
        {
            ["codex"] = PokemonBalance.EggHatchThreshold,
        });

        await store.EnsureHatchedAsync();

        Assert.Collection(
            store.CurrentLineStages,
            stage =>
            {
                Assert.Equal(1, stage.SpeciesId);
                Assert.Equal("이상해씨", stage.Name);
                Assert.Equal(PokemonLineStageStatus.Current, stage.Status);
            },
            stage =>
            {
                Assert.Null(stage.SpeciesId);
                Assert.Equal("???", stage.Name);
                Assert.Equal(PokemonLineStageStatus.HiddenFuture, stage.Status);
            },
            stage => Assert.Equal(PokemonLineStageStatus.HiddenFuture, stage.Status));
        var dexSpecies = Assert.Single(store.DexSpecies);
        Assert.Equal(1, dexSpecies.SpeciesId);
        Assert.True(dexSpecies.IsRaising);
        var activeEntry = Assert.Single(store.CollectionEntries);
        Assert.True(activeEntry.IsRaising);
        Assert.Null(activeEntry.CaughtAt);
        Assert.Equal("이상해씨", activeEntry.Names[1]);
    }

    [Fact]
    public async Task Hatch_overflow_is_carried_into_growth_and_can_evolve_immediately()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = new CompanionStore(
            StatePath(temporary),
            FakePokemonProvider.BulbasaurLine(),
            new ConstantRandomSource(1));
        var firstPhase = PokemonBalance.PhaseThreshold(PokemonRarity.Common, 3, 0);
        Update(store, new Dictionary<string, long> { ["codex"] = 0 });
        Update(store, new Dictionary<string, long>
        {
            ["codex"] = PokemonBalance.EggHatchThreshold + firstPhase + 123,
        });

        await store.EnsureHatchedAsync();

        Assert.Equal(2, store.CurrentSpeciesId);
        Assert.Equal("이상해풀", store.CurrentPokemonName);
        Assert.Equal(123, store.ActiveUsedAtStage);
        Assert.Equal(new[] { 1, 2 }, store.CurrentEvolutionPath);
    }

    [Fact]
    public async Task Branch_is_chosen_once_and_the_saved_route_drives_evolution()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = new CompanionStore(
            StatePath(temporary),
            FakePokemonProvider.BranchingLine(),
            new SequenceRandomSource(0, 1, 1, 1));
        Update(store, new Dictionary<string, long> { ["codex"] = 0 });
        Update(store, new Dictionary<string, long>
        {
            ["codex"] = PokemonBalance.EggHatchThreshold,
        });
        await store.EnsureHatchedAsync();
        var phase = PokemonBalance.PhaseThreshold(PokemonRarity.Common, 2, 0);

        Update(store, new Dictionary<string, long>
        {
            ["codex"] = PokemonBalance.EggHatchThreshold + phase,
        });

        Assert.Equal(4, store.CurrentSpeciesId);
        Assert.Equal("이상해꽃-B", store.CurrentPokemonName);
    }

    [Fact]
    public async Task Final_growth_phase_moves_the_pokemon_to_the_dex_and_starts_a_fresh_egg()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = new CompanionStore(
            StatePath(temporary),
            FakePokemonProvider.BulbasaurLine(),
            new ConstantRandomSource(1));
        Update(store, new Dictionary<string, long> { ["codex"] = 0 });
        Update(store, new Dictionary<string, long>
        {
            ["codex"] = PokemonBalance.EggHatchThreshold,
        });
        await store.EnsureHatchedAsync();

        Update(store, new Dictionary<string, long>
        {
            ["codex"] = PokemonBalance.EggHatchThreshold
                + PokemonBalance.GraduationTotal(PokemonRarity.Common),
        });

        Assert.False(store.HasActivePokemon);
        Assert.Equal(1, store.DexCount);
        Assert.Equal(0, store.EggUsage);
        Assert.Equal(new[] { 1, 2, 3 }, store.DexSpecies.Select(item => item.SpeciesId));
        Assert.All(store.DexSpecies, item => Assert.False(item.IsRaising));
        var graduated = Assert.Single(store.CollectionEntries);
        Assert.False(graduated.IsRaising);
        Assert.Equal("이상해꽃", graduated.FinalName);
        Assert.NotNull(graduated.CaughtAt);

        var reloaded = CreateStore(temporary);
        Assert.Equal(new[] { 1, 2, 3 }, reloaded.DexSpecies.Select(item => item.SpeciesId));
        Assert.Equal("이상해꽃", Assert.Single(reloaded.CollectionEntries).FinalName);
    }

    [Fact]
    public void Fresh_store_has_an_empty_collection()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(temporary);

        Assert.Empty(store.CurrentLineStages);
        Assert.Empty(store.CollectionEntries);
        Assert.Empty(store.DexSpecies);
    }

    [Fact]
    public void Permanent_species_wins_raising_status_while_shiny_discovery_is_merged()
    {
        using var temporary = TemporaryDirectory.Create();
        File.WriteAllText(
            StatePath(temporary),
            """
            {
              "activePokemon": {
                "baseId": 1,
                "pathIds": [1],
                "plannedPathIds": [1, 2, 3],
                "stageIndex": 0,
                "rarity": "common",
                "totalForms": 3,
                "isShiny": true,
                "nature": "hardy",
                "names": { "1": "이상해씨", "2": "이상해풀", "3": "이상해꽃" }
              },
              "dex": [{
                "id": "graduated-one",
                "baseId": 1,
                "finalId": 2,
                "chainOrder": [1, 2],
                "rarity": "common",
                "caughtAt": "2026-08-26T00:00:00Z",
                "isShiny": false,
                "nature": "hardy",
                "names": { "1": "이상해씨", "2": "이상해풀" }
              }]
            }
            """,
            new UTF8Encoding(false));

        var store = CreateStore(temporary);

        Assert.Equal(new[] { 1, 2 }, store.DexSpecies.Select(item => item.SpeciesId));
        var bulbasaur = store.DexSpecies[0];
        Assert.False(bulbasaur.IsRaising);
        Assert.True(bulbasaur.IsShiny);
        Assert.Equal(2, store.CollectionEntries.Count);
        Assert.True(store.CollectionEntries[0].IsRaising);
        Assert.Equal("graduated-one", store.CollectionEntries[1].Id);
    }

    [Fact]
    public void Graduated_collection_entries_are_sorted_newest_first()
    {
        using var temporary = TemporaryDirectory.Create();
        File.WriteAllText(
            StatePath(temporary),
            """
            {
              "dex": [
                {
                  "id": "older",
                  "baseId": 1,
                  "finalId": 1,
                  "chainOrder": [1],
                  "rarity": "common",
                  "caughtAt": "2026-08-20T00:00:00Z",
                  "nature": "hardy",
                  "names": { "1": "이상해씨" }
                },
                {
                  "id": "newer",
                  "baseId": 4,
                  "finalId": 4,
                  "chainOrder": [4],
                  "rarity": "common",
                  "caughtAt": "2026-08-30T00:00:00Z",
                  "nature": "brave",
                  "names": { "4": "파이리" }
                }
              ]
            }
            """,
            new UTF8Encoding(false));

        var store = CreateStore(temporary);

        Assert.Equal(new[] { "newer", "older" }, store.CollectionEntries.Select(item => item.Id));
        Assert.Equal("파이리", store.CollectionEntries[0].FinalName);
        Assert.Equal(PokemonNature.Brave, store.CollectionEntries[0].Nature);
    }

    [Fact]
    public async Task Failed_line_fetch_keeps_the_ready_egg_for_a_later_retry()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = new CompanionStore(
            StatePath(temporary),
            new FailingPokemonProvider(),
            new ConstantRandomSource(1));
        Update(store, new Dictionary<string, long> { ["codex"] = 0 });
        Update(store, new Dictionary<string, long>
        {
            ["codex"] = PokemonBalance.EggHatchThreshold,
        });

        var hatched = await store.EnsureHatchedAsync();

        Assert.False(hatched);
        Assert.True(store.ReadyToHatch);
        Assert.False(store.HasActivePokemon);
        Assert.Equal(PokemonBalance.EggHatchThreshold, store.EggUsage);
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

    private sealed class ConstantRandomSource(long value) : IRandomSource
    {
        public long NextInt64(long maxExclusive) => Math.Min(value, maxExclusive - 1);
    }

    private sealed class SequenceRandomSource(params long[] values) : IRandomSource
    {
        private readonly Queue<long> _values = new(values);

        public long NextInt64(long maxExclusive)
        {
            var value = _values.Count > 0 ? _values.Dequeue() : 0;
            return Math.Clamp(value, 0, maxExclusive - 1);
        }
    }

    private sealed class FakePokemonProvider(
        IReadOnlyList<BasePokemonSpecies> index,
        PokemonEvolutionLine line) : IPokemonProvider
    {
        public static FakePokemonProvider BulbasaurLine()
        {
            var tree = new PokemonEvolutionNode(1,
            [
                new PokemonEvolutionNode(2,
                [
                    new PokemonEvolutionNode(3, []),
                ]),
            ]);
            return new FakePokemonProvider(
                [new BasePokemonSpecies(1, 255)],
                new PokemonEvolutionLine(
                    1,
                    tree,
                    PokemonRarity.Common,
                    new Dictionary<int, string>
                    {
                        [1] = "이상해씨",
                        [2] = "이상해풀",
                        [3] = "이상해꽃",
                    }));
        }

        public static FakePokemonProvider BranchingLine()
        {
            var tree = new PokemonEvolutionNode(1,
            [
                new PokemonEvolutionNode(3, []),
                new PokemonEvolutionNode(4, []),
            ]);
            return new FakePokemonProvider(
                [new BasePokemonSpecies(1, 255)],
                new PokemonEvolutionLine(
                    1,
                    tree,
                    PokemonRarity.Common,
                    new Dictionary<int, string>
                    {
                        [1] = "이상해씨",
                        [3] = "이상해꽃-A",
                        [4] = "이상해꽃-B",
                    }));
        }

        public Task<IReadOnlyList<BasePokemonSpecies>> GetBaseSpeciesIndexAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(index);

        public Task<BasePokemonSpecies?> GetBaseSpeciesAsync(
            int speciesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BasePokemonSpecies?>(index.FirstOrDefault(item => item.Id == speciesId));

        public Task<PokemonEvolutionLine> GetEvolutionLineAsync(
            int baseSpeciesId,
            CancellationToken cancellationToken = default) => Task.FromResult(line);
    }

    private sealed class FailingPokemonProvider : IPokemonProvider
    {
        public Task<IReadOnlyList<BasePokemonSpecies>> GetBaseSpeciesIndexAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BasePokemonSpecies>>([new BasePokemonSpecies(1, 255)]);

        public Task<BasePokemonSpecies?> GetBaseSpeciesAsync(
            int speciesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BasePokemonSpecies?>(new BasePokemonSpecies(speciesId, 255));

        public Task<PokemonEvolutionLine> GetEvolutionLineAsync(
            int baseSpeciesId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<PokemonEvolutionLine>(new HttpRequestException("offline"));
    }
}
