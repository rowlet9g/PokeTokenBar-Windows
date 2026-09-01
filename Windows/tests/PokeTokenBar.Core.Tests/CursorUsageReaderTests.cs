using System.Runtime.InteropServices;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class CursorUsageReaderTests
{
    [Fact]
    public void Bubble_parser_maps_tokens_model_and_fractional_timestamp()
    {
        var entry = CursorUsageReader.ParseBubble(
            "bubbleId:tab:message",
            "{\"tokenCount\":{\"inputTokens\":1500,\"outputTokens\":800},\"createdAt\":\"2026-09-01T10:34:54.766Z\",\"modelType\":\"claude-3.5-sonnet\"}",
            DateTimeOffset.MinValue);

        Assert.NotNull(entry);
        Assert.Equal(1_500, entry.Input);
        Assert.Equal(800, entry.Output);
        Assert.Equal("claude-3.5-sonnet", entry.Model);
        Assert.Equal("cursor|bubbleId:tab:message", entry.Id);
    }

    [Theory]
    [InlineData("1767312000000")]
    [InlineData("1767312000")]
    public void Bubble_parser_accepts_epoch_millis_and_seconds(string epoch)
    {
        var entry = CursorUsageReader.ParseBubble(
            "bubbleId:t:m",
            $"{{\"tokenCount\":{{\"inputTokens\":7,\"outputTokens\":3}},\"createdAt\":{epoch}}}",
            DateTimeOffset.MinValue);

        Assert.NotNull(entry);
        Assert.Equal(10, entry.Total);
    }

    [Fact]
    public void Bubble_parser_rejects_wrong_key_zero_tokens_old_and_invalid_json()
    {
        var validPayload =
            "{\"tokenCount\":{\"inputTokens\":0,\"outputTokens\":0},\"createdAt\":\"2026-09-01T10:00:00Z\"}";

        Assert.Null(CursorUsageReader.ParseBubble("composerData:x", validPayload, DateTimeOffset.MinValue));
        Assert.Null(CursorUsageReader.ParseBubble("bubbleId:x", validPayload, DateTimeOffset.MinValue));
        Assert.Null(CursorUsageReader.ParseBubble(
            "bubbleId:x",
            validPayload.Replace("0,\"outputTokens\":0", "1,\"outputTokens\":0"),
            new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero)));
        Assert.Null(CursorUsageReader.ParseBubble("bubbleId:x", "not json", DateTimeOffset.MinValue));
    }

    [Fact]
    public void Bubble_parser_clamps_external_numeric_values()
    {
        var entry = CursorUsageReader.ParseBubble(
            "bubbleId:t:m",
            "{\"tokenCount\":{\"inputTokens\":1e30,\"outputTokens\":-5},\"createdAt\":\"2026-09-01T10:00:00Z\"}",
            DateTimeOffset.MinValue);

        Assert.NotNull(entry);
        Assert.Equal(CodexUsageReader.MaxParsedTokenValue, entry.Input);
        Assert.Equal(0, entry.Output);
    }

    [Fact]
    public void Default_roots_cover_stable_and_nightly()
    {
        var roots = WindowsCursorPaths.CreateDefaultRoots(@"C:\Users\Test\AppData\Roaming", null);

        Assert.Contains(roots, path => path.EndsWith(
            @"Cursor\User\globalStorage",
            StringComparison.OrdinalIgnoreCase));
        Assert.Contains(roots, path => path.EndsWith(
            @"Cursor Nightly\User\globalStorage",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reader_scans_sqlite_and_incrementally_adds_new_rows()
    {
        using var temporary = TemporaryDirectory.Create();
        var database = Path.Combine(temporary.Path, "state.vscdb");
        Execute(database, """
            CREATE TABLE cursorDiskKV (key TEXT UNIQUE ON CONFLICT REPLACE, value BLOB);
            INSERT INTO cursorDiskKV VALUES (
              'bubbleId:tab:a',
              '{"tokenCount":{"inputTokens":100,"outputTokens":50},"createdAt":"2026-09-01T10:00:00Z","modelType":"gpt-4o"}');
            INSERT INTO cursorDiskKV VALUES ('composerData:other', '{"unrelated":true}');
            """);
        var reader = new CursorUsageReader();

        var first = reader.ReadEntries([temporary.Path], DateTimeOffset.MinValue);
        Execute(database, """
            INSERT INTO cursorDiskKV VALUES (
              'bubbleId:tab:b',
              '{"tokenCount":{"inputTokens":200,"outputTokens":80},"createdAt":"2026-09-01T11:00:00Z","modelType":"gpt-4o"}');
            """);
        var second = reader.ReadEntries([temporary.Path], DateTimeOffset.MinValue);

        Assert.Single(first);
        Assert.Equal(2, second.Count);
        Assert.Equal(430, second.Sum(entry => entry.Total));
    }

    [Fact]
    public void Database_replacement_resets_cached_high_water_and_history()
    {
        using var temporary = TemporaryDirectory.Create();
        var database = Path.Combine(temporary.Path, "state.vscdb");
        Execute(database, """
            CREATE TABLE cursorDiskKV (key TEXT UNIQUE ON CONFLICT REPLACE, value BLOB);
            INSERT INTO cursorDiskKV VALUES ('bubbleId:old:1', '{"tokenCount":{"inputTokens":10,"outputTokens":5},"createdAt":"2026-09-01T10:00:00Z"}');
            INSERT INTO cursorDiskKV VALUES ('bubbleId:old:2', '{"tokenCount":{"inputTokens":20,"outputTokens":5},"createdAt":"2026-09-01T10:01:00Z"}');
            """);
        var reader = new CursorUsageReader();
        Assert.Equal(2, reader.ReadEntries([temporary.Path], DateTimeOffset.MinValue).Count);

        File.Delete(database);
        Execute(database, """
            CREATE TABLE cursorDiskKV (key TEXT UNIQUE ON CONFLICT REPLACE, value BLOB);
            INSERT INTO cursorDiskKV VALUES ('bubbleId:new:1', '{"tokenCount":{"inputTokens":30,"outputTokens":5},"createdAt":"2026-09-01T11:00:00Z"}');
            """);
        var after = reader.ReadEntries([temporary.Path], DateTimeOffset.MinValue);

        var entry = Assert.Single(after);
        Assert.Contains("bubbleId:new:1", entry.Id);
    }

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
                $"PokeTokenBarCursorTests-{Guid.NewGuid():N}");
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
