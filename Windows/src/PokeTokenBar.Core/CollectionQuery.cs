using System.Globalization;

namespace PokeTokenBar.Core;

public enum DexSort { NumberAscending, NumberDescending, NameAscending, NameDescending, RarityDescending }
public enum CatchLogSort { RecentFirst, OldestFirst, NumberAscending, NumberDescending, NameAscending, NameDescending, RarityDescending }
public sealed record CollectionPage<T>(IReadOnlyList<T> Items, int Index, int PageCount, int TotalCount);

public static class CollectionQuery
{
    public const int DexPageSize = 16;

    public static IReadOnlyList<PokemonDexSpecies> Species(IEnumerable<PokemonDexSpecies> source,
        string query = "", PokemonRarity? rarity = null, bool shinyOnly = false, DexSort sort = DexSort.NumberAscending)
    {
        var filtered = source.Where(item => (rarity is null || item.Rarity == rarity)
            && (!shinyOnly || item.IsShiny) && Matches(query, item.SpeciesId, item.Name));
        IOrderedEnumerable<PokemonDexSpecies> ordered = sort switch
        {
            DexSort.NumberDescending => filtered.OrderByDescending(item => item.SpeciesId),
            DexSort.NameAscending => filtered.OrderBy(item => item.Name, StringComparer.CurrentCulture),
            DexSort.NameDescending => filtered.OrderByDescending(item => item.Name, StringComparer.CurrentCulture),
            DexSort.RarityDescending => filtered.OrderByDescending(item => item.Rarity),
            _ => filtered.OrderBy(item => item.SpeciesId),
        };
        return ordered.ThenBy(item => item.SpeciesId).ToArray();
    }

    public static IReadOnlyList<PokemonCollectionEntry> Entries(IEnumerable<PokemonCollectionEntry> source,
        string query = "", PokemonRarity? rarity = null, bool shinyOnly = false, CatchLogSort sort = CatchLogSort.RecentFirst)
    {
        var filtered = source.Where(item => (rarity is null || item.Rarity == rarity)
            && (!shinyOnly || item.IsShiny)
            && item.ChainOrder.Any(id => Matches(query, id, item.Names.GetValueOrDefault(id, $"#{id}"))));
        IOrderedEnumerable<PokemonCollectionEntry> ordered = sort switch
        {
            CatchLogSort.OldestFirst => filtered.OrderBy(item => item.IsRaising).ThenBy(item => item.CaughtAt),
            CatchLogSort.NumberAscending => filtered.OrderBy(item => item.FinalSpeciesId).ThenByDescending(item => item.CaughtAt),
            CatchLogSort.NumberDescending => filtered.OrderByDescending(item => item.FinalSpeciesId).ThenByDescending(item => item.CaughtAt),
            CatchLogSort.NameAscending => filtered.OrderBy(item => item.FinalName, StringComparer.CurrentCulture).ThenByDescending(item => item.CaughtAt),
            CatchLogSort.NameDescending => filtered.OrderByDescending(item => item.FinalName, StringComparer.CurrentCulture).ThenByDescending(item => item.CaughtAt),
            CatchLogSort.RarityDescending => filtered.OrderByDescending(item => item.Rarity).ThenByDescending(item => item.CaughtAt),
            _ => filtered.OrderByDescending(item => item.IsRaising).ThenByDescending(item => item.CaughtAt),
        };
        return ordered.ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    public static CollectionPage<T> Page<T>(IReadOnlyList<T> items, int index)
    {
        var pages = Math.Max(1, (items.Count + DexPageSize - 1) / DexPageSize);
        var current = Math.Clamp(index, 0, pages - 1);
        return new(items.Skip(current * DexPageSize).Take(DexPageSize).ToArray(), current, pages, items.Count);
    }

    private static bool Matches(string query, int id, string name)
    {
        var text = query.Trim();
        if (text.Length == 0) return true;
        var number = text.TrimStart('#');
        if (int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric))
            return id.ToString(CultureInfo.InvariantCulture).Contains(numeric.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return CultureInfo.CurrentCulture.CompareInfo.IndexOf(name, text, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }
}
