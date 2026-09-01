namespace PokeTokenBar.Core;

public enum CompanionItemKind
{
    RareCandy,
    Mint,
    ShinyCharm,
}

public sealed record OwnedCompanionItem(CompanionItemKind Kind, int Count);

public enum RareCandyUseResult
{
    Unavailable,
    Progressed,
    Evolved,
    Graduated,
}

public static class CompanionItemRules
{
    public const long RareCandyPrice = 500_000_000;
    public const long RareCandyExperience = 100_000_000;
    public const long MintPrice = 100_000_000;
    public const long ShinyCharmPrice = 3_000_000_000;
    public const long StandardShinyDenominator = 64;
    public const long ShinyCharmDenominator = 48;

    public static long Price(CompanionItemKind kind) => kind switch
    {
        CompanionItemKind.RareCandy => RareCandyPrice,
        CompanionItemKind.Mint => MintPrice,
        CompanionItemKind.ShinyCharm => ShinyCharmPrice,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool IsPassive(CompanionItemKind kind) =>
        kind == CompanionItemKind.ShinyCharm;

    public static string StorageKey(CompanionItemKind kind) => kind switch
    {
        CompanionItemKind.RareCandy => "rareCandy",
        CompanionItemKind.Mint => "mint",
        CompanionItemKind.ShinyCharm => "shinyCharm",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool IsShinyRoll(long roll, bool ownsShinyCharm)
    {
        var denominator = ownsShinyCharm
            ? ShinyCharmDenominator
            : StandardShinyDenominator;
        return Math.Abs(roll % denominator) == 0;
    }
}
