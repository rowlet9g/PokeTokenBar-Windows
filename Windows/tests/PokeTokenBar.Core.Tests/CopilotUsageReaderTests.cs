using System.Runtime.InteropServices;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class CopilotUsageReaderTests
{
    [Fact]
    public void Cached_prompt_tokens_are_counted_once()
    {
        var entry = CopilotUsageReader.MapRow(
            "session-store.db",
            1,
            "claude-opus-5",
            43_792,
            443,
            42_698,
            904,
            "2026-09-01T10:00:00.000Z",
            DateTimeOffset.MinValue);

        Assert.NotNull(entry);
        Assert.Equal(190, entry.Input);
        Assert.Equal(42_698, entry.CacheRead);
        Assert.Equal(904, entry.CacheWrite);
        Assert.Equal(443, entry.Output);
        Assert.Equal(44_235, entry.Total);
    }

    [Theory]
    [InlineData("2026-09-01T10:00:00.000Z", "2026-09-01T10:00:00+00:00")]
    [InlineData("2026-09-01 11:00:00", "2026-09-01T11:00:00+00:00")]
    [InlineData("2026-08-31T20:00:00-05:00", "2026-09-01T01:00:00+00:00")]
    public void Stored_timestamp_shapes_are_normalized(string raw, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected), CopilotUsageReader.ParseTimestamp(raw));
    }

    [Fact]
    public void Invalid_old_and_zero_rows_are_skipped()
    {
        Assert.Null(CopilotUsageReader.MapRow(
            "db", 1, null, 0, 0, 0, 0, "2026-09-01T10:00:00Z", DateTimeOffset.MinValue));
        Assert.Null(CopilotUsageReader.MapRow(
            "db", 1, null, 1, 0, 0, 0, "not-a-date", DateTimeOffset.MinValue));
        Assert.Null(CopilotUsageReader.MapRow(
            "db", 1, null, 1, 0, 0, 0, "2026-09-01T10:00:00Z",
            new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Default_and_override_roots_are_deterministic()
    {
        var defaults = WindowsCopilotPaths.CreateDefaultRoots(@"C:\Users\Test", null);
        var overrides = WindowsCopilotPaths.CreateDefaultRoots(
            @"C:\ignored",
            @"D:\CopilotA;D:\CopilotB");

        Assert.Equal([@"C:\Users\Test\.copilot"], defaults);
        Assert.Equal(2, overrides.Count);
    }

    [Fact]
    public void Reader_scans_usage_table_and_incrementally_adds_rows()
    {
        using var temporary = TemporaryDirectory.Create();
        var database = Path.Combine(temporary.Path, "session-store.db");
        Seed(database);
        Insert(database, 1, 100, 10, 0, 0, "2026-09-01T10:00:00Z");
        var reader = new CopilotUsageReader();

        var first = reader.ReadEntries([temporary.Path], DateTimeOffset.MinValue);
        Insert(database, 2, 300, 30, 50, 100, "2026-09-01 11:00:00");
        var second = reader.ReadEntries([temporary.Path], DateTimeOffset.MinValue);

        Assert.Single(first);
        Assert.Equal(2, second.Count);
        Assert.Equal(440, second.Sum(entry => entry.Total));
    }

    [Fact]
    public void Recreated_store_resets_high_water_and_cached_history()
    {
        using var temporary = TemporaryDirectory.Create();
        var database = Path.Combine(temporary.Path, "session-store.db");
        Seed(database);
        Insert(database, 1, 100, 10, 0, 0, "2026-09-01T10:00:00Z");
        Insert(database, 2, 200, 20, 0, 0, "2026-09-01T10:01:00Z");
        var reader = new CopilotUsageReader();
        Assert.Equal(2, reader.ReadEntries([temporary.Path], DateTimeOffset.MinValue).Count);

        File.Delete(database);
        Seed(database);
        Insert(database, 1, 700, 70, 0, 0, "2026-09-01T11:00:00Z");
        var after = reader.ReadEntries([temporary.Path], DateTimeOffset.MinValue);

        var entry = Assert.Single(after);
        Assert.Equal(770, entry.Total);
    }

    [Fact]
    public async Task Provider_aggregates_today_and_reports_tokens_only()
    {
        using var temporary = TemporaryDirectory.Create();
        var database = Path.Combine(temporary.Path, "session-store.db");
        Seed(database);
        Insert(database, 1, 100, 10, 0, 0, "2026-09-01T01:00:00Z");
        var now = new DateTimeOffset(2026, 9, 1, 10, 30, 0, TimeSpan.FromHours(9));

        var snapshot = await new CopilotUsageProvider([temporary.Path]).FetchAsync(now);

        Assert.NotNull(snapshot);
        Assert.Equal("copilot", snapshot.ProviderId);
        Assert.Equal(110, snapshot.TodayTotalTokens);
        Assert.False(snapshot.ReportsCost);
    }

    private static void Seed(string databasePath) => Execute(databasePath, """
        CREATE TABLE assistant_usage_events (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          model TEXT NOT NULL,
          input_tokens INTEGER,
          output_tokens INTEGER,
          cache_read_tokens INTEGER,
          cache_write_tokens INTEGER,
          created_at TEXT);
        """);

    private static void Insert(
        string databasePath,
        int id,
        int input,
        int output,
        int cacheRead,
        int cacheWrite,
        string createdAt) => Execute(databasePath,
        $"INSERT INTO assistant_usage_events VALUES ({id}, 'gpt-5.4-mini', {input}, {output}, {cacheRead}, {cacheWrite}, '{createdAt}');");

    private static void Execute(string databasePath, string sql)
    {
        Assert.Equal(0, sqlite3_open(databasePath, out var database));
        try
        {
            var result = sqlite3_exec(database, sql, IntPtr.Zero, IntPtr.Zero, out var error);
            var message = error == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(error);
            if (error != IntPtr.Zero)
            {
                sqlite3_free(error);
            }

            Assert.True(result == 0, message);
        }
        finally
        {
            sqlite3_close(database);
        }
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
        out IntPtr database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(
        IntPtr database,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
        IntPtr callback,
        IntPtr context,
        out IntPtr errorMessage);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void sqlite3_free(IntPtr pointer);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr database);

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PokeTokenBarCopilotTests-{Guid.NewGuid():N}");
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
