using PokeTokenBar.Core;

namespace PokeTokenBar.Core.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void Missing_file_uses_safe_defaults()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = new AppSettingsStore(Path.Combine(temporary.Path, "settings.json"));

        Assert.Equal(30, store.Current.RefreshIntervalSeconds);
        Assert.True(store.Current.NotificationsEnabled);
        Assert.True(store.Current.AlwaysOnTop);
        Assert.False(store.Current.LaunchAtLogin);
        Assert.False(store.Current.FloatingPetEnabled);
        Assert.Equal(96, store.Current.FloatingPetSize);
    }

    [Fact]
    public void Saved_settings_are_restored()
    {
        using var temporary = TemporaryDirectory.Create();
        var path = Path.Combine(temporary.Path, "settings.json");
        var store = new AppSettingsStore(path);
        store.Save(new AppSettings
        {
            RefreshIntervalSeconds = 120,
            NotificationsEnabled = false,
            AlwaysOnTop = false,
            LaunchAtLogin = true,
            FloatingPetEnabled = true,
            FloatingPetSize = 128,
            FloatingPetLeft = 321.5,
            FloatingPetTop = 123.5,
        });

        var restored = new AppSettingsStore(path).Current;

        Assert.Equal(120, restored.RefreshIntervalSeconds);
        Assert.False(restored.NotificationsEnabled);
        Assert.False(restored.AlwaysOnTop);
        Assert.True(restored.LaunchAtLogin);
        Assert.True(restored.FloatingPetEnabled);
        Assert.Equal(128, restored.FloatingPetSize);
        Assert.Equal(321.5, restored.FloatingPetLeft);
        Assert.Equal(123.5, restored.FloatingPetTop);
    }

    [Theory]
    [InlineData(-10, 30)]
    [InlineData(0, 30)]
    [InlineData(10, 30)]
    [InlineData(7200, 3600)]
    public void Refresh_interval_is_clamped_at_the_trust_boundary(int raw, int expected)
    {
        using var temporary = TemporaryDirectory.Create();
        var path = Path.Combine(temporary.Path, "settings.json");
        File.WriteAllText(path, $$"""{"refreshIntervalSeconds":{{raw}}}""");

        var store = new AppSettingsStore(path);

        Assert.Equal(expected, store.Current.RefreshIntervalSeconds);
    }

    [Fact]
    public void Legacy_minute_interval_moves_to_the_new_thirty_second_default()
    {
        using var temporary = TemporaryDirectory.Create();
        var path = Path.Combine(temporary.Path, "settings.json");
        File.WriteAllText(path, """{"refreshIntervalMinutes":10}""");

        var store = new AppSettingsStore(path);

        Assert.Equal(30, store.Current.RefreshIntervalSeconds);
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
        Assert.Equal(30, store.Current.RefreshIntervalSeconds);
    }

    [Theory]
    [InlineData(1, 64)]
    [InlineData(999, 160)]
    public void Floating_pet_size_is_clamped(int raw, int expected)
    {
        using var temporary = TemporaryDirectory.Create();
        var path = Path.Combine(temporary.Path, "settings.json");
        File.WriteAllText(path, $$"""{"floatingPetSize":{{raw}}}""");

        Assert.Equal(expected, new AppSettingsStore(path).Current.FloatingPetSize);
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
