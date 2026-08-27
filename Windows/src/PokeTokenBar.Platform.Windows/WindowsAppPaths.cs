namespace PokeTokenBar.Platform.Windows;

public sealed record WindowsAppPaths(
    string DataDirectory,
    string CacheDirectory,
    string LogsDirectory)
{
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
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                throw new InvalidOperationException("Windows LocalApplicationData is unavailable.");
            }

            data = Path.Combine(localAppData, "PokeTokenBar");
        }

        return new WindowsAppPaths(
            data,
            Path.Combine(data, "Cache"),
            Path.Combine(data, "Logs"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
