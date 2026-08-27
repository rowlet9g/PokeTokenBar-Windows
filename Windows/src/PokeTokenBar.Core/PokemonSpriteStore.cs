namespace PokeTokenBar.Core;

public sealed class PokemonSpriteStore
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;

    public PokemonSpriteStore(HttpClient httpClient, string cacheDirectory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<byte[]?> GetSpriteAsync(
        int speciesId,
        bool shiny,
        CancellationToken cancellationToken = default)
    {
        if (!PokemonAssets.HasSprite(speciesId))
        {
            return null;
        }

        var fileName = shiny ? $"shiny-{speciesId}.png" : $"{speciesId}.png";
        var path = Path.Combine(_cacheDirectory, fileName);
        var cached = await ReadValidPngAsync(path, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var variant = shiny ? "shiny/" : string.Empty;
        var uri = new Uri(
            $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{variant}{speciesId}.png");
        using var response = await _httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 1_048_576)
        {
            throw new InvalidDataException("Pokémon sprite exceeded the 1 MiB safety limit.");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (!HasPngSignature(bytes))
        {
            throw new InvalidDataException("Pokémon sprite response was not a PNG image.");
        }

        var temporaryPath = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
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
                // A stale temporary cache is harmless.
            }
            catch (UnauthorizedAccessException)
            {
                // A stale temporary cache is harmless.
            }
        }

        return bytes;
    }

    private static async Task<byte[]?> ReadValidPngAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return HasPngSignature(bytes) ? bytes : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool HasPngSignature(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= PngSignature.Length
        && bytes[..PngSignature.Length].SequenceEqual(PngSignature);
}
