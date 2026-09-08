namespace PokeTokenBar.Core;

public static class EvolutionPreview
{
    public static IReadOnlyList<PokemonLineStage> Build(
        IReadOnlyList<PokemonLineStage> realized, PokemonEvolutionLine line)
    {
        var result = realized.Where(s => s.Status != PokemonLineStageStatus.HiddenFuture).ToList();
        var current = result.LastOrDefault();
        if (current?.SpeciesId is not { } id) return result;
        PokemonEvolutionNode? Find(PokemonEvolutionNode node) => node.SpeciesId == id
            ? node : node.Children.Select(Find).FirstOrDefault(n => n is not null);
        var node = Find(line.Tree);
        while (node?.Children.Count == 1)
        {
            node = node.Children[0];
            result.Add(new(node.SpeciesId, line.NameFor(node.SpeciesId),
                PokemonLineStageStatus.HiddenFuture, current.IsShiny));
        }
        if (node?.Children.Count > 1)
            result.Add(new(null, "?", PokemonLineStageStatus.HiddenFuture, current.IsShiny));
        return result;
    }
}
