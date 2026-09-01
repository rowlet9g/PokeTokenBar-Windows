namespace PokeTokenBar.Platform.Windows;

public static class WindowsCursorPaths
{
    public static IReadOnlyList<string> CreateDefaultRoots(
        string? appData = null,
        string? overrideValue = null)
    {
        var configured = overrideValue ?? Environment.GetEnvironmentVariable("CURSOR_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var roaming = appData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(roaming))
        {
            throw new InvalidOperationException("Windows roaming AppData directory is unavailable.");
        }

        return
        [
            Path.Combine(roaming, "Cursor", "User", "globalStorage"),
            Path.Combine(roaming, "Cursor Nightly", "User", "globalStorage"),
        ];
    }
}
