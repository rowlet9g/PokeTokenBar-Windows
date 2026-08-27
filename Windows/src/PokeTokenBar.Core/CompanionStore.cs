using System.Globalization;
using System.Text.Json;

namespace PokeTokenBar.Core;

public sealed class CompanionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly object _stateLock = new();
    private readonly string _filePath;
    private CompanionState _state;
    private string? _lastPersistenceError;

    public CompanionStore(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new ArgumentException("Companion state path must have a parent directory.", nameof(filePath));
        Directory.CreateDirectory(directory);
        _state = LoadState();
    }

    public event EventHandler? Changed;

    public string StateFilePath => _filePath;

    public bool InstallBaselineSet
    {
        get
        {
            lock (_stateLock)
            {
                return _state.InstallBaselineSet;
            }
        }
    }

    public long UsedSinceInstall
    {
        get
        {
            lock (_stateLock)
            {
                return _state.UsedSinceInstall;
            }
        }
    }

    public long AvailableTokens
    {
        get
        {
            lock (_stateLock)
            {
                return Math.Max(0, _state.UsedSinceInstall - _state.SpentTokens);
            }
        }
    }

    public long EggUsage
    {
        get
        {
            lock (_stateLock)
            {
                return _state.EggUsage;
            }
        }
    }

    public bool EggStarted => EggUsage > 0;

    public double EggProgress => Math.Clamp(
        EggUsage / (double)PokemonBalance.EggHatchThreshold,
        0,
        1);

    public long EggTokensToHatch => Math.Max(
        0,
        PokemonBalance.EggHatchThreshold - EggUsage);

    public bool ReadyToHatch => EggUsage >= PokemonBalance.EggHatchThreshold;

    public string LastDate
    {
        get
        {
            lock (_stateLock)
            {
                return _state.LastDate;
            }
        }
    }

    public IReadOnlyDictionary<string, long>? ClaimedTodayTokensByProvider
    {
        get
        {
            lock (_stateLock)
            {
                return _state.ClaimedTodayTokensByProvider is { } ledger
                    ? new Dictionary<string, long>(ledger, StringComparer.Ordinal)
                    : null;
            }
        }
    }

    public string? LastPersistenceError
    {
        get
        {
            lock (_stateLock)
            {
                return _lastPersistenceError;
            }
        }
    }

    public void Update(
        IReadOnlyDictionary<string, long> todayTokensByProvider,
        DateOnly todayDate,
        bool hasUsageData)
    {
        ArgumentNullException.ThrowIfNull(todayTokensByProvider);
        var current = NormalizeLedger(todayTokensByProvider);
        var hasCurrentProviderData = hasUsageData && current.Count > 0;
        var changed = false;

        lock (_stateLock)
        {
            if (!_state.InstallBaselineSet)
            {
                if (!hasCurrentProviderData)
                {
                    return;
                }

                _state.InstallBaselineSet = true;
                _state.ClaimedTodayTokensByProvider = current;
                _state.LastDate = DateKey(todayDate);
                changed = true;
            }
            else if (hasCurrentProviderData)
            {
                changed = UpdateLedger(current, DateKey(todayDate));
            }

            if (changed)
            {
                TrySaveState();
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool UpdateLedger(Dictionary<string, long> current, string dateKey)
    {
        if (_state.ClaimedTodayTokensByProvider is null)
        {
            _state.ClaimedTodayTokensByProvider = current;
            _state.LastDate = dateKey;
            return true;
        }

        if (!string.Equals(_state.LastDate, dateKey, StringComparison.Ordinal))
        {
            var next = _state.ClaimedTodayTokensByProvider.Keys.ToDictionary(
                providerId => providerId,
                _ => 0L,
                StringComparer.Ordinal);
            foreach (var (providerId, tokens) in current)
            {
                next[providerId] = tokens;
            }

            _state.LastDate = dateKey;
            _state.ClaimedTodayTokensByProvider = next;
            ApplyUsage(current.Values.Aggregate(0L, SaturatingTokenAdd));
            return true;
        }

        var ledger = _state.ClaimedTodayTokensByProvider;
        var delta = 0L;
        var changed = false;
        foreach (var (providerId, tokens) in current)
        {
            if (!ledger.TryGetValue(providerId, out var previous))
            {
                ledger[providerId] = tokens;
                changed = true;
                continue;
            }

            if (tokens < previous)
            {
                ledger[providerId] = tokens;
                changed = true;
                continue;
            }

            delta = SaturatingTokenAdd(delta, tokens - previous);
            if (tokens != previous)
            {
                ledger[providerId] = tokens;
                changed = true;
            }
        }

        if (delta > 0)
        {
            ApplyUsage(delta);
        }

        return changed;
    }

    private void ApplyUsage(long delta)
    {
        if (delta <= 0)
        {
            return;
        }

        _state.UsedSinceInstall = SaturatingTokenAdd(_state.UsedSinceInstall, delta);
        _state.EggUsage = SaturatingTokenAdd(_state.EggUsage, delta);
    }

    private CompanionState LoadState()
    {
        if (!File.Exists(_filePath))
        {
            return new CompanionState();
        }

        try
        {
            using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var decoded = JsonSerializer.Deserialize<CompanionState>(stream, JsonOptions)
                ?? throw new JsonException("Companion state was empty.");
            Sanitize(decoded);
            return decoded;
        }
        catch (JsonException error)
        {
            BackupCorruptState(error.Message);
            return new CompanionState();
        }
        catch (IOException error)
        {
            _lastPersistenceError = error.Message;
            return new CompanionState();
        }
        catch (UnauthorizedAccessException error)
        {
            _lastPersistenceError = error.Message;
            return new CompanionState();
        }
    }

    private void TrySaveState()
    {
        var temporaryPath = $"{_filePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, _state, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
            _lastPersistenceError = null;
        }
        catch (IOException error)
        {
            _lastPersistenceError = error.Message;
        }
        catch (UnauthorizedAccessException error)
        {
            _lastPersistenceError = error.Message;
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // The completed move already removed the temporary path.
            }
            catch (UnauthorizedAccessException)
            {
                // A stale temporary file is preferable to losing in-memory progress.
            }
        }
    }

    private void BackupCorruptState(string errorDescription)
    {
        var backupPath = $"{_filePath}.corrupt";
        try
        {
            File.Move(_filePath, backupPath, overwrite: true);
            _lastPersistenceError = $"Invalid companion state was moved to {Path.GetFileName(backupPath)}: {errorDescription}";
        }
        catch (IOException error)
        {
            _lastPersistenceError = error.Message;
        }
        catch (UnauthorizedAccessException error)
        {
            _lastPersistenceError = error.Message;
        }
    }

    private static Dictionary<string, long> NormalizeLedger(
        IReadOnlyDictionary<string, long> source)
    {
        var normalized = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (providerId, tokens) in source)
        {
            if (!string.IsNullOrWhiteSpace(providerId))
            {
                normalized[providerId] = ClampToken(tokens);
            }
        }

        return normalized;
    }

    private static void Sanitize(CompanionState state)
    {
        state.UsedSinceInstall = ClampToken(state.UsedSinceInstall);
        state.SpentTokens = ClampToken(state.SpentTokens);
        state.EggUsage = ClampToken(state.EggUsage);
        state.LastDate ??= string.Empty;
        if (state.ClaimedTodayTokensByProvider is { } ledger)
        {
            state.ClaimedTodayTokensByProvider = NormalizeLedger(ledger);
        }
    }

    private static long ClampToken(long value) =>
        Math.Clamp(value, 0, PokemonBalance.MaxTokenValue);

    private static long SaturatingTokenAdd(long left, long right) =>
        Math.Min(
            PokemonBalance.MaxTokenValue,
            UsageMath.SaturatingAdd(ClampToken(left), ClampToken(right)));

    private static string DateKey(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
