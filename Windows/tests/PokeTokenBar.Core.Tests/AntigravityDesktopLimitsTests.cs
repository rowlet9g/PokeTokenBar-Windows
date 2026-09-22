using System.Net;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class AntigravityDesktopLimitsTests
{
    [Fact]
    public async Task Desktop_language_server_returns_official_quota_without_oauth_token_access()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PtbAntigravity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var mainLog = Path.Combine(directory, "main.log");
        var serverLog = Path.Combine(directory, "language_server.log");
        try
        {
            File.WriteAllText(mainLog, "Spawning: language_server --csrf_token fixture-csrf");
            File.WriteAllText(serverLog, "Language server listening on random port at 43210 for HTTP");
            using var handler = new DesktopQuotaHandler();
            using var client = new HttpClient(handler);
            var provider = new AntigravityRateLimitsProvider(
                client,
                [],
                mainLog,
                serverLog);

            var snapshot = await provider.FetchAsync(DateTimeOffset.UtcNow);

            Assert.NotNull(snapshot);
            Assert.Equal("antigravity", snapshot!.ProviderId);
            var window = Assert.Single(snapshot.Windows);
            Assert.Equal("Gemini 주간", window.DisplayName);
            Assert.Equal(25, window.UsedPercent, 5);
            Assert.True(handler.SawCsrfHeader);
            Assert.Equal(43210, handler.RequestUri?.Port);
            Assert.EndsWith(
                "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary",
                handler.RequestUri?.AbsolutePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Desktop_connection_uses_the_latest_launch_and_http_port()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PtbAntigravity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var mainLog = Path.Combine(directory, "main.log");
        var serverLog = Path.Combine(directory, "language_server.log");
        try
        {
            File.WriteAllText(mainLog, """
                Spawning: language_server --csrf_token old-token
                Spawning: language_server --csrf_token current-token
                """);
            File.WriteAllText(serverLog, """
                Language server listening on random port at 10001 for HTTP
                Language server listening on random port at 10002 for HTTP
                """);

            var connection = AntigravityRateLimitsProvider.ResolveDesktopConnection(mainLog, serverLog);

            Assert.NotNull(connection);
            Assert.Equal(10002, connection!.HttpPort);
            Assert.Equal("current-token", connection.CsrfToken);
            Assert.DoesNotContain("current-token", connection.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Desktop_connection_rejects_logs_after_the_server_shuts_down()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PtbAntigravity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var mainLog = Path.Combine(directory, "main.log");
        var serverLog = Path.Combine(directory, "language_server.log");
        try
        {
            File.WriteAllText(mainLog, """
                Spawning: language_server --csrf_token stale-token
                Shutting down language server...
                """);
            File.WriteAllText(serverLog,
                "Language server listening on random port at 10002 for HTTP");

            Assert.Null(AntigravityRateLimitsProvider.ResolveDesktopConnection(mainLog, serverLog));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class DesktopQuotaHandler : HttpMessageHandler
    {
        public bool SawCsrfHeader { get; private set; }

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            SawCsrfHeader = request.Headers.TryGetValues("x-codeium-csrf-token", out var values)
                && values.Single() == "fixture-csrf";
            const string json = """
                {
                  "response": {
                    "groups": [{
                      "displayName": "Gemini Models",
                      "buckets": [{
                        "bucketId": "gemini-weekly",
                        "window": "weekly",
                        "remainingFraction": 0.75,
                        "resetTime": "2030-01-01T00:00:00Z"
                      }]
                    }]
                  }
                }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            });
        }
    }
}
