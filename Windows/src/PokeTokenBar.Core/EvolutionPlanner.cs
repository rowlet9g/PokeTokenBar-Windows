namespace PokeTokenBar.Core;

public static class EvolutionPlanner
{
    public static List<int> Build(PokemonEvolutionNode root, IReadOnlySet<int> completedFinals, IRandomSource random)
    {
        static IEnumerable<int> Finals(PokemonEvolutionNode node) => node.Children.Count == 0
            ? [node.SpeciesId] : node.Children.SelectMany(Finals);
        var plan = new List<int> { root.SpeciesId };
        var node = root;
        while (node.Children.Count > 0)
        {
            var fresh = node.Children.Where(child => Finals(child).Any(id => !completedFinals.Contains(id))).ToArray();
            var pool = fresh.Length > 0 ? fresh : node.Children.ToArray();
            node = pool[(int)random.NextInt64(pool.Length)];
            plan.Add(node.SpeciesId);
        }
        return plan;
    }

    public static PokemonEvolutionNode? Follow(PokemonEvolutionNode root, IReadOnlyList<int> path)
    {
        if (path.Count == 0 || path[0] != root.SpeciesId) return null;
        var node = root;
        foreach (var id in path.Skip(1))
        {
            var next = node.Children.FirstOrDefault(child => child.SpeciesId == id);
            if (next is null) return null;
            node = next;
        }
        return node;
    }
}
