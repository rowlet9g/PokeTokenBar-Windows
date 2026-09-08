using System.Text.Json;
using PokeTokenBar.Core;

namespace PokeTokenBar.Core.Tests;

public sealed class CompanionItemTests
{
    [Fact]
    public void Buying_an_item_spends_wallet_tokens_without_changing_usage()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(temporary, used: 600_000_000);

        Assert.True(store.BuyItem(CompanionItemKind.RareCandy));

        Assert.Equal(1, store.ItemCount(CompanionItemKind.RareCandy));
        Assert.Equal(500_000_000, store.SpentTokens);
        Assert.Equal(100_000_000, store.AvailableTokens);
        Assert.Equal(600_000_000, store.UsedSinceInstall);
    }

    [Fact]
    public void Insufficient_wallet_is_a_no_op()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(temporary, used: CompanionItemRules.RareCandyPrice - 1);

        Assert.False(store.BuyItem(CompanionItemKind.RareCandy));

        Assert.Equal(0, store.ItemCount(CompanionItemKind.RareCandy));
        Assert.Equal(0, store.SpentTokens);
    }

    [Fact]
    public void Rare_candy_adds_companion_xp_without_forging_usage_tokens()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(
            temporary,
            used: 700_000_000,
            inventory: new Dictionary<string, int> { ["rareCandy"] = 1 },
            active: ActivePokemon(usedAtStage: 10_000_000));

        var result = store.UseRareCandy();

        Assert.Equal(RareCandyUseResult.Progressed, result);
        Assert.Equal(0, store.ItemCount(CompanionItemKind.RareCandy));
        Assert.Equal(110_000_000, store.ActiveUsedAtStage);
        Assert.Equal(700_000_000, store.UsedSinceInstall);
    }

    [Fact]
    public void Mint_changes_nature_and_consumes_one_item()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(
            temporary,
            used: 1,
            inventory: new Dictionary<string, int> { ["mint"] = 1 },
            active: ActivePokemon(nature: PokemonNature.Adamant),
            randomSource: new ConstantRandomSource(0));

        var changed = store.UseMint();

        Assert.NotNull(changed);
        Assert.NotEqual(PokemonNature.Adamant, changed);
        Assert.Equal(changed, store.CurrentPokemonNature);
        Assert.Equal(0, store.ItemCount(CompanionItemKind.Mint));
    }

    [Fact]
    public void Shiny_charm_is_a_single_permanent_purchase()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(temporary, used: 4_000_000_000);

        Assert.True(store.BuyItem(CompanionItemKind.ShinyCharm));
        Assert.True(store.OwnsShinyCharm);
        Assert.False(store.CanBuyItem(CompanionItemKind.ShinyCharm));
        Assert.False(store.BuyItem(CompanionItemKind.ShinyCharm));
        Assert.Equal(3_000_000_000, store.SpentTokens);
    }

    [Theory]
    [InlineData(48, true, true)]
    [InlineData(48, false, false)]
    [InlineData(64, false, true)]
    public void Shiny_roll_uses_the_owned_charm_denominator(long roll, bool charm, bool expected)
    {
        Assert.Equal(expected, CompanionItemRules.IsShinyRoll(roll, charm));
    }

    [Fact]
    public void Inventory_and_spending_survive_restart()
    {
        using var temporary = TemporaryDirectory.Create();
        var first = CreateStore(temporary, used: 600_000_000);
        Assert.True(first.BuyItem(CompanionItemKind.RareCandy));

        var restored = new CompanionStore(temporary.StatePath);

        Assert.Equal(1, restored.ItemCount(CompanionItemKind.RareCandy));
        Assert.Equal(500_000_000, restored.SpentTokens);
    }

    [Theory]
    [InlineData(FreshEggTier.Basic, 1_000_000_000)]
    [InlineData(FreshEggTier.Uncommon, 2_500_000_000)]
    [InlineData(FreshEggTier.Rare, 4_000_000_000)]
    public void Fresh_egg_prices_match_the_original_balance(FreshEggTier tier, long expected)
    {
        Assert.Equal(expected, CompanionItemRules.FreshEggPrice(tier));
    }

    [Fact]
    public void Buying_a_guaranteed_egg_keeps_the_released_active_in_the_dex()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(
            temporary,
            used: 3_000_000_000,
            active: ActivePokemon(usedAtStage: 90_000_000));

        Assert.True(store.BuyFreshEgg(FreshEggTier.Uncommon));

        Assert.False(store.HasActivePokemon);
        Assert.Equal(1, store.DexCount);
        var released = Assert.Single(store.CollectionEntries);
        Assert.True(released.IsReleased);
        Assert.Equal(new[] { 602 }, released.ChainOrder);
        Assert.Equal(602, released.FinalSpeciesId);
        Assert.Equal("저리어", released.FinalName);
        Assert.Equal(0, store.EggUsage);
        Assert.Equal(PokemonRarity.Uncommon, store.EggGuarantee);
        Assert.Equal(2_500_000_000, store.SpentTokens);
        Assert.Equal(3_000_000_000, store.UsedSinceInstall);

        var restored = new CompanionStore(temporary.StatePath);
        Assert.Equal(1, restored.DexCount);
        Assert.True(Assert.Single(restored.CollectionEntries).IsReleased);
    }

    [Fact]
    public void Buying_a_fresh_egg_records_only_the_forms_already_reached()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = CreateStore(
            temporary,
            used: 3_000_000_000,
            active: ActivePokemon(stageIndex: 1, pathIds: [602, 603]));

        Assert.True(store.BuyFreshEgg(FreshEggTier.Basic));

        var released = Assert.Single(store.CollectionEntries);
        Assert.True(released.IsReleased);
        Assert.Equal(new[] { 602, 603 }, released.ChainOrder);
        Assert.Equal(603, released.FinalSpeciesId);
        Assert.DoesNotContain(604, released.ChainOrder);
        Assert.Equal(new[] { 602, 603 }, store.DexSpecies.Select(item => item.SpeciesId));
    }

    [Fact]
    public void Fresh_egg_requires_an_active_companion_and_sufficient_wallet()
    {
        using var temporary = TemporaryDirectory.Create();
        var eggStore = CreateStore(temporary, used: 5_000_000_000);
        Assert.False(eggStore.BuyFreshEgg(FreshEggTier.Basic));

        using var second = TemporaryDirectory.Create();
        var poorStore = CreateStore(second, used: 999_999_999, active: ActivePokemon());
        Assert.False(poorStore.BuyFreshEgg(FreshEggTier.Basic));
        Assert.True(poorStore.HasActivePokemon);
    }

    [Theory]
    [InlineData(PokemonRarity.Common, PokemonRarity.Uncommon, false)]
    [InlineData(PokemonRarity.Uncommon, PokemonRarity.Uncommon, true)]
    [InlineData(PokemonRarity.Legendary, PokemonRarity.Rare, true)]
    public void Guarantee_rejects_only_lower_rarities(
        PokemonRarity rolled,
        PokemonRarity guarantee,
        bool expected)
    {
        Assert.Equal(expected, CompanionItemRules.MeetsGuarantee(rolled, guarantee));
    }

    [Fact]
    public void Egg_guarantee_and_spending_survive_restart()
    {
        using var temporary = TemporaryDirectory.Create();
        var first = CreateStore(
            temporary,
            used: 5_000_000_000,
            active: ActivePokemon());
        Assert.True(first.BuyFreshEgg(FreshEggTier.Rare));

        var restored = new CompanionStore(temporary.StatePath);

        Assert.Equal(PokemonRarity.Rare, restored.EggGuarantee);
        Assert.Equal(4_000_000_000, restored.SpentTokens);
        Assert.False(restored.HasActivePokemon);
    }

    private static CompanionStore CreateStore(
        TemporaryDirectory temporary,
        long used,
        Dictionary<string, int>? inventory = null,
        PokemonMonState? active = null,
        IRandomSource? randomSource = null)
    {
        var state = new CompanionState
        {
            InstallBaselineSet = true,
            UsedSinceInstall = used,
            LastDate = "2026-09-01",
            ClaimedTodayTokensByProvider = new Dictionary<string, long> { ["codex"] = 0 },
            ActivePokemon = active,
            Inventory = inventory ?? [],
        };
        File.WriteAllText(temporary.StatePath, JsonSerializer.Serialize(state));
        return new CompanionStore(temporary.StatePath, randomSource: randomSource);
    }

    private static PokemonMonState ActivePokemon(
        long usedAtStage = 0,
        PokemonNature nature = PokemonNature.Naive,
        int stageIndex = 0,
        IReadOnlyList<int>? pathIds = null)
    {
        var reachedPath = pathIds?.ToList() ?? [602];
        var names = new Dictionary<int, string>
        {
            [602] = "저리어",
            [603] = "저리릴",
            [604] = "저리더프",
        };

        return new()
        {
            BaseId = 602,
            PathIds = reachedPath,
            PlannedPathIds = [602, 603, 604],
            StageIndex = stageIndex,
            UsedAtStage = usedAtStage,
            Rarity = PokemonRarity.Common,
            TotalForms = 3,
            Nature = nature,
            Names = names,
        };
    }

    private sealed class ConstantRandomSource(long value) : IRandomSource
    {
        public long NextInt64(long maxExclusive) => Math.Abs(value % maxExclusive);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
            StatePath = System.IO.Path.Combine(path, "companion-state.json");
        }

        public string Path { get; }

        public string StatePath { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PokeTokenBar-item-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
