using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class WindowsSshHostDiscoveryTests
{
    [Fact]
    public void ParseConfig_ReturnsConcreteHostAliasesInFileOrder()
    {
        const string config = """
            Host devbox-a render-node # remote Codex machines
              HostName 192.0.2.10
            host github-rowlet9g
              HostName github.com
            Host *
              ServerAliveInterval 30
            Host dev-* !blocked invalid/name
            HOST DEVBOX-A
            """;

        var aliases = WindowsSshHostDiscovery.ParseConfig(config);

        Assert.Equal(["devbox-a", "render-node", "github-rowlet9g"], aliases);
    }

    [Fact]
    public void Discover_ReturnsEmptyWhenSshConfigDoesNotExist()
    {
        var profile = Path.Combine(Path.GetTempPath(), $"ptb-no-ssh-{Guid.NewGuid():N}");

        var aliases = WindowsSshHostDiscovery.Discover(profile);

        Assert.Empty(aliases);
    }
}
