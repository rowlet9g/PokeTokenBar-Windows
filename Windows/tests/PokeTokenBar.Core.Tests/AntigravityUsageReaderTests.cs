using System.Runtime.InteropServices;
using System.Text;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class AntigravityUsageReaderTests
{
    private static readonly DateTimeOffset SampleTime =
        new(2026, 9, 4, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Generation_metadata_maps_every_token_category()
    {
        var entry = AntigravityUsageReader.ParseGeneration(
            "conversation.db",
            7,
            GenerationBlob(
                "gemini-3.1-pro-high",
                "response-7",
                SampleTime,
                systemInput: 1_000,
                freshInput: 200,
                cacheRead: 300,
                output: 40,
                thinking: 50),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.MinValue);

        Assert.NotNull(entry);
        Assert.Equal("gemini-3.1-pro", entry.Model);
        Assert.Equal(SampleTime, entry.Timestamp);
        Assert.Equal(1_200, entry.Input);
        Assert.Equal(90, entry.Output);
        Assert.Equal(300, entry.CacheRead);
        Assert.Equal(1_590, entry.Total);
        Assert.Equal("antigravity|response|response-7", entry.Id);
    }

    [Fact]
    public void Invalid_timestamp_uses_fallback_and_zero_usage_is_skipped()
    {
        var fallback = SampleTime.AddHours(2);
        var entry = AntigravityUsageReader.ParseGeneration(
            "conversation.db",
            3,
            GenerationBlob("model", "", null, 10, 20, 0, 3, 2),
            fallback,
            fallback.AddMinutes(-1));

        Assert.NotNull(entry);
        Assert.Equal(fallback, entry.Timestamp);
        Assert.Contains("CONVERSATION.DB|3", entry.Id, StringComparison.OrdinalIgnoreCase);
        Assert.Null(AntigravityUsageReader.ParseGeneration(
            "conversation.db",
            4,
            GenerationBlob("model", "zero", SampleTime, 0, 0, 0, 0, 0),
            fallback,
            DateTimeOffset.MinValue));
    }

    [Fact]
    public void Reader_deduplicates_responses_across_databases_and_adds_rows_incrementally()
    {
        using var temporary = TemporaryDirectory.Create();
        var root = Path.Combine(temporary.Path, "antigravity-cli");
        var conversations = Path.Combine(root, "conversations");
        Directory.CreateDirectory(conversations);
        var firstDatabase = Path.Combine(conversations, "a.db");
        var secondDatabase = Path.Combine(conversations, "b.db");
        Seed(firstDatabase);
        Seed(secondDatabase);
        Insert(firstDatabase, 0, GenerationBlob("gemini-pro-default", "shared", SampleTime, 1, 2, 3, 4, 5));
        Insert(secondDatabase, 0, GenerationBlob("gemini-pro-default", "shared", SampleTime, 100, 200, 300, 400, 500));
        Insert(secondDatabase, 1, GenerationBlob("gemini-pro-default", "second", SampleTime, 10, 20, 30, 40, 50));
        var reader = new AntigravityUsageReader();

        var first = reader.ReadEntries([root], DateTimeOffset.MinValue);
        Insert(firstDatabase, 1, GenerationBlob("gemini-pro-default", "third", SampleTime, 7, 8, 9, 10, 11));
        var second = reader.ReadEntries([root], DateTimeOffset.MinValue);

        Assert.Equal(2, first.Count);
        Assert.Equal(3, second.Count);
        Assert.Single(second, entry => entry.Id.EndsWith("shared", StringComparison.Ordinal));
        Assert.Equal(15, second.Single(entry => entry.Id.EndsWith("shared", StringComparison.Ordinal)).Total);
    }

    [Fact]
    public async Task Provider_aggregates_antigravity_with_its_own_identity()
    {
        using var temporary = TemporaryDirectory.Create();
        var root = Path.Combine(temporary.Path, "antigravity-cli");
        var conversations = Path.Combine(root, "conversations");
        Directory.CreateDirectory(conversations);
        var database = Path.Combine(conversations, "current.db");
        Seed(database);
        Insert(database, 0, GenerationBlob("gemini-pro-default", "one", SampleTime, 100, 200, 300, 40, 50));
        var now = SampleTime.ToOffset(TimeSpan.FromHours(9)).AddHours(1);

        var snapshot = await new AntigravityUsageProvider([root]).FetchAsync(now);

        Assert.NotNull(snapshot);
        Assert.Equal("antigravity", snapshot.ProviderId);
        Assert.Equal("Antigravity", snapshot.DisplayName);
        Assert.Equal(690, snapshot.TodayTotalTokens);
        Assert.False(snapshot.ReportsCost);
    }

    [Fact]
    public void Default_and_override_roots_cover_cli_ide_and_backups()
    {
        var defaults = WindowsAntigravityPaths.CreateDefaultRoots(@"C:\Users\Test", null);
        var overrides = WindowsAntigravityPaths.CreateDefaultRoots(
            @"C:\ignored",
            @"D:\Antigravity;D:\AntigravityBackup,E:\Portable");

        Assert.Equal(4, defaults.Count);
        Assert.Contains(@"C:\Users\Test\.gemini\antigravity-cli", defaults);
        Assert.Contains(@"C:\Users\Test\.gemini\antigravity-ide", defaults);
        Assert.Equal(3, overrides.Count);
    }

    private static byte[] GenerationBlob(
        string model,
        string responseId,
        DateTimeOffset? timestamp,
        ulong systemInput,
        ulong freshInput,
        ulong cacheRead,
        ulong output,
        ulong thinking)
    {
        var usage = new List<byte>();
        VarintField(usage, 1, systemInput);
        VarintField(usage, 2, freshInput);
        VarintField(usage, 5, cacheRead);
        VarintField(usage, 9, output);
        VarintField(usage, 10, thinking);
        LengthField(usage, 11, Encoding.UTF8.GetBytes(responseId));

        var chatModel = new List<byte>();
        LengthField(chatModel, 4, usage.ToArray());
        if (timestamp is { } instant)
        {
            var stamp = new List<byte>();
            VarintField(stamp, 1, (ulong)instant.ToUnixTimeSeconds());
            VarintField(stamp, 2, (ulong)((instant.Ticks % TimeSpan.TicksPerSecond) * 100));
            var info = new List<byte>();
            LengthField(info, 4, stamp.ToArray());
            LengthField(chatModel, 9, info.ToArray());
        }

        LengthField(chatModel, 19, Encoding.UTF8.GetBytes(model));
        var result = new List<byte>();
        LengthField(result, 1, chatModel.ToArray());
        return result.ToArray();
    }

    private static void VarintField(List<byte> target, ulong field, ulong value)
    {
        Varint(target, field << 3);
        Varint(target, value);
    }

    private static void LengthField(List<byte> target, ulong field, byte[] value)
    {
        Varint(target, field << 3 | 2);
        Varint(target, (ulong)value.Length);
        target.AddRange(value);
    }

    private static void Varint(List<byte> target, ulong value)
    {
        do
        {
            var current = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0)
            {
                current |= 0x80;
            }

            target.Add(current);
        }
        while (value != 0);
    }

    private static void Seed(string databasePath) => Execute(databasePath, """
        CREATE TABLE gen_metadata (
          idx INTEGER PRIMARY KEY,
          data BLOB,
          size INTEGER NOT NULL DEFAULT 0);
        """);

    private static void Insert(string databasePath, int id, byte[] blob) => Execute(
        databasePath,
        $"INSERT INTO gen_metadata (idx, data, size) VALUES ({id}, X'{Convert.ToHexString(blob)}', {blob.Length});");

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
                $"PokeTokenBarAntigravityTests-{Guid.NewGuid():N}");
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
