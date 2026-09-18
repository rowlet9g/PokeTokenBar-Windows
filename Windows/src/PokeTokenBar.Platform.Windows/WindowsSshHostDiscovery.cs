using PokeTokenBar.Core;

namespace PokeTokenBar.Platform.Windows;

public static class WindowsSshHostDiscovery
{
    public static IReadOnlyList<string> Discover(string? userProfile = null)
    {
        var profile = string.IsNullOrWhiteSpace(userProfile)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfile;
        if (string.IsNullOrWhiteSpace(profile))
        {
            return [];
        }

        var configPath = Path.Combine(profile, ".ssh", "config");
        try
        {
            return File.Exists(configPath)
                ? ParseConfig(File.ReadAllText(configPath))
                : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal static IReadOnlyList<string> ParseConfig(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var aliases = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Split('#', 2)[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split((char[]?)null, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !string.Equals(parts[0], "Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var alias in parts.Skip(1))
            {
                var normalized = AppSettings.ParseRemoteCodexSshHosts(alias);
                if (normalized.Count == 1
                    && string.Equals(normalized[0], alias, StringComparison.Ordinal)
                    && seen.Add(alias))
                {
                    aliases.Add(alias);
                }
            }
        }

        return aliases;
    }
}
