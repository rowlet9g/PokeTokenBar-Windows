using System.Globalization;
using System.Text.Json;

namespace PokeTokenBar.Core;

public sealed class GeminiUsageReader
{
    public IReadOnlyList<UsageEntry> ReadEntries(
        IEnumerable<string> roots,
        DateTimeOffset modifiedSince,
        CancellationToken cancellationToken = default)
    {
        var entries = new Dictionary<string, UsageEntry>(StringComparer.Ordinal);
        foreach (var root in NormalizeRoots(roots))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(
                        root,
                        "*",
                        new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = true,
                            AttributesToSkip = FileAttributes.ReparsePoint,
                        })
                    .Where(path => string.Equals(
                            Path.GetExtension(path),
                            ".json",
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            Path.GetExtension(path),
                            ".jsonl",
                            StringComparison.OrdinalIgnoreCase));
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < modifiedSince.UtcDateTime)
                    {
                        continue;
                    }

                    foreach (var entry in ParseFile(file, cancellationToken))
                    {
                        if (!entries.TryGetValue(entry.Id, out var existing)
                            || entry.Total > existing.Total
                            || entry.Timestamp < existing.Timestamp)
                        {
                            entries[entry.Id] = entry;
                        }
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (JsonException)
                {
                }
            }
        }

        return entries.Values.OrderBy(entry => entry.Timestamp).ToArray();
    }

    public IReadOnlyList<UsageEntry> ParseFile(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(filePath);
        return string.Equals(Path.GetExtension(fullPath), ".jsonl", StringComparison.OrdinalIgnoreCase)
            ? ParseJsonLines(fullPath, cancellationToken)
            : ParseLegacyJson(fullPath);
    }

    private static IReadOnlyList<UsageEntry> ParseJsonLines(
        string filePath,
        CancellationToken cancellationToken)
    {
        var byId = new Dictionary<string, UsageEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        DateTimeOffset? fallbackTimestamp = null;
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!line.Contains("\"tokens\"", StringComparison.Ordinal)
                && !line.Contains("\"timestamp\"", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                fallbackTimestamp = Timestamp(root) ?? fallbackTimestamp;
                Absorb(root, filePath, fallbackTimestamp, byId, order);
            }
            catch (JsonException)
            {
                // A partially written last JSONL record must not hide older valid usage.
            }
        }

        return order.Select(id => byId[id]).ToArray();
    }

    private static IReadOnlyList<UsageEntry> ParseLegacyJson(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("messages", out var messages)
            || messages.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var byId = new Dictionary<string, UsageEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        var fallbackTimestamp = root.TryGetProperty("startTime", out var start)
            ? Timestamp(start)
            : null;
        foreach (var message in messages.EnumerateArray())
        {
            Absorb(message, filePath, fallbackTimestamp, byId, order);
        }

        return order.Select(id => byId[id]).ToArray();
    }

    private static void Absorb(
        JsonElement raw,
        string filePath,
        DateTimeOffset? fallbackTimestamp,
        IDictionary<string, UsageEntry> byId,
        ICollection<string> order)
    {
        if (raw.ValueKind != JsonValueKind.Object
            || !raw.TryGetProperty("tokens", out var tokens)
            || tokens.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var timestamp = Timestamp(raw) ?? fallbackTimestamp;
        if (timestamp is null)
        {
            return;
        }

        var messageId = StringValue(raw, "id") ?? Guid.NewGuid().ToString("N");
        var canonicalFile = Path.GetFullPath(filePath).ToUpperInvariant();
        var id = $"gemini|{canonicalFile}|{messageId}";
        var input = SafeLong(tokens, "input");
        var cached = SafeLong(tokens, "cached");
        var nonCached = Math.Max(0, input - cached);
        var entry = new UsageEntry(
            id,
            timestamp.Value,
            DateOnly.FromDateTime(timestamp.Value.LocalDateTime),
            StringValue(raw, "model") ?? "gemini",
            UsageMath.SaturatingAdd(nonCached, SafeLong(tokens, "tool")),
            UsageMath.SaturatingAdd(
                SafeLong(tokens, "output"),
                SafeLong(tokens, "thoughts")),
            0,
            cached);
        if (!byId.ContainsKey(messageId))
        {
            order.Add(messageId);
        }

        byId[messageId] = entry;
    }

    private static DateTimeOffset? Timestamp(JsonElement raw)
    {
        if (raw.ValueKind == JsonValueKind.String)
        {
            return DateTimeOffset.TryParse(
                raw.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var value)
                ? value
                : null;
        }

        if (raw.ValueKind != JsonValueKind.Object
            || !raw.TryGetProperty("timestamp", out var timestamp))
        {
            return null;
        }

        return Timestamp(timestamp);
    }

    private static string? StringValue(JsonElement raw, string name) =>
        raw.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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

    private static IReadOnlyList<string> NormalizeRoots(IEnumerable<string> roots)
    {
        var candidates = roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Length)
            .ToArray();
        var kept = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!kept.Any(parent => candidate.StartsWith(
                    parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)))
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }
}
