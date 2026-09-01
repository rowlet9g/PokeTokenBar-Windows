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
        PokemonNature nature = PokemonNature.Naive) => new()
    {
        BaseId = 602,
        PathIds = [602],
        PlannedPathIds = [602, 603, 604],
        StageIndex = 0,
        UsedAtStage = usedAtStage,
        Rarity = PokemonRarity.Common,
        TotalForms = 3,
        Nature = nature,
        Names = new Dictionary<int, string>
        {
            [602] = "저리어",
            [603] = "저리릴",
            [604] = "저리더프",
        },
    };

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
