namespace PokeTokenBar.Core;

public enum CompanionMilestoneKind
{
    Hatched,
    Evolved,
    Graduated,
}

public sealed record CompanionMilestone(
    CompanionMilestoneKind Kind,
    string? PokemonName,
    bool IsShiny,
    int DexCount);

public readonly record struct CompanionMilestoneSnapshot(
    int? SpeciesId,
    string? PokemonName,
    int Stage,
    bool IsShiny,
    int DexCount)
{
    public bool HasActivePokemon => SpeciesId is not null;
}

public sealed class CompanionMilestoneTracker
{
    private CompanionMilestoneSnapshot _previous;

    public CompanionMilestoneTracker(CompanionMilestoneSnapshot initial) =>
        _previous = initial;

    public CompanionMilestone? Observe(CompanionMilestoneSnapshot current)
    {
        var previous = _previous;
        _previous = current;

        if (!previous.HasActivePokemon && current.HasActivePokemon)
        {
            return new CompanionMilestone(
                CompanionMilestoneKind.Hatched,
                current.PokemonName,
                current.IsShiny,
                current.DexCount);
        }

        if (previous.HasActivePokemon
            && current.HasActivePokemon
            && (previous.SpeciesId != current.SpeciesId || current.Stage > previous.Stage))
        {
            return new CompanionMilestone(
                CompanionMilestoneKind.Evolved,
                current.PokemonName,
                current.IsShiny,
                current.DexCount);
        }

        if (previous.HasActivePokemon
            && !current.HasActivePokemon
            && current.DexCount > previous.DexCount)
        {
            return new CompanionMilestone(
                CompanionMilestoneKind.Graduated,
                previous.PokemonName,
                previous.IsShiny,
                current.DexCount);
        }

        return null;
    }
}
