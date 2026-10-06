namespace PokeTokenBar.Core;

public sealed class CompanionState
{
    public bool InstallBaselineSet { get; set; }

    public long UsedSinceInstall { get; set; }

    public long SpentTokens { get; set; }

    public long EggUsage { get; set; }

    // Unit basis for banked growth, not a preference. Settings remain authoritative
    // across imports and interrupted writes to the two separate files.
    public double GrowthDifficultyBasis { get; set; } = PokemonBalance.DefaultDifficulty;

    public int? PendingHatchId { get; set; }

    public PokemonRarity? EggGuarantee { get; set; }

    public Dictionary<string, long>? ClaimedTodayTokensByProvider { get; set; }

    public string LastDate { get; set; } = string.Empty;

    public PokemonMonState? ActivePokemon { get; set; }

    // null follows the active companion/egg; a pin only changes display surfaces.
    public int? RepresentativeSpeciesId { get; set; }

    public List<PokemonDexEntry> Dex { get; set; } = [];

    public Dictionary<string, int> Inventory { get; set; } = [];

    public Dictionary<string, LimitWindowProgress> LimitProgress { get; set; } = [];
}
