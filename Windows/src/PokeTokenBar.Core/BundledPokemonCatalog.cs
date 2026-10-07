using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PokeTokenBar.Core;

public sealed record CatalogSpecies(int Id, string Name, string KoreanName, string EnglishName,
    int CaptureRate, bool IsLegendary, bool IsMythical, int? EvolvesFromSpeciesId, int Generation);

/// <summary>A validated, release-pinned catalog. Cold starts never depend on online metadata.</summary>
public sealed class BundledPokemonCatalog : IPokemonProvider
{
    private static readonly Lazy<BundledPokemonCatalog> Instance = new(() => new());
    public static BundledPokemonCatalog Default => Instance.Value;
    private readonly Dictionary<int, CatalogSpecies> _species;
    private readonly Dictionary<int, CatalogSpecies[]> _children;
    private readonly Dictionary<string, string> _spriteHashes;
    private readonly IReadOnlyList<BasePokemonSpecies> _baseIndex;
    public IReadOnlyList<CatalogSpecies> Species { get; }
    public string SpriteSource { get; }

    private BundledPokemonCatalog()
    {
        using var stream = typeof(BundledPokemonCatalog).Assembly.GetManifestResourceStream("PokemonCatalog.json")
            ?? throw new InvalidDataException("Bundled Pokémon catalog is missing.");
        var catalog = JsonSerializer.Deserialize<CatalogDocument>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Bundled Pokémon catalog is empty.");
        if (catalog.SchemaVersion != 1 || catalog.MaximumSpeciesId != PokemonAssets.MaximumSpeciesId
            || catalog.Species.Length != PokemonAssets.MaximumSpeciesId
            || !catalog.Species.Select(s => s.Id).SequenceEqual(Enumerable.Range(1, PokemonAssets.MaximumSpeciesId)))
            throw new InvalidDataException("Bundled Pokémon catalog range is incomplete.");
        Species = Array.AsReadOnly(catalog.Species);
        SpriteSource = catalog.SpriteSource;
        _species = catalog.Species.ToDictionary(s => s.Id);
        _children = catalog.Species.Where(s => s.EvolvesFromSpeciesId is not null)
            .GroupBy(s => s.EvolvesFromSpeciesId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id).ToArray());
        _spriteHashes = catalog.SpriteHashes;
        foreach (var species in Species)
        {
            if (species.CaptureRate is < 0 or > 255 || species.Generation is < 1 or > 9
                || string.IsNullOrWhiteSpace(species.KoreanName) || string.IsNullOrWhiteSpace(species.EnglishName))
                throw new InvalidDataException($"Invalid Pokémon metadata for #{species.Id}.");
            var visited = new HashSet<int> { species.Id };
            var parent = species.EvolvesFromSpeciesId;
            while (parent is { } id)
            {
                if (!visited.Add(id) || !_species.TryGetValue(id, out var ancestor))
                    throw new InvalidDataException("Invalid bundled evolution ancestry.");
                parent = ancestor.EvolvesFromSpeciesId;
            }
            foreach (var shiny in new[] { false, true })
                if (!_spriteHashes.TryGetValue(SpriteName(species.Id, shiny), out var hash) || hash.Length != 64)
                    throw new InvalidDataException("Incomplete bundled sprite manifest.");
        }
        _baseIndex = Array.AsReadOnly(Species.Where(s => s.EvolvesFromSpeciesId is null && s.Id != PokemonAssets.DittoSpeciesId)
            .Select(s => new BasePokemonSpecies(s.Id, s.CaptureRate, s.IsLegendary, s.IsMythical)).ToArray());
    }

    public Task<IReadOnlyList<BasePokemonSpecies>> GetBaseSpeciesIndexAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_baseIndex);
    }

    public Task<BasePokemonSpecies?> GetBaseSpeciesAsync(int speciesId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_species.TryGetValue(speciesId, out var s) && s.EvolvesFromSpeciesId is null
            && speciesId != PokemonAssets.DittoSpeciesId
            ? new BasePokemonSpecies(s.Id, s.CaptureRate, s.IsLegendary, s.IsMythical) : null);
    }

    public Task<PokemonEvolutionLine> GetEvolutionLineAsync(int baseSpeciesId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_species.TryGetValue(baseSpeciesId, out var species) || species.EvolvesFromSpeciesId is not null)
            throw new ArgumentOutOfRangeException(nameof(baseSpeciesId));
        var tree = BuildTree(baseSpeciesId);
        return Task.FromResult(new PokemonEvolutionLine(baseSpeciesId, tree,
            PokemonBalance.RarityFrom(species.CaptureRate, species.IsLegendary, species.IsMythical),
            tree.SpeciesIds().ToDictionary(id => id, id => _species[id].KoreanName)));
    }

    private PokemonEvolutionNode BuildTree(int id) => new(id,
        _children.TryGetValue(id, out var children) ? children.Select(s => BuildTree(s.Id)).ToArray() : []);

    public byte[]? ReadSprite(int speciesId, bool shiny)
    {
        if (!PokemonAssets.HasSprite(speciesId)) return null;
        var name = SpriteName(speciesId, shiny);
        using var stream = typeof(BundledPokemonCatalog).Assembly.GetManifestResourceStream("PokemonSprites.zip")
            ?? throw new InvalidDataException("Bundled Pokémon sprites are missing.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Bundled sprite {name} is missing.");
        if (entry.Length is <= 0 or > 1_048_576) throw new InvalidDataException("Invalid bundled sprite size.");
        using var input = entry.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        var bytes = output.ToArray();
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(_spriteHashes[name], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Bundled sprite checksum failed: {name}.");
        return bytes;
    }

    private static string SpriteName(int id, bool shiny) => shiny ? $"shiny-{id}.png" : $"{id}.png";
    private sealed record CatalogDocument(int SchemaVersion, int MaximumSpeciesId, string SpriteSource,
        CatalogSpecies[] Species, Dictionary<string, string> SpriteHashes);
}
