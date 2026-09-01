namespace PokeTokenBar.Core;

public sealed class CompanionState
{
    public bool InstallBaselineSet { get; set; }

    public long UsedSinceInstall { get; set; }

    public long SpentTokens { get; set; }

    public long EggUsage { get; set; }

    public int? PendingHatchId { get; set; }

    public Dictionary<string, long>? ClaimedTodayTokensByProvider { get; set; }

    public string LastDate { get; set; } = string.Empty;

    public PokemonMonState? ActivePokemon { get; set; }

    public List<PokemonDexEntry> Dex { get; set; } = [];

    public Dictionary<string, int> Inventory { get; set; } = [];
}
