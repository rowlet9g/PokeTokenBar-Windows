namespace PokeTokenBar.Platform.Windows;

public static class WindowsAntigravityPaths
{
    private static readonly string[] DefaultRootNames =
    [
        "antigravity",
        "antigravity-cli",
        "antigravity-ide",
        "antigravity-backup",
    ];

    public static IReadOnlyList<string> CreateDefaultRoots(
        string? userProfile = null,
        string? overrideValue = null)
    {
        var configured = overrideValue ?? Environment.GetEnvironmentVariable("ANTIGRAVITY_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured
                .Split(
                    [Path.PathSeparator, ','],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var home = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException("Windows user profile directory is unavailable.");
        }

        return DefaultRootNames
            .Select(name => Path.Combine(home, ".gemini", name))
            .ToArray();
    }
}
