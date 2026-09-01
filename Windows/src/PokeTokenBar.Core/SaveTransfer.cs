using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokeTokenBar.Core;

public sealed record SaveEnvelope(
    string Format,
    int Schema,
    string AppVersion,
    DateTimeOffset ExportedAt,
    string SourceDevice,
    CompanionState State);

public sealed record SaveSummary(int DexCount, long LifetimeTokens)
{
    public static SaveSummary From(CompanionState state) =>
        new(state.Dex?.Count ?? 0, Math.Max(0, state.UsedSinceInstall));
}

public sealed class SaveTransferException : Exception
{
    public SaveTransferException(string message) : base(message)
    {
    }
}

public static class SaveTransfer
{
    public const string FormatId = "poketokenbar.save";
    public const int SchemaVersion = 1;
    public const long MaxFileBytes = 8 * 1024 * 1024;
    public const int BackupsToKeep = 5;
    public const string BackupFilePrefix = "companion-state.pre-import-";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string SuggestedFileName(DateTimeOffset now) =>
        $"PokeTokenBar-Save-{now:yyyy-MM-dd}.json";

    public static byte[] Encode(
        CompanionState state,
        string appVersion,
        string deviceName,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        var envelope = new SaveEnvelope(
            FormatId,
            SchemaVersion,
            appVersion,
            now,
            deviceName,
            state);
        return JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
    }

    public static SaveEnvelope Decode(byte[] data)
    {
        if (data.Length > MaxFileBytes)
        {
            throw new SaveTransferException(
                $"세이브 파일이 너무 큽니다 ({data.Length:N0} / {MaxFileBytes:N0} bytes)." );
        }

        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (!root.TryGetProperty("format", out var format)
                || !string.Equals(format.GetString(), FormatId, StringComparison.Ordinal)
                || !root.TryGetProperty("schema", out var schemaElement)
                || !schemaElement.TryGetInt32(out var schema))
            {
                throw new SaveTransferException("PokeTokenBar 세이브 파일이 아닙니다.");
            }

            if (schema > SchemaVersion)
            {
                throw new SaveTransferException(
                    $"더 새로운 세이브 형식입니다 (파일 {schema}, 지원 {SchemaVersion}). 앱을 업데이트하십시오.");
            }

            var envelope = JsonSerializer.Deserialize<SaveEnvelope>(data, JsonOptions)
                ?? throw new SaveTransferException("세이브 파일의 내용이 비어 있습니다.");
            if (envelope.State is null)
            {
                throw new SaveTransferException("세이브 파일에 진행 상태가 없습니다.");
            }

            CompanionStore.SanitizeImportedState(envelope.State);
            return envelope with { State = envelope.State };
        }
        catch (SaveTransferException)
        {
            throw;
        }
        catch (JsonException error)
        {
            throw new SaveTransferException($"세이브 파일이 손상되었습니다: {error.Message}");
        }
    }

    public static byte[] ReadFile(string filePath)
    {
        var length = new FileInfo(filePath).Length;
        if (length > MaxFileBytes)
        {
            throw new SaveTransferException(
                $"세이브 파일이 너무 큽니다 ({length:N0} / {MaxFileBytes:N0} bytes)." );
        }

        return File.ReadAllBytes(filePath);
    }

    internal static CompanionState CloneState(CompanionState state)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        return JsonSerializer.Deserialize<CompanionState>(bytes, JsonOptions)
            ?? throw new InvalidOperationException("Companion state cloning failed.");
    }

    internal static void WriteStateFile(string filePath, CompanionState state)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath))
            ?? throw new InvalidOperationException("State path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{filePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, state, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
