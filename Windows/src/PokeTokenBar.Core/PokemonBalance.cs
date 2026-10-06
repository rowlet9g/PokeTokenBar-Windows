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
    public const int RepeatGrowthMultiplier = 2;
    public const long MaxTokenValue = 1_000_000_000_000_000;
    public const double DefaultDifficulty = 1;

    public static double ClampDifficulty(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.1, 2) : DefaultDifficulty;

    public static long Scaled(long value, double difficulty) => Math.Max(1,
        (long)Math.Round(Math.Min(MaxTokenValue, value * ClampDifficulty(difficulty)), MidpointRounding.AwayFromZero));

    // Match the upstream logarithmic slider: equal ratios occupy equal distances.
    public static double DifficultyPosition(double value) => Math.Log(ClampDifficulty(value) / 0.1) / Math.Log(20);

    public static double DifficultyAtPosition(double position)
    {
        var p = double.IsFinite(position) ? Math.Clamp(position, 0, 1) : DifficultyPosition(DefaultDifficulty);
        if (Math.Abs(p - DifficultyPosition(DefaultDifficulty)) < 0.01) return DefaultDifficulty;
        var value = 0.1 * Math.Pow(20, p);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)) - 1);
        return ClampDifficulty(Math.Round(value / magnitude, MidpointRounding.AwayFromZero) * magnitude);
    }

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
        int stageIndex,
        int growthMultiplier = 1)
    {
        var forms = Math.Max(1, totalForms);
        var oneBasedStage = Math.Max(0, stageIndex) + 1L;
        var denominator = forms * (forms + 1L) / 2d;
        var threshold = (double)GraduationTotal(rarity) * oneBasedStage / denominator;
        var standardThreshold = (long)Math.Round(
            Math.Min(MaxTokenValue, threshold),
            MidpointRounding.AwayFromZero);
        return Math.Max(1, (long)Math.Round(
            standardThreshold / (double)Math.Max(1, growthMultiplier),
            MidpointRounding.AwayFromZero));
    }
}
