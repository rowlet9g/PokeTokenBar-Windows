using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class AppUpdateTests
{
    private static readonly byte[] Package = Encoding.UTF8.GetBytes("fixture installer content");
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Package)).ToLowerInvariant();
    private const string VersionString = "1.0.1";
    private const string InstallerName = "PokeTokenBar-1.0.1-win-x64-setup.exe";
    private const string Prefix = "https://github.com/rowlet9g/PokeTokenBar-Windows/releases/download/v1.0.1/";

    [Fact]
    public async Task Stable_new_release_is_manifest_validated_and_downloaded_with_progress()
    {
        using var fixture = new Fixture();
        var release = await fixture.Client.CheckAsync(new Version(1, 0, 0, 0));
        Assert.NotNull(release);
        Assert.Equal(new Version(1, 0, 1), release.Version);
        Assert.Equal(Hash, release.Sha256);
        var progress = new CaptureProgress();
        var path = await fixture.Client.DownloadAsync(release, fixture.Path, progress);
        Assert.Equal(Package, File.ReadAllBytes(path));
        Assert.Equal(1, progress.Latest);
        await AppUpdateClient.VerifyFileAsync(path, Hash);
        File.AppendAllText(path, "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => AppUpdateClient.VerifyFileAsync(path, Hash));
    }

    [Theory]
    [InlineData("equal")] [InlineData("older")] [InlineData("draft")] [InlineData("prerelease")]
    public async Task Current_older_and_unpublished_releases_never_propose_an_update(string mode)
    {
        using var fixture = new Fixture(mode);
        var current = mode == "older" ? new Version(2, 0, 0) : new Version(1, 0, 1);
        if (mode is "draft" or "prerelease") current = new Version(0, 7, 0);
        Assert.Null(await fixture.Client.CheckAsync(current));
        Assert.Equal(1, fixture.RequestCount);
    }

    [Theory]
    [InlineData("malformed-version")] [InlineData("wrong-host")] [InlineData("wrong-repo")]
    [InlineData("missing-asset")] [InlineData("duplicate-asset")] [InlineData("pending-asset")]
    [InlineData("oversized-installer")] [InlineData("empty-installer")]
    [InlineData("wrong-manifest-version")] [InlineData("wrong-runtime")]
    [InlineData("wrong-manifest-installer")] [InlineData("not-self-contained")]
    [InlineData("missing-checksum")] [InlineData("duplicate-checksum")]
    public async Task Untrusted_or_inconsistent_release_is_rejected_before_download(string mode)
    {
        using var fixture = new Fixture(mode);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Client.CheckAsync(new Version(1, 0, 0)));
        Assert.False(fixture.InstallerRequested);
    }

    [Theory]
    [InlineData("corrupt-package")] [InlineData("truncated-package")] [InlineData("extra-package")]
    public async Task Damaged_package_is_never_promoted_to_an_executable_and_partial_file_is_removed(string mode)
    {
        using var fixture = new Fixture(mode);
        var release = (await fixture.Client.CheckAsync(new Version(1, 0, 0)))!;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Client.DownloadAsync(release, fixture.Path));
        Assert.Empty(Directory.GetFiles(fixture.Path, "*.exe", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(fixture.Path, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Cancellation_and_offline_checks_do_not_create_an_installer()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Client.CheckAsync(new Version(1, 0, 0), cancellation.Token));
        using var offline = new Fixture("offline");
        await Assert.ThrowsAsync<HttpRequestException>(() => offline.Client.CheckAsync(new Version(1, 0, 0)));
        Assert.Empty(Directory.GetFiles(fixture.Path, "*.exe", SearchOption.AllDirectories));
    }

    private sealed class CaptureProgress : IProgress<double>
    {
        public double Latest;
        public void Report(double value) => Latest = value;
    }

    private sealed class Fixture : HttpMessageHandler, IDisposable
    {
        private readonly string _mode;
        private readonly HttpClient _http;
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PokeUpdate-{Guid.NewGuid():N}");
        public AppUpdateClient Client { get; }
        public int RequestCount;
        public bool InstallerRequested;
        public Fixture(string mode = "normal")
        {
            _mode = mode;
            Directory.CreateDirectory(Path);
            _http = new HttpClient(this, disposeHandler: false);
            Client = new AppUpdateClient(_http);
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            RequestCount++;
            if (_mode == "offline") throw new HttpRequestException("offline");
            var url = request.RequestUri!.AbsoluteUri;
            if (url.EndsWith("/latest"))
            {
                Assert.Contains("PokeTokenBar", request.Headers.UserAgent.ToString());
                var names = new List<string> { InstallerName, "release-manifest.json", "SHA256SUMS.txt" };
                if (_mode == "missing-asset") names.Remove(InstallerName);
                if (_mode == "duplicate-asset") names.Add(InstallerName);
                var json = JsonSerializer.Serialize(new
                {
                    tag_name = _mode == "malformed-version" ? "v1.0.1-beta" : "v1.0.1",
                    draft = _mode == "draft", prerelease = _mode == "prerelease",
                    assets = names.Select(name => new
                    {
                        name, state = _mode == "pending-asset" ? "new" : "uploaded",
                        size = _mode == "oversized-installer" ? AppUpdateClient.MaximumInstallerBytes + 1 : _mode == "empty-installer" ? 0 : Package.Length,
                        browser_download_url = (_mode == "wrong-host" ? Prefix.Replace("github.com", "example.com") :
                            _mode == "wrong-repo" ? Prefix.Replace("rowlet9g", "attacker") : Prefix) + name,
                    }),
                });
                return Task.FromResult(Text(json));
            }
            if (url.EndsWith("release-manifest.json"))
                return Task.FromResult(Text(JsonSerializer.Serialize(new { product = "PokeTokenBar", version = _mode == "wrong-manifest-version" ? "2.0.0" : VersionString,
                    runtime = _mode == "wrong-runtime" ? "win-arm64" : "win-x64", selfContained = _mode != "not-self-contained",
                    installer = _mode == "wrong-manifest-installer" ? "../evil.exe" : InstallerName })));
            if (url.EndsWith("SHA256SUMS.txt"))
            {
                var checksum = $"{Hash} *{InstallerName}\n";
                return Task.FromResult(Text(_mode == "missing-checksum" ? "" : _mode == "duplicate-checksum" ? checksum + checksum : checksum));
            }
            InstallerRequested = true;
            var bytes = _mode switch { "corrupt-package" => Encoding.UTF8.GetBytes(new string('x', Package.Length)),
                "truncated-package" => Package[..^1], "extra-package" => Package.Concat(new byte[] { 1 }).ToArray(), _ => Package };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
        private static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
        public new void Dispose()
        {
            _http.Dispose();
            base.Dispose();
            Directory.Delete(Path, true);
        }
    }
}
