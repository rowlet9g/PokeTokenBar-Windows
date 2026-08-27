namespace PokeTokenBar.Core.Tests;

public sealed class PokemonBalanceTests
{
    [Theory]
    [InlineData(PokemonRarity.Common)]
    [InlineData(PokemonRarity.Uncommon)]
    [InlineData(PokemonRarity.Rare)]
    [InlineData(PokemonRarity.Legendary)]
    public void Phase_thresholds_sum_to_the_rarity_graduation_total(PokemonRarity rarity)
    {
        for (var forms = 1; forms <= 4; forms++)
        {
            var sum = Enumerable.Range(0, forms)
                .Sum(stage => PokemonBalance.PhaseThreshold(rarity, forms, stage));

            Assert.Equal(PokemonBalance.GraduationTotal(rarity), sum);
        }
    }

    [Fact]
    public void Later_evolution_stages_cost_more()
    {
        var thresholds = Enumerable.Range(0, 3)
            .Select(stage => PokemonBalance.PhaseThreshold(PokemonRarity.Common, 3, stage))
            .ToArray();

        Assert.True(thresholds[0] < thresholds[1]);
        Assert.True(thresholds[1] < thresholds[2]);
    }

    [Fact]
    public void Absurd_stage_values_saturate_instead_of_overflowing()
    {
        var threshold = PokemonBalance.PhaseThreshold(
            PokemonRarity.Legendary,
            totalForms: 1,
            stageIndex: int.MaxValue);

        Assert.Equal(PokemonBalance.MaxTokenValue, threshold);
    }
}
