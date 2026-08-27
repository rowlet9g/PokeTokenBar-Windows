namespace PokeTokenBar.Platform.Windows;

public static class WindowsCodexPaths
{
    public static IReadOnlyList<string> CreateDefaultRoots(string? userProfile = null)
    {
        var home = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException("Windows user profile directory is unavailable.");
        }

        return
        [
            Path.Combine(home, ".codex", "sessions"),
            Path.Combine(home, ".codex", "archived_sessions"),
        ];
    }
}
