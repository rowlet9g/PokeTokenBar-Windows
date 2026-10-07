using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PokeTokenBar.Core;

public sealed record AppUpdateRelease(Version Version, Uri InstallerUrl, long InstallerBytes, string Sha256)
{
    public string InstallerName => $"PokeTokenBar-{Version}-win-x64-setup.exe";
}

/// <summary>Checks the fixed public repository and downloads only manifest-validated, hashed installers.</summary>
public sealed class AppUpdateClient(HttpClient http)
{
    public const string Repository = "rowlet9g/PokeTokenBar-Windows";
    public const long MaximumInstallerBytes = 250_000_000;
    private const int MaximumMetadataBytes = 1_000_000;
    private static readonly Uri LatestRelease = new($"https://api.github.com/repos/{Repository}/releases/latest");

    public async Task<AppUpdateRelease?> CheckAsync(Version currentVersion, CancellationToken token = default)
    {
        using var document = JsonDocument.Parse(await ReadMetadataAsync(LatestRelease, token).ConfigureAwait(false));
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$") || !Version.TryParse(tag[1..], out var version)
            || tag != $"v{version}") throw new InvalidDataException("Invalid stable release version.");
        var current = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));
        if (version <= current) return null;
        var installerName = $"PokeTokenBar-{version}-win-x64-setup.exe";
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        JsonElement Asset(string name)
        {
            var matching = assets.Where(a => a.GetProperty("name").GetString() == name).ToArray();
            if (matching.Length != 1 || matching[0].GetProperty("state").GetString() != "uploaded")
                throw new InvalidDataException($"Release asset is missing or incomplete: {name}.");
            return matching[0];
        }
        Uri AssetUri(JsonElement asset, string name)
        {
            var expected = new Uri($"https://github.com/{Repository}/releases/download/{tag}/{name}");
            if (asset.GetProperty("browser_download_url").GetString() != expected.AbsoluteUri)
                throw new InvalidDataException("Release asset location is not trusted.");
            return expected;
        }
        var installer = Asset(installerName);
        var size = installer.GetProperty("size").GetInt64();
        if (size is <= 0 or > MaximumInstallerBytes) throw new InvalidDataException("Invalid installer size.");
        var installerUri = AssetUri(installer, installerName);
        using var manifest = JsonDocument.Parse(await ReadMetadataAsync(
            AssetUri(Asset("release-manifest.json"), "release-manifest.json"), token).ConfigureAwait(false));
        var m = manifest.RootElement;
        if (m.GetProperty("product").GetString() != "PokeTokenBar" || m.GetProperty("version").GetString() != version.ToString()
            || m.GetProperty("runtime").GetString() != "win-x64" || !m.GetProperty("selfContained").GetBoolean()
            || m.GetProperty("installer").GetString() != installerName)
            throw new InvalidDataException("Release manifest does not match the installer.");
        var checksums = System.Text.Encoding.UTF8.GetString(await ReadMetadataAsync(
            AssetUri(Asset("SHA256SUMS.txt"), "SHA256SUMS.txt"), token).ConfigureAwait(false));
        var hashes = new List<string>();
        foreach (var line in checksums.Split('\n'))
        {
            var match = Regex.Match(line.Trim().TrimStart('\uFEFF'), @"^([a-fA-F0-9]{64})\s+\*?([^\r\n]+)$");
            if (match.Success && match.Groups[2].Value == installerName) hashes.Add(match.Groups[1].Value);
        }
        if (hashes.Count != 1) throw new InvalidDataException("Installer SHA-256 is missing or ambiguous.");
        return new AppUpdateRelease(version, installerUri, size, hashes[0].ToLowerInvariant());
    }

    public async Task<string> DownloadAsync(AppUpdateRelease release, string cacheDirectory,
        IProgress<double>? progress = null, CancellationToken token = default)
    {
        if (release.InstallerUrl.AbsoluteUri != $"https://github.com/{Repository}/releases/download/v{release.Version}/{release.InstallerName}"
            || release.InstallerBytes is <= 0 or > MaximumInstallerBytes
            || !Regex.IsMatch(release.Sha256, "^[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("Invalid update package.");
        var directory = Path.Combine(Path.GetFullPath(cacheDirectory), $"{release.Version}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, release.InstallerName);
        var partial = destination + ".partial";
        try
        {
            using var request = NewRequest(release.InstallerUrl);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != release.InstallerBytes)
                throw new InvalidDataException("Installer download size does not match the release.");
            await using (var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    var count = await source.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (count == 0) break;
                    total += count;
                    if (total > release.InstallerBytes) throw new InvalidDataException("Installer exceeds its declared size.");
                    hash.AppendData(buffer, 0, count);
                    await target.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    progress?.Report(total / (double)release.InstallerBytes);
                }
                if (total != release.InstallerBytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installer checksum or length validation failed.");
                await target.FlushAsync(token).ConfigureAwait(false);
            }
            File.Move(partial, destination);
            return destination;
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    public static async Task VerifyFileAsync(string path, string sha256, CancellationToken token = default)
    {
        await using var stream = File.OpenRead(path);
        var actual = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
        if (!Convert.ToHexString(actual).Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded update file has changed.");
    }

    private async Task<byte[]> ReadMetadataAsync(Uri uri, CancellationToken token)
    {
        using var request = NewRequest(uri);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumMetadataBytes) throw new InvalidDataException("Oversized release metadata.");
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await source.ReadAsync(buffer, token).ConfigureAwait(false);
            if (count == 0) break;
            if (result.Length + count > MaximumMetadataBytes) throw new InvalidDataException("Oversized release metadata.");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }

    private static HttpRequestMessage NewRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PokeTokenBar", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(uri.Host == "api.github.com" ? "application/vnd.github+json" : "application/octet-stream"));
        return request;
    }
}
