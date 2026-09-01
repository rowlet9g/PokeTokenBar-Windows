namespace PokeTokenBar.Platform.Windows;

public static class WindowsCopilotPaths
{
    public static IReadOnlyList<string> CreateDefaultRoots(
        string? userProfile = null,
        string? overrideValue = null)
    {
        var configured = overrideValue ?? Environment.GetEnvironmentVariable("COPILOT_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var home = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException("Windows user profile directory is unavailable.");
        }

        return [Path.Combine(home, ".copilot")];
    }
}
