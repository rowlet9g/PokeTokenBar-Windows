using PokeTokenBar.Core;

namespace PokeTokenBar.Core.Tests;

public sealed class CollectionQueryTests
{
    private static readonly PokemonDexSpecies[] Species = [new(1, "이상해씨", PokemonRarity.Common, false, false),
        new(25, "Pikachu", PokemonRarity.Rare, true, false), new(150, "뮤츠", PokemonRarity.Legendary, false, false)];

    [Theory]
    [InlineData("  #025 ", 25)]
    [InlineData("PIKA", 25)]
    [InlineData("이상", 1)]
    [InlineData("15", 150)]
    public void Names_and_padded_or_partial_numbers_search_owned_species(string query, int id) =>
        Assert.Equal(id, Assert.Single(CollectionQuery.Species(Species, query)).SpeciesId);

    [Fact]
    public void Filters_combine_and_empty_search_returns_no_records()
    {
        Assert.Equal(25, Assert.Single(CollectionQuery.Species(Species, "pika", PokemonRarity.Rare, true)).SpeciesId);
        Assert.Empty(CollectionQuery.Species(Species, "pika", PokemonRarity.Common, true));
        Assert.Empty(CollectionQuery.Species(Species, "없음"));
        Assert.Equal(3, CollectionQuery.Species(Species, " ").Count);
        Assert.Equal(new[] { 150, 25, 1 }, CollectionQuery.Species(Species, sort: DexSort.NumberDescending).Select(item => item.SpeciesId));
        Assert.Equal(new[] { 150, 25, 1 }, CollectionQuery.Species(Species, sort: DexSort.RarityDescending).Select(item => item.SpeciesId));
        var equalNames = Species.Select(item => item with { Name = "같음" });
        Assert.Equal(new[] { 1, 25, 150 }, CollectionQuery.Species(equalNames, sort: DexSort.NameDescending).Select(item => item.SpeciesId));
    }

    [Fact]
    public void Log_search_matches_reached_chain_names_and_numbers_but_not_future_name_metadata()
    {
        var entry = Entry("a", 26, 0, true) with { ChainOrder = new[] { 25, 26 },
            Names = new Dictionary<int, string> { [25] = "Pikachu", [26] = "라이츄", [27] = "미래" } };
        Assert.Single(CollectionQuery.Entries([entry], "pika"));
        Assert.Single(CollectionQuery.Entries([entry], "#025", PokemonRarity.Rare, true));
        Assert.Empty(CollectionQuery.Entries([entry], "미래"));
        Assert.Empty(CollectionQuery.Entries([entry], "#027"));
    }

    [Fact]
    public void Log_date_sorts_keep_active_at_the_expected_end_and_other_sorts_use_stable_ties()
    {
        var entries = new[] { Entry("b", 25, 1), Entry("a", 25, 1), Entry("old", 150, 0), Entry("active", 1, null, raising: true) };
        Assert.Equal(new[] { "active", "a", "b", "old" }, CollectionQuery.Entries(entries).Select(item => item.Id));
        Assert.Equal(new[] { "old", "a", "b", "active" }, CollectionQuery.Entries(entries, sort: CatchLogSort.OldestFirst).Select(item => item.Id));
        Assert.Equal(new[] { "active", "a", "b", "old" }, CollectionQuery.Entries(entries, sort: CatchLogSort.NumberAscending).Select(item => item.Id));
        Assert.Equal(new[] { "old", "a", "b", "active" }, CollectionQuery.Entries(entries, sort: CatchLogSort.NumberDescending).Select(item => item.Id));
    }

    [Fact]
    public void Pagination_bounds_pages_and_recovers_when_filtering_shrinks_the_result()
    {
        var items = Enumerable.Range(1, 33).ToArray();
        var first = CollectionQuery.Page(items, -1);
        Assert.Equal(16, first.Items.Count);
        Assert.Equal(0, first.Index);
        var last = CollectionQuery.Page(items, 99);
        Assert.Equal(new[] { 33 }, last.Items);
        Assert.Equal(2, last.Index);
        Assert.Equal(3, last.PageCount);
        Assert.Equal(0, CollectionQuery.Page(new[] { 25 }, last.Index).Index);
        var empty = CollectionQuery.Page(Array.Empty<int>(), 9);
        Assert.Equal(1, empty.PageCount);
        Assert.Equal(0, empty.Index);
        Assert.Empty(empty.Items);
    }

    private static PokemonCollectionEntry Entry(string id, int species, int? day, bool shiny = false, bool raising = false) =>
        new(id, species, $"Species {species}", new[] { species }, new Dictionary<int, string> { [species] = $"Species {species}" },
            PokemonRarity.Rare, day is null ? null : DateTimeOffset.UnixEpoch.AddDays(day.Value), shiny, PokemonNature.Hardy, raising);
}
