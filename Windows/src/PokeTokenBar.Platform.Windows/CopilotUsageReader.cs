using System.Globalization;
using PokeTokenBar.Core;

namespace PokeTokenBar.Platform.Windows;

public sealed class CopilotUsageReader
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
                ".db",
                StringComparison.OrdinalIgnoreCase)
                ? root
                : Path.Combine(root, "session-store.db");
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

    public static DateTimeOffset? ParseTimestamp(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length < 19)
        {
            return null;
        }

        if (text.Length > 10 && text[10] == ' ')
        {
            text = string.Concat(text.AsSpan(0, 10), "T", text.AsSpan(11));
        }

        var time = text.Length > 11 ? text[11..] : string.Empty;
        if (!time.Contains('Z', StringComparison.OrdinalIgnoreCase)
            && !time.Contains('+')
            && !time.Contains('-'))
        {
            text += "Z";
        }

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    public static UsageEntry? MapRow(
        string databasePath,
        long rowId,
        string? model,
        long inputTokens,
        long outputTokens,
        long cacheReadTokens,
        long cacheWriteTokens,
        string? createdAt,
        DateTimeOffset modifiedSince)
    {
        if (ParseTimestamp(createdAt) is not { } timestamp || timestamp < modifiedSince)
        {
            return null;
        }

        var inputTotal = Clamp(inputTokens);
        var cacheRead = Clamp(cacheReadTokens);
        var cacheWrite = Clamp(cacheWriteTokens);
        var cached = UsageMath.SaturatingAdd(cacheRead, cacheWrite);
        var uncachedInput = Math.Max(0, inputTotal - cached);
        var output = Clamp(outputTokens);
        if (UsageMath.SaturatingSum(uncachedInput, output, cacheRead, cacheWrite) == 0)
        {
            return null;
        }

        return new UsageEntry(
            $"copilot|{Path.GetFullPath(databasePath).ToUpperInvariant()}|{rowId}",
            timestamp,
            DateOnly.FromDateTime(timestamp.LocalDateTime),
            string.IsNullOrWhiteSpace(model) ? "unknown" : model,
            uncachedInput,
            output,
            cacheWrite,
            cacheRead);
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
                if (!database.TryScalarInt64(
                        "SELECT MAX(id) FROM assistant_usage_events",
                        out var maxRowId))
                {
                    return Filter(cache.Entries.Values, modifiedSince);
                }

                if (cache.HighWaterRowId > maxRowId)
                {
                    cache.Entries.Clear();
                    cache.HighWaterRowId = 0;
                }

                const string columns =
                    "id, model, input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, created_at";
                var sql = cache.HighWaterRowId == 0
                    ? $"SELECT {columns} FROM assistant_usage_events"
                    : $"SELECT {columns} FROM assistant_usage_events WHERE id > ?1";
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
                        var entry = MapRow(
                            databasePath,
                            rowId,
                            statement.ColumnText(1),
                            statement.ColumnInt64(2),
                            statement.ColumnInt64(3),
                            statement.ColumnInt64(4),
                            statement.ColumnInt64(5),
                            statement.ColumnText(6),
                            modifiedSince);
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

    private static long Clamp(long value) =>
        Math.Clamp(value, 0, CodexUsageReader.MaxParsedTokenValue);

    private static IReadOnlyList<UsageEntry> Filter(
        IEnumerable<UsageEntry> entries,
        DateTimeOffset modifiedSince) =>
        entries.Where(entry => entry.Timestamp >= modifiedSince).ToArray();

    private sealed class DatabaseCache
    {
        public long HighWaterRowId { get; set; }

        public Dictionary<string, UsageEntry> Entries { get; } = new(StringComparer.Ordinal);
    }
}
