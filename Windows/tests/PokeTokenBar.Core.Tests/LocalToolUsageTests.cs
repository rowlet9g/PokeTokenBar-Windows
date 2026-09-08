using System.Runtime.InteropServices;
using System.Text.Json;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class LocalToolUsageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PtbLocalTools-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-08T12:00:00+09:00");
    private const string Claude = """
        {"type":"assistant","timestamp":"2026-09-08T02:00:00Z","requestId":"r1","message":{"id":"m1","model":"test-model","usage":{"input_tokens":100,"output_tokens":20,"cache_creation_input_tokens":30,"cache_read_input_tokens":40}}}
        """;
    private const string Pi = """
        {"id":"p1","type":"message","timestamp":"2026-09-08T02:00:00Z","message":{"role":"assistant","model":"test-model","usage":{"input":100,"output":20,"cacheWrite":30,"cacheRead":40,"reasoning":10,"totalTokens":999}}}
        """;
    private const string OpenCode = """
        {"id":"o1","modelID":"test-model","providerID":"test","time":{"created":1788832800000},"tokens":{"input":100,"output":20,"cache":{"write":30,"read":40},"total":200}}
        """;

    public LocalToolUsageTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("claude_code", 190)]
    [InlineData("pi", 190)]
    [InlineData("omp", 190)]
    [InlineData("opencode", 200)]
    public void Real_metadata_shapes_map_cache_and_reasoning_without_double_counting(string provider, long total)
    {
        var json = provider switch { "claude_code" => Claude, "opencode" => OpenCode, _ => Pi };
        var entry = Parse(provider, json)!;
        Assert.Equal(total, entry.Total);
        Assert.Equal(100, entry.Input);
        Assert.Equal(30, entry.CacheWrite);
        Assert.Equal(40, entry.CacheRead);
        Assert.Equal(total - 170, entry.Output);
    }

    [Theory]
    [InlineData("inputTokens", 100, 80, 20, 120)]
    [InlineData("input_tokens", 100, 80, 100, 200)]
    [InlineData("inputTokens", 100, 150, 0, 120)]
    public void Grok_camel_case_includes_cache_but_snake_case_excludes_it(string inputKey, long input, long cached, long expectedInput, long total)
    {
        var record = """
            {"timestamp":1789000000,"params":{"_meta":{"agentTimestampMs":1788832800000},"update":{"sessionUpdate":"turn_completed","prompt_id":"turn1","usage":{"INPUT_KEY":INPUT_VALUE,"outputTokens":20,"cachedReadTokens":CACHE_VALUE}}}}
            """.Replace("INPUT_KEY", inputKey).Replace("INPUT_VALUE", input.ToString()).Replace("CACHE_VALUE", cached.ToString());
        var entry = Parse("grok", record)!;
        Assert.Equal(total, entry.Total);
        Assert.Equal(expectedInput, entry.Input);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1788832800000), entry.Timestamp);
        Assert.Null(Parse("grok", record.Replace("\"agentTimestampMs\"", "\"isReplay\":true,\"agentTimestampMs\"")));
    }

    [Fact]
    public void Grok_turn_identity_deduplicates_forks_and_subagents_are_excluded_before_cache()
    {
        const string grok = """
            {"timestamp":1788832800,"update":{"sessionUpdate":"turn_completed","prompt_id":"turn","usage":{"inputTokens":10,"outputTokens":5}}}
            """;
        Write("parent/updates.jsonl", grok);
        Write("fork/updates.jsonl", grok);
        Write("sub/updates.jsonl", grok.Replace("\"turn\"", "\"subturn\""));
        var reader = new LocalToolUsageReader("grok");
        Assert.Equal(2, Read(reader).Count);
        Write("sub/summary.json", """{"session_kind":"subagent_fork"}""");
        Assert.Single(Read(reader));
    }

    [Fact]
    public void Claude_streaming_snapshots_and_copies_keep_max_and_earliest_timestamp()
    {
        Write("a.jsonl", Claude + "\n" + Claude.Replace("\"output_tokens\":20", "\"output_tokens\":50"));
        Write("copy/b.jsonl", Claude.Replace("02:00:00", "03:00:00"));
        var reader = new LocalToolUsageReader("claude_code");
        var entry = Assert.Single(Read(reader));
        Assert.Equal(220, entry.Total);
        Assert.Equal(DateTimeOffset.Parse("2026-09-08T02:00:00Z"), entry.Timestamp);
        Assert.Equal(entry, Assert.Single(Read(reader)));
    }

    [Fact]
    public void Partial_lines_are_retried_and_truncated_files_are_rescanned()
    {
        var path = Write("a.jsonl", Claude + "\n{\"usage\":");
        var reader = new LocalToolUsageReader("claude_code");
        Assert.Equal(190, Assert.Single(Read(reader)).Total);
        File.WriteAllText(path, Claude.Replace("\"m1\"", "\"m2\"").Replace("\"input_tokens\":100", "\"input_tokens\":10"));
        Assert.Equal(100, Assert.Single(Read(reader)).Total);
    }

    [Theory]
    [InlineData("pi")]
    [InlineData("omp")]
    public void Pi_and_omp_skip_errors_and_include_compaction(string provider)
    {
        Assert.Null(Parse(provider, Pi.Replace("\"usage\"", "\"stopReason\":\"aborted\",\"usage\"")));
        var compact = """{"id":"compact","type":"compaction","timestamp":"2026-09-08T02:00:00Z","usage":{"totalTokens":12}}""";
        Assert.Equal(12, Parse(provider, compact)!.Total);
        Assert.Null(Parse(provider, compact.Replace("2026-09-08T02:00:00Z", "invalid")));
    }

    [Fact]
    public void Omp_ids_are_session_scoped_and_bridge_copies_do_not_count()
    {
        Write("one.jsonl", Pi); Write("two.jsonl", Pi); Write("bridge/three.jsonl", Pi);
        Assert.Equal(380, Read(new LocalToolUsageReader("omp")).Sum(entry => entry.Total));
    }

    [Fact]
    public async Task Provider_periods_sum_metadata_and_do_not_invent_subscription_costs()
    {
        Write("one.jsonl", Claude);
        var reader = new LocalToolUsageReader("claude_code");
        var provider = new LocalToolUsageProvider("claude_code", "Claude Code", (since, token) => reader.ReadEntries([_root], since, token));
        var snapshot = await provider.FetchAsync(Now);
        Assert.NotNull(snapshot);
        Assert.Equal(190, snapshot.TodayTotalTokens);
        Assert.Equal(190, snapshot.WeekTotal!.TotalTokens);
        Assert.Equal(190, snapshot.MonthTotal!.TotalTokens);
        Assert.False(snapshot.ReportsCost);
        var empty = new LocalToolUsageProvider("empty", "Empty", (_, _) => []);
        Assert.Null(await empty.FetchAsync(Now));
    }

    [Fact]
    public void OpenCode_database_and_legacy_files_deduplicate_and_mutable_rows_refresh()
    {
        var db = Path.Combine(_root, "opencode.db");
        Execute(db, "CREATE TABLE message (id TEXT, data TEXT);");
        Execute(db, $"INSERT INTO message VALUES ('o1', '{OpenCode}');");
        Write("storage/message/o1.json", OpenCode);
        var reader = new LocalToolUsageReader("opencode");
        Assert.Equal(200, Assert.Single(Read(reader)).Total);
        Execute(db, $"UPDATE message SET data = '{OpenCode.Replace("\"total\":200", "\"total\":250")}';");
        Assert.Equal(250, Assert.Single(Read(reader)).Total);
    }

    [Fact]
    public void Hermes_session_counters_include_reasoning_and_refresh_updates()
    {
        var db = Path.Combine(_root, "state.db");
        Execute(db, "CREATE TABLE sessions (id TEXT, model TEXT, started_at REAL, input_tokens INTEGER, output_tokens INTEGER, cache_read_tokens INTEGER, cache_write_tokens INTEGER, reasoning_tokens INTEGER);");
        Execute(db, "INSERT INTO sessions VALUES ('h1','test',1788832800,100,20,40,30,10);");
        var reader = new LocalToolUsageReader("hermes");
        Assert.Equal(200, Assert.Single(Read(reader)).Total);
        Execute(db, "UPDATE sessions SET output_tokens = 30;");
        Assert.Equal(210, Assert.Single(Read(reader)).Total);
    }

    [Fact]
    public async Task Database_failure_preserves_provider_snapshot_and_recovers()
    {
        var db = Path.Combine(_root, "opencode.db");
        Execute(db, "CREATE TABLE message (id TEXT, data TEXT);");
        Execute(db, $"INSERT INTO message VALUES ('o1', '{OpenCode}');");
        var reader = new LocalToolUsageReader("opencode");
        using var store = new UsageStore([new LocalToolUsageProvider("opencode", "OpenCode", (_, token) => reader.ReadEntries([_root], DateTimeOffset.MinValue, token))]);
        await store.RefreshAsync();
        var before = Assert.Single(store.Snapshots);
        Execute(db, "ALTER TABLE message RENAME TO missing;");
        await store.RefreshAsync();
        Assert.NotNull(store.LastErrorDescription);
        Assert.Equal(before.MonthTotal, Assert.Single(store.Snapshots).MonthTotal);
        Execute(db, "ALTER TABLE missing RENAME TO message;");
        await store.RefreshAsync();
        Assert.Null(store.LastErrorDescription);
    }

    [Fact]
    public void Kiro_sqlite_versions_deduplicate_and_keep_cleared_turns_in_process()
    {
        var db = Path.Combine(_root, "data.sqlite3");
        const string json = """{"conversation_id":"k1","history":[{"user":{"content":"12345678","images":"ignore"},"assistant":{"content":"1234"},"request_metadata":{"request_start_timestamp_ms":1788832800000,"response_size":8}},{"user":{"content":"1234"},"assistant":{"content":"1234"},"request_metadata":{"request_start_timestamp_ms":1788832810000,"response_size":4}}]}""";
        Execute(db, "CREATE TABLE conversations_v2 (conversation_id TEXT, value TEXT); CREATE TABLE conversations (value TEXT);");
        Execute(db, $"INSERT INTO conversations_v2 VALUES ('k1','{json}'); INSERT INTO conversations VALUES ('{json}');");
        var reader = new LocalToolUsageReader("kiro");
        var entries = Read(reader);
        Assert.Equal(2, entries.Count);
        Assert.Equal(9, entries.Sum(entry => entry.Total));
        Execute(db, "DELETE FROM conversations_v2; DELETE FROM conversations;");
        Assert.Equal(9, Read(reader).Sum(entry => entry.Total));
    }

    [Fact]
    public void Kiro_cli_jsonl_uses_text_only_and_clear_resets_history()
    {
        Write("cli/k.jsonl", """
            {"kind":"Prompt","data":{"content":[{"kind":"text","data":"12345678"},{"kind":"image","data":"should not count"}],"meta":{"timestamp":"2026-09-08T02:00:00Z"}}}
            {"kind":"AssistantMessage","data":{"content":"1234"}}
            {"kind":"Clear"}
            {"kind":"Prompt","data":{"content":"1234","meta":{"timestamp":"2026-09-08T02:01:00Z"}}}
            {"kind":"AssistantMessage","data":{"content":"1234"}}
            """);
        Assert.Equal(5, Read(new LocalToolUsageReader("kiro")).Sum(entry => entry.Total));
    }

    [Fact]
    public void Kiro_v3_summaries_do_not_turn_credits_into_tokens()
    {
        Write("v3/session.json", """{"id":"v3","modelId":"test","createdAt":"2026-09-08T02:00:00Z"}""");
        Write("v3/messages.jsonl", """
            {"payload":{"type":"user","content":"12345678"}}
            {"payload":{"type":"assistant","content":[{"text":"1234"}]}}
            {"payload":{"type":"usage_summary","credits":100000}}
            {"payload":{"type":"turn_end"}}
            {"role":"user","content":"1234"}
            {"role":"assistant","content":"1234"}
            """);
        Assert.Equal(8, Read(new LocalToolUsageReader("kiro")).Sum(entry => entry.Total));
    }

    [Fact]
    public void Roots_keep_defaults_and_environment_overrides_without_duplicates()
    {
        var home = _root;
        var env = new Dictionary<string, string> { ["PI_CODING_AGENT_DIR"] = Path.Combine(home, ".pi", "agent"),
            ["PI_CODING_AGENT_SESSION_DIR"] = Path.Combine(home, "custom") };
        var paths = WindowsLocalToolPaths.CreateRoots("pi", home, home, name => env.GetValueOrDefault(name));
        Assert.Equal(2, paths.Count);
        foreach (var id in WindowsLocalToolPaths.ProviderIds)
            Assert.NotEmpty(WindowsLocalToolPaths.CreateRoots(id, home, home, _ => null));
        Assert.Empty(new LocalToolUsageReader("pi").ReadEntries([Path.Combine(home, "absent")], DateTimeOffset.MinValue));
    }

    private IReadOnlyList<UsageEntry> Read(LocalToolUsageReader reader) => reader.ReadEntries([_root, _root], DateTimeOffset.MinValue);
    private static UsageEntry? Parse(string provider, string json)
    {
        using var document = JsonDocument.Parse(json);
        return LocalToolUsageParser.Parse(provider, document.RootElement, "test.jsonl|1");
    }
    private string Write(string relative, string contents)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }
    private static void Execute(string path, string sql)
    {
        Assert.Equal(0, sqlite3_open(path, out var db));
        try { Assert.Equal(0, sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero)); }
        finally { sqlite3_close(db); }
    }
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr context, IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
