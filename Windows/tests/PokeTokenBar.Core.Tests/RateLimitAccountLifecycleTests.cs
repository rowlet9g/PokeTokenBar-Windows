using System.Net;
using System.Text.Json;
using PokeTokenBar.Core;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class RateLimitAccountLifecycleTests
{
    [Fact]
    public void Explicit_Claude_config_directory_does_not_fall_back_to_another_account()
    {
        var paths = ClaudeRateLimitsProvider.ResolveCredentialPaths(
            @"C:\Users\Fixture", name => name == "CLAUDE_CONFIG_DIR" ? @"D:\TeamClaude" : null);

        Assert.Equal(@"D:\TeamClaude\.credentials.json", Assert.Single(paths));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("logged-out")]
    [InlineData("expired")]
    public async Task Claude_account_unavailable_removes_old_limits_and_login_restores_them(string state)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PtbLimits-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ".credentials.json");
        try
        {
            WriteCredential(path, "fixture-first", DateTimeOffset.UtcNow.AddHours(1));
            using var handler = new UsageHandler();
            using var client = new HttpClient(handler);
            using var store = new RateLimitStore([new ClaudeRateLimitsProvider(client, [path])]);
            await store.RefreshAsync();
            Assert.Equal(25, Assert.Single(store.Snapshots).Windows[0].UsedPercent);

            handler.Fail = true;
            await store.RefreshAsync();
            Assert.Equal(25, Assert.Single(store.Snapshots).Windows[0].UsedPercent);
            Assert.NotNull(store.LastErrorDescription);
            handler.Fail = false;
            var requestsBeforeLogout = handler.RequestCount;

            switch (state)
            {
                case "missing": File.Delete(path); break;
                case "logged-out": File.WriteAllText(path, "{\"claudeAiOauth\":null}"); break;
                case "expired": WriteCredential(path, "fixture-first", DateTimeOffset.UtcNow.AddHours(-1)); break;
            }
            await store.RefreshAsync();
            Assert.Empty(store.Snapshots);
            Assert.Null(store.LastErrorDescription);
            Assert.Equal(requestsBeforeLogout, handler.RequestCount);

            WriteCredential(path, "fixture-second", DateTimeOffset.UtcNow.AddHours(1));
            await store.RefreshAsync();
            Assert.Equal(60, Assert.Single(store.Snapshots).Windows[0].UsedPercent);
            Assert.Null(store.LastErrorDescription);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task Antigravity_logout_removes_old_limits_without_another_request()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PtbLimits-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "token.json");
        try
        {
            File.WriteAllText(path, "{\"token\":\"fixture-antigravity\"}");
            using var handler = new UsageHandler();
            using var client = new HttpClient(handler);
            using var store = new RateLimitStore([new AntigravityRateLimitsProvider(client, [path])]);
            await store.RefreshAsync();
            Assert.Single(store.Snapshots);
            File.Delete(path);
            await store.RefreshAsync();
            Assert.Empty(store.Snapshots);
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static void WriteCredential(string path, string token, DateTimeOffset expiry) =>
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            claudeAiOauth = new { accessToken = token, expiresAt = expiry.ToUnixTimeMilliseconds() },
        }));

    private sealed class UsageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var token = request.Headers.Authorization?.Parameter;
            var json = token == "fixture-antigravity"
                ? "{\"groups\":[{\"displayName\":\"Gemini\",\"buckets\":[{\"bucketId\":\"5h\",\"remainingFraction\":0.75}]}]}"
                : JsonSerializer.Serialize(new { five_hour = new { utilization = token == "fixture-first" ? 25 : 60 } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            });
        }
    }
}
