using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokeTokenBar.Core;

public sealed class PokeApiClient : IPokemonProvider
{
    private static readonly Uri RestBase = new("https://pokeapi.co/api/v2/");
    private static readonly Uri GraphQlEndpoint = new("https://graphql.pokeapi.co/v1beta2");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _baseIndexGate = new(1, 1);
    private readonly Dictionary<int, SpeciesDto> _speciesMemory = [];
    private readonly Dictionary<int, PokemonEvolutionLine> _lineMemory = [];
    private IReadOnlyList<BasePokemonSpecies>? _baseIndexMemory;

    public PokeApiClient(HttpClient httpClient, string cacheDirectory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<IReadOnlyList<BasePokemonSpecies>> GetBaseSpeciesIndexAsync(
        CancellationToken cancellationToken = default)
    {
        if (_baseIndexMemory is { Count: > 0 } memory)
        {
            return memory;
        }

        await _baseIndexGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_baseIndexMemory is { Count: > 0 } cachedMemory)
            {
                return cachedMemory;
            }

            var snapshot = await ReadJsonFileAsync<BaseIndexSnapshot>(
                Path.Combine(_cacheDirectory, "base-index.json"),
                cancellationToken).ConfigureAwait(false);
            if (snapshot is { Entries.Count: > 0 }
                && DateTimeOffset.UtcNow - snapshot.FetchedAt < TimeSpan.FromDays(30))
            {
                return _baseIndexMemory = snapshot.Entries;
            }

            try
            {
                var fetched = await FetchBaseIndexAsync(cancellationToken).ConfigureAwait(false);
                await WriteJsonFileAsync(
                    Path.Combine(_cacheDirectory, "base-index.json"),
                    new BaseIndexSnapshot(DateTimeOffset.UtcNow, fetched),
                    cancellationToken).ConfigureAwait(false);
                return _baseIndexMemory = fetched;
            }
            catch when (snapshot is { Entries.Count: > 0 })
            {
                return _baseIndexMemory = snapshot.Entries;
            }
        }
        finally
        {
            _baseIndexGate.Release();
        }
    }

    public async Task<BasePokemonSpecies?> GetBaseSpeciesAsync(
        int speciesId,
        CancellationToken cancellationToken = default)
    {
        if (!PokemonAssets.HasSprite(speciesId) || speciesId == PokemonAssets.DittoSpeciesId)
        {
            return null;
        }

        var species = await GetSpeciesAsync(speciesId, cancellationToken).ConfigureAwait(false);
        return species.EvolvesFromSpecies is null
            ? new BasePokemonSpecies(speciesId, species.CaptureRate)
            : null;
    }

    public async Task<PokemonEvolutionLine> GetEvolutionLineAsync(
        int baseSpeciesId,
        CancellationToken cancellationToken = default)
    {
        if (!PokemonAssets.HasSprite(baseSpeciesId))
        {
            throw new ArgumentOutOfRangeException(nameof(baseSpeciesId));
        }

        lock (_lineMemory)
        {
            if (_lineMemory.TryGetValue(baseSpeciesId, out var cached))
            {
                return cached;
            }
        }

        var baseSpecies = await GetSpeciesAsync(baseSpeciesId, cancellationToken).ConfigureAwait(false);
        var chainUri = ValidateEvolutionChainUri(baseSpecies.EvolutionChain.Url)
            ?? throw new InvalidDataException("PokéAPI returned an unsafe evolution-chain URL.");
        var chainId = SpeciesIdFromUri(chainUri);
        var chain = await GetCachedJsonAsync<ChainDto>(
            chainUri,
            $"evolution-chain-{chainId}.json",
            cancellationToken).ConfigureAwait(false);
        var rawTree = NodeFrom(chain.Chain);
        var tree = rawTree.KeepingSupportedSpecies()
            ?? new PokemonEvolutionNode(baseSpeciesId, []);

        var ids = tree.SpeciesIds().Distinct().ToArray();
        var speciesTasks = ids.Select(async id => new
        {
            Id = id,
            Species = await GetSpeciesAsync(id, cancellationToken).ConfigureAwait(false),
        });
        var species = await Task.WhenAll(speciesTasks).ConfigureAwait(false);
        var names = species.ToDictionary(
            item => item.Id,
            item => PreferredName(item.Species));

        var line = new PokemonEvolutionLine(
            baseSpeciesId,
            tree,
            PokemonBalance.RarityFrom(
                baseSpecies.CaptureRate,
                baseSpecies.IsLegendary,
                baseSpecies.IsMythical),
            names);
        lock (_lineMemory)
        {
            _lineMemory[baseSpeciesId] = line;
        }

        return line;
    }

    public static Uri? ValidateEvolutionChainUri(string? raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "pokeapi.co", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith("/api/v2/evolution-chain/", StringComparison.Ordinal)
            || SpeciesIdFromUri(uri) <= 0)
        {
            return null;
        }

        return uri;
    }

    private async Task<List<BasePokemonSpecies>> FetchBaseIndexAsync(
        CancellationToken cancellationToken)
    {
        const string query = "{ pokemonspecies(where: {evolves_from_species_id: {_is_null: true}, id: {_lte: 649, _neq: 132}}, order_by: {id: asc}) { id capture_rate } }";
        using var response = await _httpClient.PostAsJsonAsync(
            GraphQlEndpoint,
            new { query },
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var decoded = await response.Content.ReadFromJsonAsync<GraphQlResponse>(
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("PokéAPI GraphQL returned an empty response.");
        var entries = decoded.Data.PokemonSpecies
            .Where(row => PokemonAssets.HasSprite(row.Id) && row.Id != PokemonAssets.DittoSpeciesId)
            .Select(row => new BasePokemonSpecies(row.Id, Math.Clamp(row.CaptureRate, 1, 255)))
            .ToList();
        if (entries.Count == 0)
        {
            throw new InvalidDataException("PokéAPI GraphQL returned no base species.");
        }

        return entries;
    }

    private async Task<SpeciesDto> GetSpeciesAsync(
        int speciesId,
        CancellationToken cancellationToken)
    {
        lock (_speciesMemory)
        {
            if (_speciesMemory.TryGetValue(speciesId, out var cached))
            {
                return cached;
            }
        }

        var result = await GetCachedJsonAsync<SpeciesDto>(
            new Uri(RestBase, $"pokemon-species/{speciesId}/"),
            $"pokemon-species-{speciesId}.json",
            cancellationToken).ConfigureAwait(false);
        lock (_speciesMemory)
        {
            _speciesMemory[speciesId] = result;
        }

        return result;
    }

    private async Task<T> GetCachedJsonAsync<T>(
        Uri uri,
        string fileName,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_cacheDirectory, fileName);
        var cached = await ReadJsonFileAsync<T>(path, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var decoded = await JsonSerializer.DeserializeAsync<T>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"PokéAPI returned an empty response for {uri}.");
        await WriteJsonFileAsync(path, decoded, cancellationToken).ConfigureAwait(false);
        return decoded;
    }

    private static async Task<T?> ReadJsonFileAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<T>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return default;
        }
        catch (IOException)
        {
            return default;
        }
    }

    private static async Task WriteJsonFileAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // A stale temporary cache is harmless and may be cleaned later.
            }
            catch (UnauthorizedAccessException)
            {
                // A stale temporary cache is harmless and may be cleaned later.
            }
        }
    }

    private static PokemonEvolutionNode NodeFrom(ChainLinkDto link)
    {
        var id = SpeciesIdFromUri(link.Species.Url);
        if (id <= 0)
        {
            throw new InvalidDataException("PokéAPI returned an invalid species URL.");
        }

        return new PokemonEvolutionNode(
            id,
            link.EvolvesTo.Select(NodeFrom).ToArray());
    }

    private static int SpeciesIdFromUri(Uri uri)
    {
        var part = uri.Segments.LastOrDefault()?.Trim('/');
        return int.TryParse(part, out var id) ? id : 0;
    }

    private static int SpeciesIdFromUri(string? raw) =>
        Uri.TryCreate(raw, UriKind.Absolute, out var uri) ? SpeciesIdFromUri(uri) : 0;

    private static string PreferredName(SpeciesDto species)
    {
        var korean = species.Names.FirstOrDefault(name => name.Language.Name == "ko")?.Name;
        var english = species.Names.FirstOrDefault(name => name.Language.Name == "en")?.Name;
        return korean
            ?? english
            ?? species.Name.Replace('-', ' ');
    }

    private sealed record BaseIndexSnapshot(DateTimeOffset FetchedAt, List<BasePokemonSpecies> Entries);

    private sealed class GraphQlResponse
    {
        [JsonPropertyName("data")]
        public GraphQlData Data { get; set; } = new();
    }

    private sealed class GraphQlData
    {
        [JsonPropertyName("pokemonspecies")]
        public List<GraphQlSpeciesRow> PokemonSpecies { get; set; } = [];
    }

    private sealed class GraphQlSpeciesRow
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("capture_rate")]
        public int CaptureRate { get; set; }
    }

    private sealed class SpeciesDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("capture_rate")]
        public int CaptureRate { get; set; }

        [JsonPropertyName("is_legendary")]
        public bool IsLegendary { get; set; }

        [JsonPropertyName("is_mythical")]
        public bool IsMythical { get; set; }

        [JsonPropertyName("names")]
        public List<NameDto> Names { get; set; } = [];

        [JsonPropertyName("evolution_chain")]
        public UrlRefDto EvolutionChain { get; set; } = new();

        [JsonPropertyName("evolves_from_species")]
        public NamedRefDto? EvolvesFromSpecies { get; set; }
    }

    private sealed class NameDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        public NamedRefDto Language { get; set; } = new();
    }

    private sealed class NamedRefDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    private sealed class UrlRefDto
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    private sealed class ChainDto
    {
        [JsonPropertyName("chain")]
        public ChainLinkDto Chain { get; set; } = new();
    }

    private sealed class ChainLinkDto
    {
        [JsonPropertyName("species")]
        public NamedRefDto Species { get; set; } = new();

        [JsonPropertyName("evolves_to")]
        public List<ChainLinkDto> EvolvesTo { get; set; } = [];
    }
}
