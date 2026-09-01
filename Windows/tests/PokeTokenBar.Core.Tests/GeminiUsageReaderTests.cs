namespace PokeTokenBar.Core.Tests;

public sealed class GeminiUsageReaderTests
{
    private const string NewJsonLines = """
        {"type":"session_metadata","sessionId":"s1","startTime":"2026-09-01T01:00:00.000Z"}
        {"type":"user","id":"m1","timestamp":"2026-09-01T01:00:05.000Z","content":[{"text":"hi"}]}
        {"type":"gemini","id":"m2","timestamp":"2026-09-01T01:00:10.000Z","model":"gemini-2.5-pro","tokens":{"input":1000,"output":50,"cached":600,"thoughts":30,"tool":20,"total":1100}}
        {"type":"gemini","id":"m3","timestamp":"2026-09-01T01:01:00.000Z","model":"gemini-2.5-flash","tokens":{"input":10,"output":5,"cached":0,"thoughts":0,"tool":0,"total":15}}
        {"type":"message_update","id":"m3","tokens":{"input":10,"output":8,"cached":0,"thoughts":2,"tool":0,"total":20}}
        """;

    private const string LegacyJson = """
        {"sessionId":"s0","startTime":"2026-08-31T00:00:00.000Z","messages":[
          {"id":"a1","type":"gemini","timestamp":"2026-08-31T00:10:00.000Z","model":"gemini-2.5-pro","tokens":{"input":100,"output":10,"cached":0,"thoughts":0,"tool":0,"total":110}},
          {"id":"a2","type":"user","content":[{"text":"x"}]}
        ]}
        """;

    [Fact]
    public void Jsonl_mapping_preserves_total_and_last_update_wins()
    {
        using var temporary = TemporaryDirectory.Create();
        var file = temporary.Write("hash/chats/session-a.jsonl", NewJsonLines);

        var entries = new GeminiUsageReader().ParseFile(file);

        Assert.Equal(2, entries.Count);
        Assert.Equal((420, 80, 600, 1_100),
            (entries[0].Input, entries[0].Output, entries[0].CacheRead, entries[0].Total));
        Assert.Equal((10, 10, 20),
            (entries[1].Input, entries[1].Output, entries[1].Total));
    }

    [Fact]
    public void Legacy_json_reads_only_messages_with_tokens()
    {
        using var temporary = TemporaryDirectory.Create();
        var file = temporary.Write("hash/chats/checkpoint.json", LegacyJson);

        var entry = Assert.Single(new GeminiUsageReader().ParseFile(file));

        Assert.Equal("gemini-2.5-pro", entry.Model);
        Assert.Equal(110, entry.Total);
    }

    [Fact]
    public void Reader_collects_both_formats_and_ignores_prompt_only_json()
    {
        using var temporary = TemporaryDirectory.Create();
        temporary.Write("hash/chats/session-a.jsonl", NewJsonLines);
        temporary.Write("hash/chats/checkpoint.json", LegacyJson);
        temporary.Write("hash/logs.json", "{\"entries\":[{\"message\":\"private prompt\"}]}");

        var entries = new GeminiUsageReader().ReadEntries(
            [temporary.Path],
            DateTimeOffset.UnixEpoch);

        Assert.Equal(3, entries.Count);
        Assert.Equal(1_230, entries.Sum(entry => entry.Total));
    }

    [Fact]
    public void Non_session_json_shapes_are_ignored_without_failing_the_scan()
    {
        using var temporary = TemporaryDirectory.Create();
        temporary.Write("array.json", "[{\"metadata\":true}]");
        temporary.Write("scalar.json", "42");
        temporary.Write("hash/chats/session.jsonl", NewJsonLines);

        var entries = new GeminiUsageReader().ReadEntries(
            [temporary.Path],
            DateTimeOffset.UnixEpoch);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void Absurd_and_negative_counts_are_safely_clamped()
    {
        using var temporary = TemporaryDirectory.Create();
        var file = temporary.Write(
            "session.jsonl",
            "{\"id\":\"g1\",\"timestamp\":\"2026-09-01T01:00:00Z\",\"tokens\":{\"input\":1e30,\"cached\":-2,\"tool\":1e30,\"output\":1e30,\"thoughts\":1e30}}");

        var entry = Assert.Single(new GeminiUsageReader().ParseFile(file));

        Assert.Equal(2 * CodexUsageReader.MaxParsedTokenValue, entry.Input);
        Assert.Equal(2 * CodexUsageReader.MaxParsedTokenValue, entry.Output);
        Assert.Equal(0, entry.CacheRead);
    }

    [Fact]
    public async Task Provider_aggregates_today_week_and_month()
    {
        using var temporary = TemporaryDirectory.Create();
        temporary.Write("hash/chats/session-a.jsonl", NewJsonLines);
        var now = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(9));

        var snapshot = await new GeminiUsageProvider([temporary.Path]).FetchAsync(now);

        Assert.NotNull(snapshot);
        Assert.Equal("gemini", snapshot.ProviderId);
        Assert.Equal(1_120, snapshot.TodayTotalTokens);
        Assert.Equal(1_120, snapshot.WeekTotal?.TotalTokens);
        Assert.Equal(1_120, snapshot.MonthTotal?.TotalTokens);
        Assert.False(snapshot.ReportsCost);
    }

    [Fact]
    public async Task Provider_keeps_month_usage_when_today_is_empty()
    {
        using var temporary = TemporaryDirectory.Create();
        temporary.Write(
            "hash/chats/session.jsonl",
            "{\"id\":\"old\",\"timestamp\":\"2026-09-01T01:00:00Z\",\"tokens\":{\"input\":100,\"output\":10}}");
        var now = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(9));

        var snapshot = await new GeminiUsageProvider([temporary.Path]).FetchAsync(now);

        Assert.NotNull(snapshot);
        Assert.Null(snapshot.Today);
        Assert.Equal(110, snapshot.MonthTotal?.TotalTokens);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PokeTokenBarGeminiTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public string Write(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
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
