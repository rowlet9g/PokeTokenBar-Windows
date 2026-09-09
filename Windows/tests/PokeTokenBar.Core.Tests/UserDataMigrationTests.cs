using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class UserDataMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ptb-migration-test-" + Guid.NewGuid().ToString("N"));
    private string Legacy => Path.Combine(_root, "old");
    private string Destination => Path.Combine(_root, "new");

    public UserDataMigrationTests()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "companion-state.json"), "original-progress");
        Directory.CreateDirectory(Path.Combine(Legacy, "Cache"));
        File.WriteAllText(Path.Combine(Legacy, "Cache", "sample.json"), "cache");
    }

    [Fact]
    public void MigrationPreservesEveryFileAndNeverReimportsStaleProgress()
    {
        UserDataMigration.EnsureMigrated(Legacy, Destination);
        Assert.Equal("original-progress", File.ReadAllText(Path.Combine(Destination, "companion-state.json")));
        Assert.Equal("original-progress", File.ReadAllText(Path.Combine(Legacy, "companion-state.json")));
        Assert.Equal("cache", File.ReadAllText(Path.Combine(Destination, "Cache", "sample.json")));
        File.WriteAllText(Path.Combine(Destination, "companion-state.json"), "new-evolution");
        UserDataMigration.EnsureMigrated(Legacy, Destination);
        Assert.Equal("new-evolution", File.ReadAllText(Path.Combine(Destination, "companion-state.json")));
    }

    [Fact]
    public void RedirectedLegacyDoesNotBecomeTheAuthoritativeSave()
    {
        Assert.Throws<IOException>(() => UserDataMigration.EnsureMigrated(Legacy, Destination,
            _ => Path.Combine(_root, "package-shadow")));
        Assert.False(Directory.Exists(Destination));
        Assert.True(File.Exists(Path.Combine(Legacy, "companion-state.json")));
    }

    [Fact]
    public void InterruptedCopyLeavesSourceAndNoPartialDestinationAndCanRetry()
    {
        using (var locked = new FileStream(Path.Combine(Legacy, "companion-state.json"),
                   FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() => UserDataMigration.EnsureMigrated(Legacy, Destination));
        }
        Assert.False(Directory.Exists(Destination));
        Assert.Empty(Directory.GetDirectories(_root, "new.migration-*"));
        UserDataMigration.EnsureMigrated(Legacy, Destination);
        Assert.Equal("original-progress", File.ReadAllText(Path.Combine(Destination, "companion-state.json")));
    }

    [Fact]
    public void ExistingSharedSaveDoesNotReadRedirectedLegacy()
    {
        Directory.CreateDirectory(Destination);
        File.WriteAllText(Path.Combine(Destination, "companion-state.json"), "shared-progress");
        UserDataMigration.EnsureMigrated(Legacy, Destination, path =>
            path == Destination ? path : throw new InvalidOperationException("Legacy must not be inspected"));
        Assert.Equal("shared-progress", File.ReadAllText(Path.Combine(Destination, "companion-state.json")));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
