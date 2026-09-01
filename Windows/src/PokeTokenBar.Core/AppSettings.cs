using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokeTokenBar.Core;

public sealed record AppSettings
{
    public const int DefaultRefreshIntervalMinutes = 2;

    public int RefreshIntervalMinutes { get; init; } = DefaultRefreshIntervalMinutes;

    public bool NotificationsEnabled { get; init; } = true;

    public bool AlwaysOnTop { get; init; } = true;

    public bool LaunchAtLogin { get; init; }

    public AppSettings Normalize() => this with
    {
        RefreshIntervalMinutes = Math.Clamp(RefreshIntervalMinutes, 1, 60),
    };
}

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _filePath;

    public AppSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public string? LastError { get; private set; }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_filePath}.tmp-{Guid.NewGuid():N}";

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                JsonSerializer.Serialize(stream, normalized, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
            Current = normalized;
            LastError = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LastError = error.Message;
            TryDelete(temporaryPath);
            throw;
        }
    }

    private AppSettings Load()
    {
        try
        {
            using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return (JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions) ?? new AppSettings())
                .Normalize();
        }
        catch (FileNotFoundException)
        {
            return new AppSettings();
        }
        catch (DirectoryNotFoundException)
        {
            return new AppSettings();
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            LastError = error.Message;
            return new AppSettings();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The original settings file remains authoritative.
        }
    }
}
