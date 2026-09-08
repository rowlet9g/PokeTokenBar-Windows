namespace PokeTokenBar.Platform.Windows;

public static class WindowsLocalToolPaths
{
    public static readonly string[] ProviderIds = ["claude_code", "opencode", "hermes", "grok", "kiro", "pi", "omp"];

    public static IReadOnlyList<string> CreateRoots(string provider, string? home = null,
        string? appData = null, Func<string, string?>? environment = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        appData ??= Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        environment ??= Environment.GetEnvironmentVariable;
        string Home(string path) => Path.Combine(home, path.Replace('/', Path.DirectorySeparatorChar));
        var roots = new List<string>();
        void AddOverride(string variable, string suffix = "")
        {
            var value = environment(variable);
            if (string.IsNullOrWhiteSpace(value)) return;
            foreach (var path in value.Split(Path.PathSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                roots.Add(Path.Combine(path, suffix));
        }
        switch (provider)
        {
            case "claude_code":
                roots.AddRange([Home(".claude/projects"), Home(".config/claude/projects")]);
                AddOverride("CLAUDE_CONFIG_DIR", "projects");
                // Desktop embedded Claude Code sessions use the same JSONL envelope.
                foreach (var store in new[] { "local-agent-mode-sessions", "claude-code-sessions" })
                {
                    var directory = Path.Combine(appData, "Claude", store);
                    if (!Directory.Exists(directory)) continue;
                    roots.AddRange(Directory.EnumerateDirectories(directory, "projects", new EnumerationOptions
                    {
                        RecurseSubdirectories = true, MaxRecursionDepth = 7, IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint,
                    }).Where(path => Path.GetFileName(Path.GetDirectoryName(path)) == ".claude"));
                }
                break;
            case "opencode":
                roots.Add(Home(".local/share/opencode"));
                var xdg = environment("XDG_DATA_HOME");
                if (!string.IsNullOrWhiteSpace(xdg)) roots.Add(Path.Combine(xdg, "opencode"));
                AddOverride("OPENCODE_DATA_DIR");
                break;
            case "hermes": roots.Add(Home(".hermes")); AddOverride("HERMES_HOME"); break;
            case "grok": roots.Add(Home(".grok/sessions")); AddOverride("GROK_HOME", "sessions"); break;
            case "pi":
                roots.Add(Home(".pi/agent/sessions")); AddOverride("PI_CODING_AGENT_DIR", "sessions");
                AddOverride("PI_CODING_AGENT_SESSION_DIR"); break;
            case "omp": roots.Add(Home(".omp/agent/sessions")); AddOverride("OMP_CODING_AGENT_DIR", "sessions"); break;
            case "kiro":
                roots.AddRange([Home(".kiro/sessions"), Home(".local/share/kiro-cli"), Path.Combine(appData, "kiro-cli")]);
                AddOverride("KIRO_HOME", "sessions"); AddOverride("KIRO_DATA_DIR");
                break;
            default: throw new ArgumentOutOfRangeException(nameof(provider));
        }
        // Optional direct scan roots also support logs exported from WSL or another machine.
        AddOverride("PTB_" + provider.ToUpperInvariant() + "_ROOTS");
        return roots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
