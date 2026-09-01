using PokeTokenBar.Core;

namespace PokeTokenBar.Core.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void Missing_file_uses_safe_defaults()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = new AppSettingsStore(Path.Combine(temporary.Path, "settings.json"));

        Assert.Equal(2, store.Current.RefreshIntervalMinutes);
        Assert.True(store.Current.NotificationsEnabled);
        Assert.True(store.Current.AlwaysOnTop);
        Assert.False(store.Current.LaunchAtLogin);
    }

    [Fact]
    public void Saved_settings_are_restored()
    {
        using var temporary = TemporaryDirectory.Create();
        var path = Path.Combine(temporary.Path, "settings.json");
        var store = new AppSettingsStore(path);
        store.Save(new AppSettings
        {
            RefreshIntervalMinutes = 10,
            NotificationsEnabled = false,
            AlwaysOnTop = false,
            LaunchAtLogin = true,
        });

        var restored = new AppSettingsStore(path).Current;

        Assert.Equal(10, restored.RefreshIntervalMinutes);
        Assert.False(restored.NotificationsEnabled);
        Assert.False(restored.AlwaysOnTop);
        Assert.True(restored.LaunchAtLogin);
    }

    [Theory]
    [InlineData(-10, 1)]
    [InlineData(0, 1)]
    [InlineData(90, 60)]
    public void Refresh_interval_is_clamped_at_the_trust_boundary(int raw, int expected)
    {
        using var temporary = TemporaryDirectory.Create();
        var path = Path.Combine(temporary.Path, "settings.json");
        File.WriteAllText(path, $$"""{"refreshIntervalMinutes":{{raw}}}""");

        var store = new AppSettingsStore(path);

        Assert.Equal(expected, store.Current.RefreshIntervalMinutes);
    }

    [Fact]
    public void Invalid_json_falls_back_without_overwriting_the_source()
    {
        using var temporary = TemporaryDirectory.Create();
        var path = Path.Combine(temporary.Path, "settings.json");
        File.WriteAllText(path, "{ invalid");

        var store = new AppSettingsStore(path);

        Assert.NotNull(store.LastError);
        Assert.Equal("{ invalid", File.ReadAllText(path));
        Assert.Equal(2, store.Current.RefreshIntervalMinutes);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PokeTokenBar-settings-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
