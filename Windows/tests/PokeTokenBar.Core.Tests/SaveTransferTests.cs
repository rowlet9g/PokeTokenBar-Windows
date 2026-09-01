using System.Text;

namespace PokeTokenBar.Core.Tests;

public sealed class SaveTransferTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 12, 34, 56, TimeSpan.FromHours(9));

    [Fact]
    public void Versioned_envelope_round_trips_progress()
    {
        var original = ProgressState();

        var envelope = SaveTransfer.Decode(
            SaveTransfer.Encode(original, "1.2.3", "TEST-PC", Now));

        Assert.Equal(SaveTransfer.FormatId, envelope.Format);
        Assert.Equal("TEST-PC", envelope.SourceDevice);
        Assert.Equal(8_000_000_000, envelope.State.UsedSinceInstall);
        Assert.Equal(3_500_000_000, envelope.State.SpentTokens);
        Assert.Single(envelope.State.Dex);
        Assert.Equal(2, envelope.State.Inventory["rareCandy"]);
    }

    [Fact]
    public void Foreign_json_is_rejected()
    {
        var error = Assert.Throws<SaveTransferException>(() =>
            SaveTransfer.Decode(Encoding.UTF8.GetBytes("{\"some\":\"other app\"}")));

        Assert.Contains("아닙니다", error.Message);
    }

    [Fact]
    public void Newer_schema_has_a_specific_error()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "{\"format\":\"poketokenbar.save\",\"schema\":99,\"state\":{}}");

        var error = Assert.Throws<SaveTransferException>(() => SaveTransfer.Decode(bytes));

        Assert.Contains("더 새로운", error.Message);
    }

    [Fact]
    public void Oversized_input_is_rejected_before_parsing()
    {
        var bytes = new byte[SaveTransfer.MaxFileBytes + 1];

        var error = Assert.Throws<SaveTransferException>(() => SaveTransfer.Decode(bytes));

        Assert.Contains("너무 큽니다", error.Message);
    }

    [Fact]
    public void Imported_numeric_values_are_sanitized()
    {
        var state = ProgressState();
        state.UsedSinceInstall = -1;
        state.SpentTokens = long.MaxValue;
        state.EggUsage = long.MaxValue;
        state.Inventory["rareCandy"] = int.MaxValue;

        var decoded = SaveTransfer.Decode(
            SaveTransfer.Encode(state, "1", "PC", Now)).State;

        Assert.Equal(0, decoded.UsedSinceInstall);
        Assert.Equal(1_000_000_000_000_000, decoded.SpentTokens);
        Assert.Equal(1_000_000_000_000_000, decoded.EggUsage);
        Assert.Equal(1_000_000, decoded.Inventory["rareCandy"]);
    }

    [Fact]
    public async Task Import_rebases_local_ledger_and_writes_recovery_backup()
    {
        using var temporary = TemporaryDirectory.Create();
        var statePath = Path.Combine(temporary.Path, "companion-state.json");
        var store = new CompanionStore(statePath);
        store.Update(new Dictionary<string, long> { ["codex"] = 100 }, new DateOnly(2026, 9, 1), true);
        store.Update(new Dictionary<string, long> { ["codex"] = 300 }, new DateOnly(2026, 9, 1), true);

        await store.ImportStateAsync(
            ProgressState(),
            new Dictionary<string, long> { ["codex"] = 700 },
            new DateOnly(2026, 9, 1),
            hasUsageData: true,
            Now);

        Assert.Equal(8_000_000_000, store.UsedSinceInstall);
        Assert.Equal(700, store.ClaimedTodayTokensByProvider?["codex"]);
        Assert.Single(Directory.GetFiles(
            temporary.Path,
            $"{SaveTransfer.BackupFilePrefix}*.json"));

        var reloaded = new CompanionStore(statePath);
        Assert.Equal(8_000_000_000, reloaded.UsedSinceInstall);
        Assert.Equal(1, reloaded.DexCount);
    }

    [Fact]
    public async Task Import_without_current_usage_defers_baseline()
    {
        using var temporary = TemporaryDirectory.Create();
        var store = new CompanionStore(Path.Combine(temporary.Path, "companion-state.json"));

        await store.ImportStateAsync(
            ProgressState(),
            new Dictionary<string, long>(),
            new DateOnly(2026, 9, 1),
            hasUsageData: false,
            Now);

        Assert.False(store.InstallBaselineSet);
        Assert.Null(store.ClaimedTodayTokensByProvider);
        Assert.Equal(string.Empty, store.LastDate);
    }

    private static CompanionState ProgressState() => new()
    {
        InstallBaselineSet = true,
        UsedSinceInstall = 8_000_000_000,
        SpentTokens = 3_500_000_000,
        EggUsage = 123,
        LastDate = "2026-08-31",
        ClaimedTodayTokensByProvider = new Dictionary<string, long> { ["codex"] = 500 },
        Inventory = new Dictionary<string, int> { ["rareCandy"] = 2 },
        Dex =
        [
            new PokemonDexEntry
            {
                BaseId = 1,
                FinalId = 3,
                ChainOrder = [1, 2, 3],
                Names = new Dictionary<int, string> { [1] = "이상해씨", [3] = "이상해꽃" },
                CaughtAt = Now,
                Nature = PokemonNature.Hardy,
                Rarity = PokemonRarity.Common,
            },
        ],
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PokeTokenBarSaveTransferTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
