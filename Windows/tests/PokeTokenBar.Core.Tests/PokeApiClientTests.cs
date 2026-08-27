using System.Net;
using System.Text;
using System.Text.Json;

namespace PokeTokenBar.Core.Tests;

public sealed class PokeApiClientTests
{
    [Fact]
    public async Task Evolution_line_uses_korean_names_and_parses_the_tree()
    {
        using var temporary = TemporaryDirectory.Create();
        using var http = new HttpClient(new StubHttpHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            "https://pokeapi.co/api/v2/pokemon-species/1/" => Json(SpeciesJson(
                1,
                "bulbasaur",
                255,
                "https://pokeapi.co/api/v2/evolution-chain/1/",
                "이상해씨")),
            "https://pokeapi.co/api/v2/evolution-chain/1/" => Json(
                """
                {"chain":{"species":{"name":"bulbasaur","url":"https://pokeapi.co/api/v2/pokemon-species/1/"},"evolves_to":[{"species":{"name":"ivysaur","url":"https://pokeapi.co/api/v2/pokemon-species/2/"},"evolves_to":[]}]}}
                """),
            "https://pokeapi.co/api/v2/pokemon-species/2/" => Json(SpeciesJson(
                2,
                "ivysaur",
                45,
                "https://pokeapi.co/api/v2/evolution-chain/1/",
                "이상해풀",
                evolvesFrom: "bulbasaur")),
            _ => throw new InvalidOperationException(request.RequestUri!.AbsoluteUri),
        }));
        var client = new PokeApiClient(http, temporary.Path);

        var line = await client.GetEvolutionLineAsync(1);

        Assert.Equal(PokemonRarity.Common, line.Rarity);
        Assert.Equal(2, line.Tree.Depth);
        Assert.Equal("이상해씨", line.NameFor(1));
        Assert.Equal("이상해풀", line.NameFor(2));
    }

    [Fact]
    public async Task Unsafe_evolution_chain_host_is_rejected_before_a_second_request()
    {
        using var temporary = TemporaryDirectory.Create();
        var requests = 0;
        using var http = new HttpClient(new StubHttpHandler(_ =>
        {
            requests++;
            return Json(SpeciesJson(
                1,
                "bulbasaur",
                255,
                "https://example.com/api/v2/evolution-chain/1/",
                "이상해씨"));
        }));
        var client = new PokeApiClient(http, temporary.Path);

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetEvolutionLineAsync(1));

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task Graphql_base_index_is_parsed_and_written_to_cache()
    {
        using var temporary = TemporaryDirectory.Create();
        using var http = new HttpClient(new StubHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://graphql.pokeapi.co/v1beta2", request.RequestUri!.AbsoluteUri);
            return Json("""{"data":{"pokemonspecies":[{"id":1,"capture_rate":45},{"id":4,"capture_rate":255}]}}""");
        }));
        var client = new PokeApiClient(http, temporary.Path);

        var index = await client.GetBaseSpeciesIndexAsync();

        Assert.Collection(
            index,
            item => Assert.Equal(new BasePokemonSpecies(1, 45), item),
            item => Assert.Equal(new BasePokemonSpecies(4, 255), item));
        Assert.True(File.Exists(Path.Combine(temporary.Path, "base-index.json")));
    }

    [Fact]
    public async Task Sprite_is_served_from_disk_cache_when_the_network_is_later_offline()
    {
        using var temporary = TemporaryDirectory.Create();
        byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3];
        using (var online = new HttpClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(png),
        })))
        {
            var store = new PokemonSpriteStore(online, temporary.Path);
            Assert.Equal(png, await store.GetSpriteAsync(1, shiny: false));
        }

        using var offline = new HttpClient(new StubHttpHandler(_ =>
            throw new HttpRequestException("offline")));
        var cachedStore = new PokemonSpriteStore(offline, temporary.Path);

        Assert.Equal(png, await cachedStore.GetSpriteAsync(1, shiny: false));
    }

    [Theory]
    [InlineData("http://pokeapi.co/api/v2/evolution-chain/1/")]
    [InlineData("https://example.com/api/v2/evolution-chain/1/")]
    [InlineData("https://pokeapi.co/api/v2/pokemon-species/1/")]
    [InlineData("not-a-url")]
    public void Evolution_chain_url_guard_rejects_untrusted_locations(string raw)
    {
        Assert.Null(PokeApiClient.ValidateEvolutionChainUri(raw));
    }

    private static string SpeciesJson(
        int id,
        string name,
        int captureRate,
        string chainUrl,
        string koreanName,
        string? evolvesFrom = null) => JsonSerializer.Serialize(new
        {
            id,
            name,
            capture_rate = captureRate,
            is_legendary = false,
            is_mythical = false,
            names = new[]
            {
                new { name = koreanName, language = new { name = "ko", url = (string?)null } },
                new { name, language = new { name = "en", url = (string?)null } },
            },
            evolution_chain = new { url = chainUrl },
            evolves_from_species = evolvesFrom is null
                ? null
                : new { name = evolvesFrom, url = (string?)null },
        });

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(handler(request));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PokeTokenBarApiTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
