namespace PokeTokenBar.Platform.Windows;

public sealed record WindowsAppPaths(
    string DataDirectory,
    string CacheDirectory,
    string LogsDirectory)
{
    public string? LegacyDataDirectory { get; init; }

    public static WindowsAppPaths CreateDefault(string? overrideDirectory = null)
    {
        var configured = overrideDirectory ?? Environment.GetEnvironmentVariable("PTB_DATA_DIR");
        string data;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            data = Path.GetFullPath(configured);
        }
        else
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(profile))
            {
                throw new InvalidOperationException("Windows UserProfile is unavailable.");
            }

            // LocalAppData can be redirected by a packaged parent process (e.g. Codex).
            // User-owned progress must have one location regardless of the launcher.
            data = Path.Combine(profile, ".poketokenbar");
        }

        return new WindowsAppPaths(
            data,
            Path.Combine(data, "Cache"),
            Path.Combine(data, "Logs"))
        {
            LegacyDataDirectory = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokeTokenBar")
                : null,
        };
    }

    public void EnsureDirectories()
    {
        if (LegacyDataDirectory is { } legacy)
        {
            UserDataMigration.EnsureMigrated(legacy, DataDirectory);
        }
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
