using System.Text.Json;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class RemoteCodexUsageProviderTests
{
    [Fact]
    public async Task Remote_provider_excludes_usage_already_present_in_local_sessions()
    {
        using var temporary = TemporaryDirectory.Create();
        var local = Path.Combine(temporary.Path, "local");
        var cache = Path.Combine(temporary.Path, "cache");
        var remote = Path.Combine(cache, RemoteCodexUsageProvider.CacheName("devbox-a"));
        Directory.CreateDirectory(local);
        Directory.CreateDirectory(remote);
        var now = DateTimeOffset.Now;
        File.WriteAllLines(Path.Combine(local, "local.jsonl"),
        [
            SessionMeta("shared"),
            TokenCount(now, 100, 10),
        ]);
        File.WriteAllLines(Path.Combine(remote, "shared.jsonl"),
        [
            SessionMeta("shared"),
            TokenCount(now, 100, 10),
        ]);
        File.WriteAllLines(Path.Combine(remote, "remote.jsonl"),
        [
            SessionMeta("remote-only"),
            TokenCount(now, 200, 20),
        ]);

        var provider = new RemoteCodexUsageProvider(
            () => ["devbox-a"], cache, [local], new NoOpSynchronizer());

        var snapshot = await provider.FetchAsync(now);

        Assert.NotNull(snapshot);
        Assert.Equal(220, snapshot.Today?.TotalTokens);
        Assert.Equal("Codex (원격)", snapshot.DisplayName);
    }

    [Fact]
    public void Synchronizer_output_appends_then_resets_compact_cache_atomically()
    {
        using var temporary = TemporaryDirectory.Create();
        var manifestPath = Path.Combine(temporary.Path, "manifest.json");
        var manifest = new Dictionary<string, RemoteCodexLogSynchronizer.CacheState>(StringComparer.Ordinal);
        const string source = ".codex/sessions/a.jsonl";
        var metadata = SessionMeta("remote-a");
        var token = TokenCount(DateTimeOffset.Now, 100, 10);

        RemoteCodexLogSynchronizer.ApplyOutput(temporary.Path, manifestPath, manifest,
            Envelope("record", source, metadata) + "\n" + FileEnvelope(source, 100, reset: false));
        var cachePath = Path.Combine(temporary.Path, Assert.Single(manifest).Value.CacheFile);
        Assert.Single(File.ReadAllLines(cachePath));

        RemoteCodexLogSynchronizer.ApplyOutput(temporary.Path, manifestPath, manifest,
            Envelope("record", source, token) + "\n" + FileEnvelope(source, 200, reset: false));
        Assert.Equal(2, File.ReadAllLines(cachePath).Length);
        Assert.Equal(200, manifest[source].Offset);

        RemoteCodexLogSynchronizer.ApplyOutput(temporary.Path, manifestPath, manifest,
            Envelope("record", source, metadata) + "\n" + FileEnvelope(source, 50, reset: true));
        Assert.Single(File.ReadAllLines(cachePath));
        Assert.Equal(50, manifest[source].Offset);
        Assert.True(File.Exists(manifestPath));
    }

    private static string SessionMeta(string id) => JsonSerializer.Serialize(new
    {
        timestamp = DateTimeOffset.Now,
        type = "session_meta",
        payload = new { id, session_id = id, thread_source = "user" },
    });

    private static string TokenCount(DateTimeOffset timestamp, long input, long output) =>
        JsonSerializer.Serialize(new
        {
            timestamp,
            type = "event_msg",
            payload = new
            {
                type = "token_count",
                info = new
                {
                    total_token_usage = new { input_tokens = input, cached_input_tokens = 0, output_tokens = output, total_tokens = input + output },
                    last_token_usage = new { input_tokens = input, cached_input_tokens = 0, output_tokens = output, total_tokens = input + output },
                },
            },
        });

    private static string Envelope(string kind, string path, string record) =>
        $$"""{"kind":"{{kind}}","path":"{{path}}","record":{{record}}}""";

    private static string FileEnvelope(string path, long offset, bool reset) =>
        JsonSerializer.Serialize(new { kind = "file", path, offset, reset });

    private sealed class NoOpSynchronizer : IRemoteCodexLogSynchronizer
    {
        public Task SyncAsync(string host, string cacheDirectory, DateTimeOffset since,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }
        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PokeTokenBar-remote-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
