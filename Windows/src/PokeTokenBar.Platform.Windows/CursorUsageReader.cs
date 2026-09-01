using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using PokeTokenBar.Core;

namespace PokeTokenBar.Platform.Windows;

public sealed class CursorUsageReader
{
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, DatabaseCache> _cache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<UsageEntry> ReadEntries(
        IEnumerable<string> roots,
        DateTimeOffset modifiedSince,
        CancellationToken cancellationToken = default)
    {
        var combined = new Dictionary<string, UsageEntry>(StringComparer.Ordinal);
        foreach (var root in roots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var databasePath = string.Equals(
                Path.GetExtension(root),
                ".vscdb",
                StringComparison.OrdinalIgnoreCase)
                ? root
                : Path.Combine(root, "state.vscdb");
            if (!File.Exists(databasePath))
            {
                continue;
            }

            foreach (var entry in ReadDatabase(databasePath, modifiedSince, cancellationToken))
            {
                combined[entry.Id] = entry;
            }
        }

        return combined.Values.OrderBy(entry => entry.Timestamp).ToArray();
    }

    public static UsageEntry? ParseBubble(
        string key,
        string payload,
        DateTimeOffset modifiedSince,
        string databaseIdentity = "")
    {
        if (!key.StartsWith("bubbleId:", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("tokenCount", out var tokens)
                || tokens.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var input = SafeLong(tokens, "inputTokens");
            var output = SafeLong(tokens, "outputTokens");
            if (UsageMath.SaturatingAdd(input, output) == 0
                || !root.TryGetProperty("createdAt", out var createdAt)
                || FlexibleTimestamp(createdAt) is not { } timestamp
                || timestamp < modifiedSince)
            {
                return null;
            }

            var model = root.TryGetProperty("modelType", out var modelType)
                        && modelType.ValueKind == JsonValueKind.String
                ? modelType.GetString() ?? "unknown"
                : "unknown";
            var identity = string.IsNullOrWhiteSpace(databaseIdentity)
                ? string.Empty
                : $"{Path.GetFullPath(databaseIdentity).ToUpperInvariant()}|";
            return new UsageEntry(
                $"cursor|{identity}{key}",
                timestamp,
                DateOnly.FromDateTime(timestamp.LocalDateTime),
                model,
                input,
                output,
                0,
                0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private IReadOnlyList<UsageEntry> ReadDatabase(
        string databasePath,
        DateTimeOffset modifiedSince,
        CancellationToken cancellationToken)
    {
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(databasePath, out var cache))
            {
                cache = new DatabaseCache();
                _cache[databasePath] = cache;
            }

            if (!NativeSqlite.TryOpenReadOnly(databasePath, out var database))
            {
                return Filter(cache.Entries.Values, modifiedSince);
            }

            using (database)
            {
                if (!database.TryScalarInt64("SELECT MAX(rowid) FROM cursorDiskKV", out var maxRowId))
                {
                    return Filter(cache.Entries.Values, modifiedSince);
                }

                if (cache.HighWaterRowId > maxRowId)
                {
                    cache.Entries.Clear();
                    cache.HighWaterRowId = 0;
                }

                var sql = cache.HighWaterRowId == 0
                    ? "SELECT rowid, key, value FROM cursorDiskKV WHERE key GLOB 'bubbleId:*'"
                    : "SELECT rowid, key, value FROM cursorDiskKV NOT INDEXED WHERE rowid > ?1 AND key GLOB 'bubbleId:*'";
                if (!database.TryPrepare(sql, out var statement))
                {
                    return Filter(cache.Entries.Values, modifiedSince);
                }

                using (statement)
                {
                    if (cache.HighWaterRowId > 0)
                    {
                        statement.BindInt64(1, cache.HighWaterRowId);
                    }

                    while (statement.Step())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var rowId = statement.ColumnInt64(0);
                        cache.HighWaterRowId = Math.Max(cache.HighWaterRowId, rowId);
                        var key = statement.ColumnText(1);
                        var payload = statement.ColumnText(2);
                        if (key is null || payload is null)
                        {
                            continue;
                        }

                        var entry = ParseBubble(
                            key,
                            payload,
                            DateTimeOffset.MinValue,
                            databasePath);
                        if (entry is not null)
                        {
                            cache.Entries[entry.Id] = entry;
                        }
                    }
                }

                cache.HighWaterRowId = Math.Max(cache.HighWaterRowId, maxRowId);
                return Filter(cache.Entries.Values, modifiedSince);
            }
        }
    }

    private static IReadOnlyList<UsageEntry> Filter(
        IEnumerable<UsageEntry> entries,
        DateTimeOffset modifiedSince) =>
        entries.Where(entry => entry.Timestamp >= modifiedSince).ToArray();

    private static DateTimeOffset? FlexibleTimestamp(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed)
                ? parsed
                : null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var epoch))
        {
            return null;
        }

        try
        {
            return epoch >= 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)epoch)
                : DateTimeOffset.FromUnixTimeSeconds((long)epoch);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static long SafeLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        if (value.TryGetInt64(out var integer))
        {
            return Math.Clamp(integer, 0, CodexUsageReader.MaxParsedTokenValue);
        }

        if (!value.TryGetDouble(out var number) || !double.IsFinite(number) || number <= 0)
        {
            return 0;
        }

        return number >= CodexUsageReader.MaxParsedTokenValue
            ? CodexUsageReader.MaxParsedTokenValue
            : (long)number;
    }

    private sealed class DatabaseCache
    {
        public long HighWaterRowId { get; set; }

        public Dictionary<string, UsageEntry> Entries { get; } = new(StringComparer.Ordinal);
    }

    private static class NativeSqlite
    {
        private const int SqliteOpenReadOnly = 0x00000001;
        private const int SqliteRow = 100;

        public static bool TryOpenReadOnly(string path, out Database database)
        {
            var result = sqlite3_open_v2(path, out var handle, SqliteOpenReadOnly, IntPtr.Zero);
            if (result == 0 && handle != IntPtr.Zero)
            {
                database = new Database(handle);
                return true;
            }

            if (handle != IntPtr.Zero)
            {
                sqlite3_close(handle);
            }

            database = null!;
            return false;
        }

        internal sealed class Database : IDisposable
        {
            private IntPtr _handle;

            public Database(IntPtr handle) => _handle = handle;

            public bool TryScalarInt64(string sql, out long value)
            {
                value = 0;
                if (!TryPrepare(sql, out var statement))
                {
                    return false;
                }

                using (statement)
                {
                    if (!statement.Step())
                    {
                        return true;
                    }

                    value = statement.ColumnInt64(0);
                    return true;
                }
            }

            public bool TryPrepare(string sql, out Statement statement)
            {
                var result = sqlite3_prepare_v2(_handle, sql, -1, out var handle, IntPtr.Zero);
                if (result == 0 && handle != IntPtr.Zero)
                {
                    statement = new Statement(handle);
                    return true;
                }

                if (handle != IntPtr.Zero)
                {
                    sqlite3_finalize(handle);
                }

                statement = null!;
                return false;
            }

            public void Dispose()
            {
                if (_handle != IntPtr.Zero)
                {
                    sqlite3_close(_handle);
                    _handle = IntPtr.Zero;
                }
            }
        }

        internal sealed class Statement : IDisposable
        {
            private IntPtr _handle;

            public Statement(IntPtr handle) => _handle = handle;

            public void BindInt64(int index, long value) => sqlite3_bind_int64(_handle, index, value);

            public bool Step() => sqlite3_step(_handle) == SqliteRow;

            public long ColumnInt64(int index) => sqlite3_column_int64(_handle, index);

            public string? ColumnText(int index)
            {
                var pointer = sqlite3_column_text(_handle, index);
                if (pointer == IntPtr.Zero)
                {
                    return null;
                }

                var length = sqlite3_column_bytes(_handle, index);
                return Marshal.PtrToStringUTF8(pointer, length);
            }

            public void Dispose()
            {
                if (_handle != IntPtr.Zero)
                {
                    sqlite3_finalize(_handle);
                    _handle = IntPtr.Zero;
                }
            }
        }

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open_v2(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
            out IntPtr database,
            int flags,
            IntPtr virtualFileSystem);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(
            IntPtr database,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
            int bytes,
            out IntPtr statement,
            IntPtr tail);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr statement);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr statement);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern long sqlite3_column_int64(IntPtr statement, int index);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text(IntPtr statement, int index);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_bytes(IntPtr statement, int index);
    }
}
