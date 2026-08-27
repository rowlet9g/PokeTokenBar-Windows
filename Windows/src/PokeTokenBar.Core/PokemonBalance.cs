namespace PokeTokenBar.Core;

public enum PokemonRarity
{
    Common,
    Uncommon,
    Rare,
    Legendary,
}

public static class PokemonBalance
{
    public const long EggHatchThreshold = 5_000_000;
    public const long MaxTokenValue = 1_000_000_000_000_000;

    public static long GraduationTotal(PokemonRarity rarity) => rarity switch
    {
        PokemonRarity.Common => 750_000_000,
        PokemonRarity.Uncommon => 1_875_000_000,
        PokemonRarity.Rare => 3_000_000_000,
        PokemonRarity.Legendary => 6_000_000_000,
        _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
    };

    public static PokemonRarity RarityFrom(
        int captureRate,
        bool isLegendary,
        bool isMythical)
    {
        if (isLegendary || isMythical)
        {
            return PokemonRarity.Legendary;
        }

        if (captureRate <= 45)
        {
            return PokemonRarity.Rare;
        }

        return captureRate <= 120
            ? PokemonRarity.Uncommon
            : PokemonRarity.Common;
    }

    public static long PhaseThreshold(
        PokemonRarity rarity,
        int totalForms,
        int stageIndex)
    {
        var forms = Math.Max(1, totalForms);
        var oneBasedStage = Math.Max(0, stageIndex) + 1L;
        var denominator = forms * (forms + 1L) / 2d;
        var threshold = (double)GraduationTotal(rarity) * oneBasedStage / denominator;
        return (long)Math.Round(
            Math.Min(MaxTokenValue, threshold),
            MidpointRounding.AwayFromZero);
    }
}
