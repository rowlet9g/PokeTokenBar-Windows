using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PokeTokenBar.Core;

public sealed class CodexUsageReader
{
    public const long MaxParsedTokenValue = 1_000_000_000_000_000;
    public const int ProbeByteLimit = 1 << 20;

    private const int ProbeChunkSize = 64 * 1024;
    private static readonly TimeSpan ForkReplayMaximumGap = TimeSpan.FromSeconds(1);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, ParsedCacheEntry> _parsedCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProbeCacheEntry> _probeCache =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<UsageEntry> ReadEntries(
        IEnumerable<string> roots,
        DateTimeOffset modifiedSince,
        CancellationToken cancellationToken = default)
    {
        var files = EnumerateRolloutFiles(roots, cancellationToken);
        var windowFiles = files
            .Where(file => file.LastWriteTimeUtc >= modifiedSince.UtcDateTime)
            .ToArray();

        var rolloutsByPath = windowFiles.ToDictionary(
            file => file.Path,
            file => GetParsedRollout(file),
            StringComparer.OrdinalIgnoreCase);
        var includedPaths = windowFiles
            .Select(file => file.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var pendingParentIds = rolloutsByPath.Values
            .Select(rollout => rollout.ParentSessionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var searchedParentIds = new HashSet<string>(StringComparer.Ordinal);

        while (pendingParentIds.Except(searchedParentIds).FirstOrDefault() is { } parentId)
        {
            cancellationToken.ThrowIfCancellationRequested();
            searchedParentIds.Add(parentId);

            if (rolloutsByPath.Values.Any(rollout => rollout.SessionId == parentId))
            {
                continue;
            }

            bool Adopt(IEnumerable<RolloutFile> candidates)
            {
                var resolved = false;
                foreach (var candidate in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var parent = GetParsedRollout(candidate);
                    if (parent.SessionId != parentId)
                    {
                        continue;
                    }

                    rolloutsByPath[parent.Path] = parent;
                    resolved = true;
                    if (!string.IsNullOrWhiteSpace(parent.ParentSessionId))
                    {
                        pendingParentIds.Add(parent.ParentSessionId);
                    }
                }

                return resolved;
            }

            var unresolved = files
                .Where(file => !rolloutsByPath.ContainsKey(file.Path))
                .ToArray();
            var hinted = IsUsableFilenameHint(parentId)
                ? unresolved.Where(file => Path.GetFileName(file.Path).Contains(parentId, StringComparison.Ordinal))
                    .ToArray()
                : [];
            if (Adopt(hinted))
            {
                continue;
            }

            var hintedPaths = hinted
                .Select(file => file.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Adopt(unresolved.Where(file =>
                !hintedPaths.Contains(file.Path)
                && GetProbedSessionId(file) == parentId));
        }

        return ResolveRollouts(rolloutsByPath.Values, includedPaths);
    }

    public string? ProbeSessionId(string path, int byteLimit = ProbeByteLimit)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            ProbeChunkSize,
            FileOptions.SequentialScan);
        if (byteLimit <= 0)
        {
            return null;
        }

        var buffer = new byte[ProbeChunkSize];
        var line = new List<byte>(ProbeChunkSize);
        var readTotal = 0;
        while (readTotal < byteLimit)
        {
            var requested = Math.Min(buffer.Length, byteLimit - readTotal);
            var count = stream.Read(buffer, 0, requested);
            if (count == 0)
            {
                return ProbeLine(CollectionsMarshalHelper.AsSpan(line)).SessionId;
            }

            readTotal += count;
            for (var index = 0; index < count; index++)
            {
                var value = buffer[index];
                if (value != (byte)'\n')
                {
                    line.Add(value);
                    continue;
                }

                var outcome = ProbeLine(CollectionsMarshalHelper.AsSpan(line));
                line.Clear();
                if (outcome.Kind == ProbeKind.SessionFound)
                {
                    return outcome.SessionId;
                }

                if (outcome.Kind is ProbeKind.Stop or ProbeKind.Invalid)
                {
                    return null;
                }
            }
        }

        var finalOutcome = ProbeLine(CollectionsMarshalHelper.AsSpan(line));
        return finalOutcome.Kind == ProbeKind.SessionFound ? finalOutcome.SessionId : null;
    }

    public static bool IsUsableFilenameHint(string id) =>
        id.Length >= 4 && id.Any(char.IsLetterOrDigit);

    private IReadOnlyList<RolloutFile> EnumerateRolloutFiles(
        IEnumerable<string> roots,
        CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive,
        };
        var byPath = new Dictionary<string, RolloutFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in NormalizeRoots(roots))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(root, "*.jsonl", options);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            try
            {
                foreach (var path in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(path);
                        var fullPath = Path.GetFullPath(path);
                        byPath[fullPath] = new RolloutFile(
                            fullPath,
                            info.LastWriteTimeUtc,
                            info.Length);
                    }
                    catch (IOException)
                    {
                        // A session can be archived while enumeration is in progress.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // One unreadable rollout must not hide all other sessions.
                    }
                }
            }
            catch (IOException)
            {
                // The directory itself can disappear during an archive operation.
            }
        }

        return byPath.Values.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> NormalizeRoots(IEnumerable<string> roots)
    {
        var normalized = roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Select(root => root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root.Length)
            .ToList();
        var result = new List<string>();
        foreach (var candidate in normalized)
        {
            if (result.Any(parent =>
                    candidate.Equals(parent, StringComparison.OrdinalIgnoreCase)
                    || candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(candidate);
        }

        return result;
    }

    private ParsedRollout GetParsedRollout(RolloutFile file)
    {
        var stamp = new FileStamp(file.LastWriteTimeUtc.Ticks, file.Size);
        lock (_cacheLock)
        {
            if (_parsedCache.TryGetValue(file.Path, out var cached) && cached.Stamp == stamp)
            {
                return cached.Rollout;
            }
        }

        var parsed = ParseRollout(file.Path);
        lock (_cacheLock)
        {
            _parsedCache[file.Path] = new ParsedCacheEntry(stamp, parsed);
        }

        return parsed;
    }

    private string? GetProbedSessionId(RolloutFile file)
    {
        var stamp = new FileStamp(file.LastWriteTimeUtc.Ticks, file.Size);
        lock (_cacheLock)
        {
            if (_probeCache.TryGetValue(file.Path, out var cached) && cached.Stamp == stamp)
            {
                return cached.SessionId;
            }
        }

        string? sessionId;
        try
        {
            sessionId = ProbeSessionId(file.Path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        lock (_cacheLock)
        {
            _probeCache[file.Path] = new ProbeCacheEntry(stamp, sessionId);
        }

        return sessionId;
    }

    private static ParsedRollout ParseRollout(string path)
    {
        var events = new List<CodexUsageEvent>();
        var turn = 0;
        string? sessionId = null;
        string? parentSessionId = null;
        DateTimeOffset? forkedAt = null;
        var isSubagent = false;
        string? currentSessionId = null;
        (string SessionId, CodexUsageState State)? previousUsageState = null;
        var model = "codex";

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                1024 * 1024,
                FileOptions.SequentialScan);
            using var reader = new StreamReader(
                stream,
                StrictUtf8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 1024 * 1024);

            while (reader.ReadLine() is { } line)
            {
                if (line.Contains("session_meta", StringComparison.Ordinal)
                    && TryParseSessionMeta(line, out var meta))
                {
                    if (sessionId is null)
                    {
                        sessionId = meta.Id;
                        parentSessionId = meta.ParentId;
                        forkedAt = meta.Date;
                        isSubagent = meta.IsSubagent;
                    }

                    if (meta.Id is { } id && id != currentSessionId)
                    {
                        currentSessionId = id;
                        previousUsageState = null;
                    }
                }

                if (line.Contains("\"model\"", StringComparison.Ordinal)
                    && TryParseModel(line, out var parsedModel))
                {
                    model = parsedModel;
                }

                if (!line.Contains("token_count", StringComparison.Ordinal)
                    || !TryParseTokenLine(line, Path.GetFileName(path), turn, model, out var parsed))
                {
                    continue;
                }

                turn++;
                if (parsed.UsageState is { } state && currentSessionId is { } current)
                {
                    if (previousUsageState is { } previous
                        && previous.SessionId == current
                        && previous.State == state)
                    {
                        continue;
                    }

                    previousUsageState = (current, state);
                }
                else
                {
                    previousUsageState = null;
                }

                events.Add(new CodexUsageEvent(parsed.Entry, parsed.UsageState, currentSessionId));
            }
        }
        catch (IOException)
        {
            return ParsedRollout.Empty(path);
        }
        catch (UnauthorizedAccessException)
        {
            return ParsedRollout.Empty(path);
        }
        catch (DecoderFallbackException)
        {
            return ParsedRollout.Empty(path);
        }

        return new ParsedRollout(
            path,
            sessionId,
            parentSessionId,
            forkedAt,
            isSubagent,
            events);
    }

    private static bool TryParseSessionMeta(string line, out SessionMeta meta)
    {
        meta = default;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (GetString(root, "type") != "session_meta"
                || !root.TryGetProperty("payload", out var payload)
                || payload.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var id = NonEmpty(GetString(payload, "id")) ?? NonEmpty(GetString(payload, "session_id"));
            var parentId = NonEmpty(GetString(payload, "forked_from_id"))
                ?? NonEmpty(GetString(payload, "parent_thread_id"));
            DateTimeOffset? date = TryParseDate(GetString(root, "timestamp"), out var parsedDate)
                ? parsedDate
                : null;
            var sourceHasSubagent = payload.TryGetProperty("source", out var source)
                && source.ValueKind == JsonValueKind.Object
                && source.TryGetProperty("subagent", out _);
            meta = new SessionMeta(
                id,
                parentId,
                date,
                GetString(payload, "thread_source") == "subagent" || sourceHasSubagent);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseModel(string line, out string model)
    {
        model = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("payload", out var payload)
                || payload.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            model = GetString(payload, "model") ?? string.Empty;
            if (model.Length == 0
                && payload.TryGetProperty("turn_context", out var context)
                && context.ValueKind == JsonValueKind.Object)
            {
                model = GetString(context, "model") ?? string.Empty;
            }

            return model.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseTokenLine(
        string line,
        string fileName,
        int turn,
        string model,
        out ParsedToken parsed)
    {
        parsed = default;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("payload", out var payload)
                || payload.ValueKind != JsonValueKind.Object
                || GetString(payload, "type") != "token_count"
                || !payload.TryGetProperty("info", out var info)
                || info.ValueKind != JsonValueKind.Object
                || !info.TryGetProperty("last_token_usage", out var last)
                || last.ValueKind != JsonValueKind.Object
                || !TryParseDate(GetString(root, "timestamp"), out var timestamp))
            {
                return false;
            }

            var inputTotal = SafeLong(last, "input_tokens");
            var cachedInput = SafeLong(last, "cached_input_tokens");
            var output = SafeLong(last, "output_tokens");
            var nonCachedInput = Math.Max(0, inputTotal - cachedInput);
            var entry = new UsageEntry(
                $"codex|{fileName}|{turn}",
                timestamp,
                DateOnly.FromDateTime(timestamp.LocalDateTime),
                model,
                nonCachedInput,
                output,
                0,
                cachedInput);

            CodexUsageState? state = null;
            if (info.TryGetProperty("total_token_usage", out var cumulative)
                && cumulative.ValueKind == JsonValueKind.Object)
            {
                state = new CodexUsageState(
                    ParseVector(cumulative),
                    ParseVector(last));
            }

            parsed = new ParsedToken(entry, state);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static CodexUsageVector ParseVector(JsonElement raw) => new(
        SafeLong(raw, "input_tokens"),
        SafeLong(raw, "cached_input_tokens"),
        SafeLong(raw, "cache_write_input_tokens"),
        SafeLong(raw, "output_tokens"),
        SafeLong(raw, "reasoning_output_tokens"),
        SafeLong(raw, "total_tokens"));

    private static long SafeLong(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        if (value.TryGetInt64(out var integer))
        {
            return Math.Clamp(integer, 0, MaxParsedTokenValue);
        }

        if (!value.TryGetDouble(out var number) || !double.IsFinite(number) || number <= 0)
        {
            return 0;
        }

        return number >= MaxParsedTokenValue ? MaxParsedTokenValue : (long)number;
    }

    private static IReadOnlyList<UsageEntry> ResolveRollouts(
        IEnumerable<ParsedRollout> source,
        IReadOnlySet<string> includedPaths)
    {
        var rollouts = source.ToArray();
        var bySession = rollouts
            .Where(rollout => rollout.SessionId is not null)
            .GroupBy(rollout => rollout.SessionId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(rollout => rollout.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.Ordinal);
        var byPath = rollouts.ToDictionary(
            rollout => rollout.Path,
            StringComparer.OrdinalIgnoreCase);
        var memo = new Dictionary<string, ResolvedRollout>(StringComparer.OrdinalIgnoreCase);

        ResolvedRollout Resolve(ParsedRollout rollout, HashSet<string> visiting)
        {
            if (memo.TryGetValue(rollout.Path, out var cached))
            {
                return cached;
            }

            if (!visiting.Add(rollout.Path))
            {
                return ResolveOwnedEvents(rollout, FallbackReplayCount(rollout));
            }

            try
            {
                (int ReplayCount, IReadOnlyList<ResolvedEvent> History)? bestParentMatch = null;
                if (rollout.ParentSessionId is { } parentId
                    && bySession.TryGetValue(parentId, out var candidates))
                {
                    foreach (var candidate in candidates.Where(candidate => candidate.Path != rollout.Path))
                    {
                        var resolvedParent = Resolve(candidate, visiting);
                        var replayCount = ComparableUsagePrefixCount(rollout.Events, resolvedParent.History);
                        if (replayCount is not > 0)
                        {
                            continue;
                        }

                        if (bestParentMatch is null || replayCount > bestParentMatch.Value.ReplayCount)
                        {
                            bestParentMatch = (replayCount.Value, resolvedParent.History);
                        }
                    }
                }

                ResolvedRollout resolved;
                if (bestParentMatch is { } parentMatch)
                {
                    resolved = ResolveOwnedEvents(
                        rollout,
                        parentMatch.ReplayCount,
                        parentMatch.History.Take(parentMatch.ReplayCount).ToArray());
                }
                else if (rollout.ParentSessionId is not null)
                {
                    resolved = ResolveOwnedEvents(rollout, FallbackReplayCount(rollout));
                }
                else
                {
                    resolved = ResolveOwnedEvents(rollout, 0);
                }

                memo[rollout.Path] = resolved;
                return resolved;
            }
            finally
            {
                visiting.Remove(rollout.Path);
            }
        }

        var entries = new List<UsageEntry>();
        foreach (var path in includedPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (byPath.TryGetValue(path, out var rollout))
            {
                entries.AddRange(Resolve(
                    rollout,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)).OwnedEntries);
            }
        }

        var byId = new Dictionary<string, UsageEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var entry in entries)
        {
            if (byId.TryGetValue(entry.Id, out var existing))
            {
                if (entry.Timestamp < existing.Timestamp)
                {
                    byId[entry.Id] = entry;
                }
            }
            else
            {
                byId[entry.Id] = entry;
                order.Add(entry.Id);
            }
        }

        return order.Select(id => byId[id]).ToArray();
    }

    private static int? ComparableUsagePrefixCount(
        IReadOnlyList<CodexUsageEvent> child,
        IReadOnlyList<ResolvedEvent> parent)
    {
        if (child.Count == 0)
        {
            return 0;
        }

        if (parent.Count == 0)
        {
            return null;
        }

        var count = 0;
        while (count < child.Count && count < parent.Count)
        {
            if (child[count].UsageState is not { } childState
                || parent[count].UsageState is not { } parentState)
            {
                return null;
            }

            if (childState != parentState)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static int FallbackReplayCount(ParsedRollout rollout)
    {
        if (rollout.IsSubagent)
        {
            return 0;
        }

        if (rollout.Events.Count <= 1)
        {
            return rollout.Events.Count;
        }

        var count = 1;
        while (count < rollout.Events.Count)
        {
            var gap = rollout.Events[count].Entry.Timestamp - rollout.Events[count - 1].Entry.Timestamp;
            if (gap >= ForkReplayMaximumGap)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static ResolvedRollout ResolveOwnedEvents(
        ParsedRollout rollout,
        int replayCount,
        IReadOnlyList<ResolvedEvent>? inheritedHistory = null)
    {
        var history = inheritedHistory?.ToList() ?? [];
        var owned = new List<UsageEntry>();
        var epoch = 0;
        CodexUsageVector? previousCumulative = null;
        string? previousOwner = null;

        foreach (var item in rollout.Events.Skip(replayCount))
        {
            var owner = rollout.ParentSessionId is null
                ? item.SessionId ?? rollout.SessionId
                : rollout.SessionId;
            if (owner != previousOwner)
            {
                epoch = 0;
                previousCumulative = null;
                previousOwner = owner;
            }

            if (item.UsageState?.Cumulative is { } cumulative)
            {
                if (previousCumulative is { } previous && cumulative.IsLowerThan(previous))
                {
                    epoch++;
                }

                previousCumulative = cumulative;
            }
            else
            {
                previousCumulative = null;
            }

            var entry = item.Entry;
            if (owner is not null && item.UsageState is { } state)
            {
                entry = entry with
                {
                    Id = $"codex|{owner}|{epoch}|{state.Fingerprint}",
                };
            }

            owned.Add(entry);
            history.Add(new ResolvedEvent(entry, item.UsageState));
        }

        return new ResolvedRollout(history, owned);
    }

    private static ProbeOutcome ProbeLine(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return new ProbeOutcome(ProbeKind.KeepScanning, null);
        }

        string line;
        try
        {
            line = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return new ProbeOutcome(ProbeKind.Invalid, null);
        }

        if (line.Contains("session_meta", StringComparison.Ordinal)
            && TryParseSessionMeta(line, out var meta))
        {
            return new ProbeOutcome(ProbeKind.SessionFound, meta.Id);
        }

        return line.Contains("token_count", StringComparison.Ordinal)
            ? new ProbeOutcome(ProbeKind.Stop, null)
            : new ProbeOutcome(ProbeKind.KeepScanning, null);
    }

    private static bool TryParseDate(string? value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out result);

    private static string? GetString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private readonly record struct FileStamp(long LastWriteTicks, long Size);
    private sealed record ParsedCacheEntry(FileStamp Stamp, ParsedRollout Rollout);
    private sealed record ProbeCacheEntry(FileStamp Stamp, string? SessionId);
    private sealed record RolloutFile(string Path, DateTime LastWriteTimeUtc, long Size);
    private readonly record struct SessionMeta(
        string? Id,
        string? ParentId,
        DateTimeOffset? Date,
        bool IsSubagent);
    private readonly record struct ParsedToken(UsageEntry Entry, CodexUsageState? UsageState);
    private sealed record CodexUsageEvent(
        UsageEntry Entry,
        CodexUsageState? UsageState,
        string? SessionId);
    private sealed record ParsedRollout(
        string Path,
        string? SessionId,
        string? ParentSessionId,
        DateTimeOffset? ForkedAt,
        bool IsSubagent,
        IReadOnlyList<CodexUsageEvent> Events)
    {
        public static ParsedRollout Empty(string path) =>
            new(path, null, null, null, false, []);
    }

    private sealed record ResolvedEvent(UsageEntry Entry, CodexUsageState? UsageState);
    private sealed record ResolvedRollout(
        IReadOnlyList<ResolvedEvent> History,
        IReadOnlyList<UsageEntry> OwnedEntries);
    private readonly record struct CodexUsageVector(
        long Input,
        long CachedInput,
        long CacheWriteInput,
        long Output,
        long ReasoningOutput,
        long Total)
    {
        public string Fingerprint =>
            $"{Input},{CachedInput},{CacheWriteInput},{Output},{ReasoningOutput},{Total}";

        public bool IsLowerThan(CodexUsageVector previous) =>
            Input < previous.Input
            || CachedInput < previous.CachedInput
            || CacheWriteInput < previous.CacheWriteInput
            || Output < previous.Output
            || ReasoningOutput < previous.ReasoningOutput
            || Total < previous.Total;
    }

    private readonly record struct CodexUsageState(
        CodexUsageVector Cumulative,
        CodexUsageVector Last)
    {
        public string Fingerprint => $"{Cumulative.Fingerprint}|{Last.Fingerprint}";
    }

    private enum ProbeKind
    {
        KeepScanning,
        SessionFound,
        Stop,
        Invalid,
    }

    private readonly record struct ProbeOutcome(ProbeKind Kind, string? SessionId);

    private static class CollectionsMarshalHelper
    {
        public static ReadOnlySpan<byte> AsSpan(List<byte> bytes) =>
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes);
    }
}
