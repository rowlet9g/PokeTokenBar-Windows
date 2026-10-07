using System.Text.Json.Serialization;

namespace PokeTokenBar.Core;

public sealed record BasePokemonSpecies(int Id, int CaptureRate, bool IsLegendary = false, bool IsMythical = false);

public sealed record PokemonEvolutionNode(
    int SpeciesId,
    IReadOnlyList<PokemonEvolutionNode> Children)
{
    public int Depth => 1 + (Children.Count == 0 ? 0 : Children.Max(child => child.Depth));

    public IEnumerable<int> SpeciesIds()
    {
        yield return SpeciesId;
        foreach (var child in Children)
        {
            foreach (var id in child.SpeciesIds())
            {
                yield return id;
            }
        }
    }

    public PokemonEvolutionNode? KeepingSupportedSpecies()
        => KeepingSpeciesThrough(PokemonAssets.MaximumSpeciesId);

    public PokemonEvolutionNode? KeepingSpeciesThrough(int maximumSpeciesId)
    {
        if (!PokemonAssets.HasSprite(SpeciesId) || SpeciesId > maximumSpeciesId)
        {
            return null;
        }

        return new PokemonEvolutionNode(
            SpeciesId,
            Children
                .Select(child => child.KeepingSpeciesThrough(maximumSpeciesId))
                .Where(child => child is not null)
                .Cast<PokemonEvolutionNode>()
                .ToArray());
    }
}

public sealed record PokemonEvolutionLine(
    int BaseId,
    PokemonEvolutionNode Tree,
    PokemonRarity Rarity,
    IReadOnlyDictionary<int, string> Names)
{
    public string NameFor(int speciesId) =>
        Names.TryGetValue(speciesId, out var name) ? name : $"#{speciesId}";
}

public interface IPokemonProvider
{
    Task<IReadOnlyList<BasePokemonSpecies>> GetBaseSpeciesIndexAsync(
        CancellationToken cancellationToken = default);

    Task<BasePokemonSpecies?> GetBaseSpeciesAsync(
        int speciesId,
        CancellationToken cancellationToken = default);

    Task<PokemonEvolutionLine> GetEvolutionLineAsync(
        int baseSpeciesId,
        CancellationToken cancellationToken = default);
}

public interface IRandomSource
{
    long NextInt64(long maxExclusive);
}

public sealed class SystemRandomSource : IRandomSource
{
    public long NextInt64(long maxExclusive) => Random.Shared.NextInt64(maxExclusive);
}

public static class PokemonAssets
{
    public const int MinimumSpeciesId = 1;
    public const int MaximumSpeciesId = 1025;
    public const int DittoSpeciesId = 132;

    public static bool HasSprite(int speciesId) =>
        speciesId is >= MinimumSpeciesId and <= MaximumSpeciesId;
}

public enum PokemonNature
{
    Hardy,
    Lonely,
    Brave,
    Adamant,
    Naughty,
    Bold,
    Docile,
    Relaxed,
    Impish,
    Lax,
    Timid,
    Hasty,
    Serious,
    Jolly,
    Naive,
    Modest,
    Mild,
    Quiet,
    Bashful,
    Rash,
    Calm,
    Gentle,
    Sassy,
    Careful,
    Quirky,
}

public sealed class PokemonMonState
{
    // Preserve pre-1.0 evolution plans even when later generations add a descendant.
    public int EvolutionCatalogMaximumSpeciesId { get; set; } = 649;

    // Fixed at hatch. Missing fields in older saves retain their original costs.
    public bool HasGrowthBoost { get; set; }

    public int? DittoDisguise { get; set; }

    public bool DittoRevealed { get; set; }

    [JsonIgnore]
    public bool IsDittoDisguised => DittoDisguise is not null && !DittoRevealed;

    [JsonIgnore]
    public bool VisibleShiny => IsShiny && !IsDittoDisguised;

    public int BaseId { get; set; }

    public List<int> PathIds { get; set; } = [];

    public List<int> PlannedPathIds { get; set; } = [];

    public int StageIndex { get; set; }

    public long UsedAtStage { get; set; }

    public PokemonRarity Rarity { get; set; }

    public int TotalForms { get; set; } = 1;

    public bool IsShiny { get; set; }

    public PokemonNature Nature { get; set; }

    public Dictionary<int, string> Names { get; set; } = [];

    [JsonIgnore]
    public int CurrentId => PathIds.Count == 0
        ? BaseId
        : PathIds[Math.Clamp(StageIndex, 0, PathIds.Count - 1)];

    [JsonIgnore]
    public string CurrentName => Names.TryGetValue(CurrentId, out var name)
        ? name
        : $"#{CurrentId}";
}

public sealed class PokemonDexEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public int BaseId { get; set; }

    public int FinalId { get; set; }

    public List<int> ChainOrder { get; set; } = [];

    public PokemonRarity Rarity { get; set; }

    public DateTimeOffset CaughtAt { get; set; }

    // A released entry came from abandoning the active companion for a fresh egg.
    // Keep it in the dex so buying an egg never erases discovered species.
    public DateTimeOffset? ReleasedAt { get; set; }

    public bool IsShiny { get; set; }

    public PokemonNature Nature { get; set; }

    public Dictionary<int, string> Names { get; set; } = [];

    [JsonIgnore]
    public bool IsReleased => ReleasedAt is not null;
}

public enum PokemonLineStageStatus
{
    Realized,
    Current,
    HiddenFuture,
}

public sealed record PokemonLineStage(
    int? SpeciesId,
    string Name,
    PokemonLineStageStatus Status,
    bool IsShiny);

public sealed record PokemonCollectionEntry(
    string Id,
    int FinalSpeciesId,
    string FinalName,
    IReadOnlyList<int> ChainOrder,
    IReadOnlyDictionary<int, string> Names,
    PokemonRarity Rarity,
    DateTimeOffset? CaughtAt,
    bool IsShiny,
    PokemonNature Nature,
    bool IsRaising,
    bool IsReleased = false);

public sealed record PokemonDexSpecies(
    int SpeciesId,
    string Name,
    PokemonRarity Rarity,
    bool IsShiny,
    bool IsRaising,
    bool HasNormal = true);

public sealed record RepresentativePokemon(int? SpeciesId, string Name, bool IsShiny, bool IsPinned);
