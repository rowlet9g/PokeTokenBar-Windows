using System.Text.Json;
using PokeTokenBar.Core;
using static PokeTokenBar.Core.LocalToolUsageParser;

namespace PokeTokenBar.Platform.Windows;

public sealed class LocalToolUsageReader(string provider)
{
    private readonly Dictionary<string, FileCache> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UsageEntry> _kiroHistory = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public IReadOnlyList<UsageEntry> ReadEntries(IEnumerable<string> roots, DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var result = new Dictionary<string, UsageEntry>(StringComparer.Ordinal);
            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();
            foreach (var root in roots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    foreach (var file in SourceFiles(root))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!seenFiles.Add(file)) continue;
                        try
                        {
                            if (provider == "grok" && IsGrokSubagent(file)) continue;
                            if (provider == "omp" && file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                .Contains("bridge", StringComparer.OrdinalIgnoreCase)) continue;
                            foreach (var entry in ReadFile(file, cancellationToken))
                                if (entry.Timestamp >= since) Merge(result, entry);
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                        {
                            errors.Add($"{provider}: {Path.GetFileName(file)} ({error.GetType().Name})");
                        }
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{provider}: scan failed ({error.GetType().Name})");
                }
            }
            // Surface failures so UsageStore preserves the last provider snapshot instead of publishing a partial total.
            if (errors.Count > 0) throw new IOException(string.Join("; ", errors.Distinct()));
            foreach (var key in _files.Keys.Where(key => !seenFiles.Contains(key)).ToArray()) _files.Remove(key);
            if (provider == "kiro")
            {
                foreach (var entry in result.Values) Merge(_kiroHistory, entry);
                foreach (var key in _kiroHistory.Where(pair => pair.Value.Timestamp < since).Select(pair => pair.Key).ToArray())
                    _kiroHistory.Remove(key);
                return _kiroHistory.Values.OrderBy(entry => entry.Timestamp).ToArray();
            }
            return result.Values.OrderBy(entry => entry.Timestamp).ToArray();
        }
    }

    private static void Merge(Dictionary<string, UsageEntry> entries, UsageEntry entry)
    {
        if (!entries.TryGetValue(entry.Id, out var previous)) entries[entry.Id] = entry;
        else
        {
            var best = entry.Total > previous.Total ? entry : previous;
            var earliest = entry.Timestamp < previous.Timestamp ? entry : previous;
            entries[entry.Id] = best with { Timestamp = earliest.Timestamp, LocalDay = earliest.LocalDay };
        }
    }

    private IEnumerable<string> SourceFiles(string root)
    {
        if (File.Exists(root)) { yield return root; yield break; }
        if (!Directory.Exists(root)) yield break;
        if (provider is "opencode" or "hermes" or "kiro")
        {
            var name = provider switch { "opencode" => "opencode.db", "hermes" => "state.db", _ => "data.sqlite3" };
            var database = Path.Combine(root, name);
            if (File.Exists(database)) yield return database;
            else if (provider == "opencode")
                foreach (var channel in Directory.EnumerateFiles(root, "opencode-*.db")) yield return channel;
            if (provider == "hermes") yield break;
            if (provider == "opencode") root = Path.Combine(root, "storage", "message");
        }
        if (!Directory.Exists(root)) yield break;
        foreach (var file in Directory.EnumerateFiles(root, provider == "opencode" ? "*.json" : "*.jsonl",
            new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            if (provider == "kiro" && Path.GetFileName(file) != "messages.jsonl"
                && Path.GetFileName(Path.GetDirectoryName(file)) != "cli") continue;
            if (provider != "grok" || Path.GetFileName(file) == "updates.jsonl") yield return file;
        }
    }

    private IReadOnlyList<UsageEntry> ReadFile(string file, CancellationToken cancellationToken)
    {
        // Kiro companion metadata can change without the JSONL mtime changing; DB WAL can change independently too.
        var isDatabase = Path.GetExtension(file) is ".db" or ".sqlite3";
        if (isDatabase) return ReadDatabase(file, cancellationToken);
        var info = new FileInfo(file);
        if (provider != "kiro" && _files.TryGetValue(file, out var cached)
            && cached.Size == info.Length && cached.Modified == info.LastWriteTimeUtc) return cached.Entries;
        List<UsageEntry> entries = [];
        var incomplete = false;
        if (provider == "kiro") entries.AddRange(KiroUsageParser.ParseFile(file, cancellationToken));
        else if (Path.GetExtension(file) == ".json")
        {
            using var document = ReadJson(file);
            if (Parse(provider, document.RootElement, Path.GetFileNameWithoutExtension(file)) is { } entry) entries.Add(entry);
        }
        else
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lineNumber = 0;
            while (reader.ReadLine() is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineNumber++;
                if (!line.Contains("\"usage\"", StringComparison.Ordinal)) continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var fallback = provider == "omp" ? $"{Path.GetFileName(file)}|{lineNumber}" : $"{file}|{lineNumber}";
                    if (Parse(provider, document.RootElement, fallback) is { } entry) entries.Add(entry);
                }
                catch (JsonException) { incomplete = true; }
            }
        }
        if (!incomplete && provider != "kiro") _files[file] = new FileCache(info.Length, info.LastWriteTimeUtc, entries);
        return entries;
    }

    internal static JsonDocument ReadJson(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonDocument.Parse(stream);
    }

    private static bool IsGrokSubagent(string file)
    {
        var summary = Path.Combine(Path.GetDirectoryName(file)!, "summary.json");
        if (!File.Exists(summary)) return false;
        using var document = ReadJson(summary);
        return Text(document.RootElement, "session_kind")?.StartsWith("subagent", StringComparison.Ordinal) == true;
    }

    private IReadOnlyList<UsageEntry> ReadDatabase(string file, CancellationToken cancellationToken)
    {
        if (!WindowsSqlite.TryOpenReadOnly(file, out var database)) throw new IOException("Cannot open usage database.");
        using (database)
        {
            List<UsageEntry> entries = [];
            bool Query(string sql, Action<WindowsSqlite.Statement> read)
            {
                if (!database.TryPrepare(sql, out var statement)) return false;
                using (statement)
                    while (statement.StepChecked()) { cancellationToken.ThrowIfCancellationRequested(); read(statement); }
                return true;
            }
            void JsonRow(WindowsSqlite.Statement row, int column, string fallback)
            {
                if (row.ColumnText(column) is not { } json) return;
                try
                {
                    using var document = JsonDocument.Parse(json);
                    if (provider == "kiro") entries.AddRange(KiroUsageParser.ParseConversation(document.RootElement, fallback));
                    else if (Parse(provider, document.RootElement, fallback) is { } entry) entries.Add(entry);
                }
                catch (JsonException) { /* An incomplete row can be retried next refresh. */ }
            }
            var success = false;
            if (provider == "opencode") success = Query("SELECT id, data FROM message", row => JsonRow(row, 1, row.ColumnText(0) ?? file));
            if (provider == "hermes") success = Query(
                "SELECT id, model, started_at, input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, reasoning_tokens FROM sessions",
                row =>
                {
                    if (string.IsNullOrWhiteSpace(row.ColumnText(0)) || string.IsNullOrWhiteSpace(row.ColumnText(1))) return;
                    using var timestamp = JsonDocument.Parse(JsonSerializer.Serialize(row.ColumnText(2)));
                    if (Entry("hermes|" + row.ColumnText(0), Date(timestamp.RootElement), row.ColumnText(1),
                        Math.Max(0, row.ColumnInt64(3)), UsageMath.SaturatingAdd(Math.Max(0, row.ColumnInt64(4)), Math.Max(0, row.ColumnInt64(7))),
                        Math.Max(0, row.ColumnInt64(6)), Math.Max(0, row.ColumnInt64(5))) is { } entry) entries.Add(entry);
                });
            if (provider == "kiro")
            {
                success = Query("SELECT conversation_id, value FROM conversations_v2", row => JsonRow(row, 1, row.ColumnText(0) ?? file));
                success = Query("SELECT value FROM conversations", row => JsonRow(row, 0, file)) || success;
            }
            if (!success) throw new IOException("Unsupported or unavailable usage database schema.");
            return entries;
        }
    }

    private sealed record FileCache(long Size, DateTime Modified, IReadOnlyList<UsageEntry> Entries);
}
