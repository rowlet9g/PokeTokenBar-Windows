using System.Globalization;
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

            if (!WindowsSqlite.TryOpenReadOnly(databasePath, out var database))
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

}
