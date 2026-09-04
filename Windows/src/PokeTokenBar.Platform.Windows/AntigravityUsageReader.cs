using System.Text;
using PokeTokenBar.Core;

namespace PokeTokenBar.Platform.Windows;

public sealed class AntigravityUsageReader
{
    // Some Antigravity generations contain cumulative transcript payloads. The
    // token metadata is near the front, so a prefix keeps refresh memory bounded.
    private const int MetadataPrefixBytes = 1024 * 1024;

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, DatabaseCache> _cache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<UsageEntry> ReadEntries(
        IEnumerable<string> roots,
        DateTimeOffset modifiedSince,
        CancellationToken cancellationToken = default)
    {
        var combined = new Dictionary<string, UsageEntry>(StringComparer.Ordinal);
        foreach (var databasePath in EnumerateDatabases(roots))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in ReadDatabase(databasePath, modifiedSince, cancellationToken))
            {
                combined.TryAdd(entry.Id, entry);
            }
        }

        return combined.Values.OrderBy(entry => entry.Timestamp).ToArray();
    }

    public static UsageEntry? ParseGeneration(
        string databasePath,
        long rowId,
        byte[] blob,
        DateTimeOffset fallbackTimestamp,
        DateTimeOffset modifiedSince)
    {
        ArgumentNullException.ThrowIfNull(blob);
        var decoded = DecodeGeneration(blob);
        var timestamp = decoded.Timestamp ?? fallbackTimestamp;
        var input = UsageMath.SaturatingAdd(decoded.SystemInput, decoded.FreshInput);
        var output = UsageMath.SaturatingAdd(decoded.Output, decoded.Thinking);
        var total = UsageMath.SaturatingSum(input, output, decoded.CacheRead);
        if (total == 0 || timestamp < modifiedSince)
        {
            return null;
        }

        var canonicalDatabase = Path.GetFullPath(databasePath).ToUpperInvariant();
        var id = string.IsNullOrWhiteSpace(decoded.ResponseId)
            ? $"antigravity|{canonicalDatabase}|{rowId}"
            : $"antigravity|response|{decoded.ResponseId}";
        return new UsageEntry(
            id,
            timestamp,
            DateOnly.FromDateTime(timestamp.LocalDateTime),
            NormalizeModel(decoded.Model),
            input,
            output,
            0,
            decoded.CacheRead);
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

            if (WindowsSqlite.TryOpenReadOnly(databasePath, out var database))
            {
                using (database)
                {
                    if (TryReadOpenDatabase(
                            databasePath,
                            database,
                            cache,
                            cancellationToken,
                            out var directEntries))
                    {
                        return Filter(directEntries, modifiedSince);
                    }
                }
            }

            if (!TryCreateSnapshot(databasePath, out var snapshotDatabase, out var snapshotDirectory))
            {
                return Filter(cache.Entries.Values, modifiedSince);
            }

            try
            {
                if (!WindowsSqlite.TryOpenReadOnly(snapshotDatabase, out var snapshot))
                {
                    return Filter(cache.Entries.Values, modifiedSince);
                }

                using (snapshot)
                {
                    return TryReadOpenDatabase(
                        databasePath,
                        snapshot,
                        cache,
                        cancellationToken,
                        out var snapshotEntries)
                        ? Filter(snapshotEntries, modifiedSince)
                        : Filter(cache.Entries.Values, modifiedSince);
                }
            }
            finally
            {
                TryDeleteSnapshot(snapshotDirectory);
            }
        }
    }

    private static bool TryReadOpenDatabase(
        string sourceDatabasePath,
        WindowsSqlite.Database database,
        DatabaseCache cache,
        CancellationToken cancellationToken,
        out IEnumerable<UsageEntry> entries)
    {
        entries = cache.Entries.Values;
        if (!database.TryScalarInt64("SELECT MAX(idx) FROM gen_metadata", out var maxRowId))
        {
            return false;
        }

        if (cache.HighWaterRowId > maxRowId)
        {
            cache.Entries.Clear();
            cache.HighWaterRowId = -1;
        }

        var fallbackTimestamp = ReadTrajectoryTimestamp(database)
            ?? new DateTimeOffset(File.GetLastWriteTimeUtc(sourceDatabasePath), TimeSpan.Zero);
        var sql = cache.HighWaterRowId < 0
            ? $"SELECT idx, substr(data, 1, {MetadataPrefixBytes}) FROM gen_metadata ORDER BY idx"
            : $"SELECT idx, substr(data, 1, {MetadataPrefixBytes}) FROM gen_metadata WHERE idx >= ?1 ORDER BY idx";
        if (!database.TryPrepare(sql, out var statement))
        {
            return false;
        }

        using (statement)
        {
            if (cache.HighWaterRowId >= 0)
            {
                statement.BindInt64(1, cache.HighWaterRowId);
            }

            while (statement.Step())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rowId = statement.ColumnInt64(0);
                var blob = statement.ColumnBlob(1);
                cache.HighWaterRowId = Math.Max(cache.HighWaterRowId, rowId);
                if (blob is null)
                {
                    continue;
                }

                var entry = ParseGeneration(
                    sourceDatabasePath,
                    rowId,
                    blob,
                    fallbackTimestamp,
                    DateTimeOffset.MinValue);
                if (entry is null)
                {
                    cache.Entries.Remove(rowId);
                }
                else
                {
                    cache.Entries[rowId] = entry;
                }
            }
        }

        cache.HighWaterRowId = Math.Max(cache.HighWaterRowId, maxRowId);
        entries = cache.Entries.Values;
        return true;
    }

    private static bool TryCreateSnapshot(
        string databasePath,
        out string snapshotDatabase,
        out string? snapshotDirectory)
    {
        snapshotDatabase = string.Empty;
        snapshotDirectory = null;
        const long maximumSnapshotBytes = 256L * 1024 * 1024;
        try
        {
            if (new FileInfo(databasePath).Length > maximumSnapshotBytes)
            {
                return false;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            snapshotDirectory = Path.Combine(
                Path.GetTempPath(),
                "PokeTokenBar",
                "AntigravitySnapshots",
                Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(snapshotDirectory);
                snapshotDatabase = Path.Combine(snapshotDirectory, Path.GetFileName(databasePath));
                CopySharedFile(databasePath, snapshotDatabase);
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    var sidecar = databasePath + suffix;
                    if (File.Exists(sidecar))
                    {
                        CopySharedFile(sidecar, snapshotDatabase + suffix);
                    }
                }

                return true;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            TryDeleteSnapshot(snapshotDirectory);
            snapshotDirectory = null;
        }

        snapshotDatabase = string.Empty;
        return false;
    }

    private static void CopySharedFile(string source, string destination)
    {
        using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        input.CopyTo(output);
    }

    private static void TryDeleteSnapshot(string? snapshotDirectory)
    {
        if (string.IsNullOrWhiteSpace(snapshotDirectory))
        {
            return;
        }

        var snapshotRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "PokeTokenBar",
            "AntigravitySnapshots"));
        var candidate = Path.GetFullPath(snapshotDirectory);
        if (!candidate.StartsWith(
                snapshotRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Directory.Delete(candidate, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static DateTimeOffset? ReadTrajectoryTimestamp(WindowsSqlite.Database database)
    {
        if (!database.TryPrepare(
                $"SELECT substr(data, 1, {MetadataPrefixBytes}) FROM trajectory_metadata_blob LIMIT 1",
                out var statement))
        {
            return null;
        }

        using (statement)
        {
            if (!statement.Step() || statement.ColumnBlob(0) is not { } blob)
            {
                return null;
            }

            var reader = new ProtobufReader(blob);
            while (reader.TryRead(out var field))
            {
                if (field.Number == 2 && field.WireType == 2)
                {
                    return DecodeTimestamp(field.Bytes);
                }
            }
        }

        return null;
    }

    private static DecodedGeneration DecodeGeneration(ReadOnlySpan<byte> blob)
    {
        var decoded = new DecodedGeneration();
        var root = new ProtobufReader(blob);
        while (root.TryRead(out var rootField))
        {
            if (rootField.Number != 1 || rootField.WireType != 2)
            {
                continue;
            }

            var chatModel = new ProtobufReader(rootField.Bytes);
            while (chatModel.TryRead(out var field))
            {
                switch (field.Number, field.WireType)
                {
                    case (4, 2):
                        DecodeUsage(field.Bytes, decoded);
                        break;
                    case (9, 2):
                        decoded.Timestamp = DecodeGenerationTimestamp(field.Bytes);
                        break;
                    case (19, 2):
                        decoded.Model = Utf8(field.Bytes);
                        break;
                }
            }
        }

        return decoded;
    }

    private static void DecodeUsage(ReadOnlySpan<byte> bytes, DecodedGeneration decoded)
    {
        var reader = new ProtobufReader(bytes);
        while (reader.TryRead(out var field))
        {
            if (field.WireType == 0)
            {
                var value = Clamp(field.Varint);
                switch (field.Number)
                {
                    case 1:
                        decoded.SystemInput = value;
                        break;
                    case 2:
                        decoded.FreshInput = value;
                        break;
                    case 5:
                        decoded.CacheRead = value;
                        break;
                    case 9:
                        decoded.Output = value;
                        break;
                    case 10:
                        decoded.Thinking = value;
                        break;
                }
            }
            else if (field.Number == 11 && field.WireType == 2)
            {
                decoded.ResponseId = Utf8(field.Bytes);
            }
        }
    }

    private static DateTimeOffset? DecodeGenerationTimestamp(ReadOnlySpan<byte> bytes)
    {
        var reader = new ProtobufReader(bytes);
        while (reader.TryRead(out var field))
        {
            if (field.Number == 4 && field.WireType == 2)
            {
                return DecodeTimestamp(field.Bytes);
            }
        }

        return null;
    }

    private static DateTimeOffset? DecodeTimestamp(ReadOnlySpan<byte> bytes)
    {
        ulong? seconds = null;
        ulong nanos = 0;
        var reader = new ProtobufReader(bytes);
        while (reader.TryRead(out var field))
        {
            if (field.WireType != 0)
            {
                continue;
            }

            if (field.Number == 1)
            {
                seconds = field.Varint;
            }
            else if (field.Number == 2)
            {
                nanos = field.Varint;
            }
        }

        if (seconds is null || seconds > long.MaxValue || nanos > 999_999_999)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(checked(
                (long)seconds.Value * 1000 + (long)(nanos / 1_000_000)));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateDatabases(IEnumerable<string> roots)
    {
        var databases = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawRoot in roots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            var root = Path.GetFullPath(rawRoot);
            if (string.Equals(Path.GetExtension(root), ".db", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(root))
                {
                    databases.Add(root);
                }

                continue;
            }

            var conversations = Path.Combine(root, "conversations");
            if (!Directory.Exists(conversations))
            {
                continue;
            }

            try
            {
                foreach (var path in Directory.EnumerateFiles(
                             conversations,
                             "*.db",
                             new EnumerationOptions
                             {
                                 RecurseSubdirectories = true,
                                 IgnoreInaccessible = true,
                                 AttributesToSkip = FileAttributes.ReparsePoint,
                             }))
                {
                    databases.Add(Path.GetFullPath(path));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return databases;
    }

    private static IReadOnlyList<UsageEntry> Filter(
        IEnumerable<UsageEntry> entries,
        DateTimeOffset modifiedSince) =>
        entries
            .Where(entry => entry.Timestamp >= modifiedSince)
            .OrderBy(entry => entry.Timestamp)
            .ToArray();

    private static string NormalizeModel(string? model) => model?.ToLowerInvariant() switch
    {
        "model_placeholder_m36" or "model_placeholder_m37" or "model_placeholder_m16" =>
            "gemini-3.1-pro",
        "model_placeholder_m18" or "model_placeholder_m84" or "model_placeholder_m47" =>
            "gemini-3-flash-preview",
        "gemini-pro-default" or "gemini-pro-agent" => "gemini-3.1-pro",
        "gemini-3.1-pro-high" or "gemini-3.1-pro-low" => "gemini-3.1-pro",
        null or "" => "antigravity",
        _ => model!,
    };

    private static string? Utf8(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return null;
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static long Clamp(ulong value) =>
        value >= (ulong)CodexUsageReader.MaxParsedTokenValue
            ? CodexUsageReader.MaxParsedTokenValue
            : (long)value;

    private sealed class DatabaseCache
    {
        public long HighWaterRowId { get; set; } = -1;

        public Dictionary<long, UsageEntry> Entries { get; } = [];
    }

    private sealed class DecodedGeneration
    {
        public string? Model { get; set; }

        public DateTimeOffset? Timestamp { get; set; }

        public long SystemInput { get; set; }

        public long FreshInput { get; set; }

        public long CacheRead { get; set; }

        public long Output { get; set; }

        public long Thinking { get; set; }

        public string? ResponseId { get; set; }
    }

    private ref struct ProtobufReader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _offset;

        public ProtobufReader(ReadOnlySpan<byte> bytes)
        {
            _bytes = bytes;
            _offset = 0;
        }

        public bool TryRead(out ProtobufField field)
        {
            field = default;
            if (!TryReadVarint(out var key) || key >> 3 is 0 or > int.MaxValue)
            {
                return false;
            }

            var number = (int)(key >> 3);
            var wireType = (int)(key & 0x07);
            switch (wireType)
            {
                case 0:
                    if (!TryReadVarint(out var value))
                    {
                        return false;
                    }

                    field = new ProtobufField(number, wireType, value, default);
                    return true;
                case 1:
                    return TrySkipFixed(number, wireType, 8, out field);
                case 2:
                    if (!TryReadVarint(out var declaredLength) || declaredLength > int.MaxValue)
                    {
                        return false;
                    }

                    var available = Math.Min((int)declaredLength, _bytes.Length - _offset);
                    field = new ProtobufField(
                        number,
                        wireType,
                        0,
                        _bytes.Slice(_offset, available));
                    _offset += available;
                    return true;
                case 5:
                    return TrySkipFixed(number, wireType, 4, out field);
                default:
                    return false;
            }
        }

        private bool TryReadVarint(out ulong value)
        {
            value = 0;
            var shift = 0;
            while (_offset < _bytes.Length && shift < 64)
            {
                var current = _bytes[_offset++];
                value |= (ulong)(current & 0x7f) << shift;
                if ((current & 0x80) == 0)
                {
                    return true;
                }

                shift += 7;
            }

            return false;
        }

        private bool TrySkipFixed(
            int number,
            int wireType,
            int length,
            out ProtobufField field)
        {
            field = default;
            if (_bytes.Length - _offset < length)
            {
                _offset = _bytes.Length;
                return false;
            }

            _offset += length;
            field = new ProtobufField(number, wireType, 0, default);
            return true;
        }
    }

    private readonly ref struct ProtobufField(
        int number,
        int wireType,
        ulong varint,
        ReadOnlySpan<byte> bytes)
    {
        public int Number { get; } = number;

        public int WireType { get; } = wireType;

        public ulong Varint { get; } = varint;

        public ReadOnlySpan<byte> Bytes { get; } = bytes;
    }
}
