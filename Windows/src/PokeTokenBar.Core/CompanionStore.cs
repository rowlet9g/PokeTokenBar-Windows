using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokeTokenBar.Core;

public sealed class CompanionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _stateLock = new();
    private readonly string _filePath;
    private readonly IPokemonProvider? _pokemonProvider;
    private readonly IRandomSource _randomSource;
    private readonly SemaphoreSlim _hatchGate = new(1, 1);
    private CompanionState _state;
    private string? _lastPersistenceError;

    public CompanionStore(
        string filePath,
        IPokemonProvider? pokemonProvider = null,
        IRandomSource? randomSource = null)
    {
        _filePath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new ArgumentException("Companion state path must have a parent directory.", nameof(filePath));
        Directory.CreateDirectory(directory);
        _pokemonProvider = pokemonProvider;
        _randomSource = randomSource ?? new SystemRandomSource();
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

    public bool ReadyToHatch
    {
        get
        {
            lock (_stateLock)
            {
                return _state.ActivePokemon is null
                    && _state.EggUsage >= PokemonBalance.EggHatchThreshold;
            }
        }
    }

    public bool HasActivePokemon
    {
        get
        {
            lock (_stateLock)
            {
                return _state.ActivePokemon is not null;
            }
        }
    }

    public int? CurrentSpeciesId
    {
        get
        {
            lock (_stateLock)
            {
                return _state.ActivePokemon?.CurrentId;
            }
        }
    }

    public string? CurrentPokemonName
    {
        get
        {
            lock (_stateLock)
            {
                return _state.ActivePokemon?.CurrentName;
            }
        }
    }

    public bool IsCurrentPokemonShiny
    {
        get
        {
            lock (_stateLock)
            {
                return _state.ActivePokemon?.IsShiny ?? false;
            }
        }
    }

    public PokemonNature? CurrentPokemonNature
    {
        get
        {
            lock (_stateLock)
            {
                return _state.ActivePokemon?.Nature;
            }
        }
    }

    public PokemonRarity? CurrentPokemonRarity
    {
        get
        {
            lock (_stateLock)
            {
                return _state.ActivePokemon?.Rarity;
            }
        }
    }

    public int CurrentStage
    {
        get
        {
            lock (_stateLock)
            {
                return (_state.ActivePokemon?.StageIndex ?? 0) + 1;
            }
        }
    }

    public int TotalForms
    {
        get
        {
            lock (_stateLock)
            {
                return _state.ActivePokemon?.TotalForms ?? 1;
            }
        }
    }

    public long ActiveUsedAtStage
    {
        get
        {
            lock (_stateLock)
            {
                return _state.ActivePokemon?.UsedAtStage ?? 0;
            }
        }
    }

    public long TokensToNextStage
    {
        get
        {
            lock (_stateLock)
            {
                if (_state.ActivePokemon is not { } active)
                {
                    return 0;
                }

                return Math.Max(0, PhaseThreshold(active) - active.UsedAtStage);
            }
        }
    }

    public double GrowthProgress
    {
        get
        {
            lock (_stateLock)
            {
                if (_state.ActivePokemon is not { } active)
                {
                    return 0;
                }

                return Math.Clamp(active.UsedAtStage / (double)PhaseThreshold(active), 0, 1);
            }
        }
    }

    public int DexCount
    {
        get
        {
            lock (_stateLock)
            {
                return _state.Dex.Count;
            }
        }
    }

    public IReadOnlyList<int> CurrentEvolutionPath
    {
        get
        {
            lock (_stateLock)
            {
                return _state.ActivePokemon?.PathIds.ToArray() ?? [];
            }
        }
    }

    public IReadOnlyList<PokemonLineStage> CurrentLineStages
    {
        get
        {
            lock (_stateLock)
            {
                if (_state.ActivePokemon is not { } active)
                {
                    return [];
                }

                return active.PlannedPathIds
                    .Select((speciesId, index) => index switch
                    {
                        _ when index < active.StageIndex => new PokemonLineStage(
                            speciesId,
                            NameFor(active.Names, speciesId),
                            PokemonLineStageStatus.Realized,
                            active.IsShiny),
                        _ when index == active.StageIndex => new PokemonLineStage(
                            speciesId,
                            NameFor(active.Names, speciesId),
                            PokemonLineStageStatus.Current,
                            active.IsShiny),
                        _ => new PokemonLineStage(
                            null,
                            "???",
                            PokemonLineStageStatus.HiddenFuture,
                            active.IsShiny),
                    })
                    .ToArray();
            }
        }
    }

    public IReadOnlyList<PokemonCollectionEntry> CollectionEntries
    {
        get
        {
            lock (_stateLock)
            {
                var entries = new List<PokemonCollectionEntry>();
                if (_state.ActivePokemon is { } active)
                {
                    entries.Add(new PokemonCollectionEntry(
                        $"active-{active.BaseId}-{active.CurrentId}",
                        active.CurrentId,
                        active.CurrentName,
                        active.PathIds.ToArray(),
                        active.Rarity,
                        DateTimeOffset.MaxValue,
                        active.IsShiny,
                        active.Nature,
                        true));
                }

                entries.AddRange(_state.Dex
                    .OrderByDescending(entry => entry.CaughtAt)
                    .Select(entry => new PokemonCollectionEntry(
                        entry.Id,
                        entry.FinalId,
                        NameFor(entry.Names, entry.FinalId),
                        entry.ChainOrder.ToArray(),
                        entry.Rarity,
                        entry.CaughtAt,
                        entry.IsShiny,
                        entry.Nature,
                        false)));
                return entries;
            }
        }
    }

    public IReadOnlyList<PokemonDexSpecies> DexSpecies
    {
        get
        {
            lock (_stateLock)
            {
                var species = new Dictionary<int, PokemonDexSpecies>();
                foreach (var entry in _state.Dex.OrderBy(item => item.CaughtAt))
                {
                    foreach (var speciesId in entry.ChainOrder)
                    {
                        if (!PokemonAssets.HasSprite(speciesId))
                        {
                            continue;
                        }

                        if (species.TryGetValue(speciesId, out var existing))
                        {
                            species[speciesId] = existing with
                            {
                                IsShiny = existing.IsShiny || entry.IsShiny,
                                IsRaising = false,
                            };
                        }
                        else
                        {
                            species[speciesId] = new PokemonDexSpecies(
                                speciesId,
                                NameFor(entry.Names, speciesId),
                                entry.Rarity,
                                entry.IsShiny,
                                false);
                        }
                    }
                }

                if (_state.ActivePokemon is { } active)
                {
                    foreach (var speciesId in active.PathIds.Take(active.StageIndex + 1))
                    {
                        if (species.TryGetValue(speciesId, out var existing))
                        {
                            species[speciesId] = existing with
                            {
                                IsShiny = existing.IsShiny || active.IsShiny,
                            };
                            continue;
                        }

                        species[speciesId] = new PokemonDexSpecies(
                            speciesId,
                            NameFor(active.Names, speciesId),
                            active.Rarity,
                            active.IsShiny,
                            true);
                    }
                }

                return species.Values.OrderBy(item => item.SpeciesId).ToArray();
            }
        }
    }

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

    public async Task<bool> EnsureHatchedAsync(CancellationToken cancellationToken = default)
    {
        if (_pokemonProvider is null)
        {
            return false;
        }

        await _hatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var changed = false;
        try
        {
            int? baseSpeciesId;
            lock (_stateLock)
            {
                if (_state.ActivePokemon is not null
                    || _state.EggUsage < PokemonBalance.EggHatchThreshold)
                {
                    return false;
                }

                baseSpeciesId = _state.PendingHatchId;
            }

            if (baseSpeciesId is null)
            {
                baseSpeciesId = await ChooseBaseSpeciesAsync(cancellationToken).ConfigureAwait(false);
                if (baseSpeciesId is null)
                {
                    return false;
                }

                lock (_stateLock)
                {
                    if (_state.ActivePokemon is not null
                        || _state.EggUsage < PokemonBalance.EggHatchThreshold)
                    {
                        return false;
                    }

                    _state.PendingHatchId = baseSpeciesId;
                    TrySaveState();
                }
            }

            PokemonEvolutionLine line;
            try
            {
                line = await _pokemonProvider.GetEvolutionLineAsync(
                    baseSpeciesId.Value,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return false;
            }

            if (line.BaseId != baseSpeciesId.Value || line.Tree.SpeciesId != line.BaseId)
            {
                return false;
            }

            var plan = BuildEvolutionPlan(line.Tree);
            lock (_stateLock)
            {
                if (_state.ActivePokemon is not null
                    || _state.EggUsage < PokemonBalance.EggHatchThreshold
                    || _state.PendingHatchId != baseSpeciesId)
                {
                    return false;
                }

                var overflow = Math.Max(0, _state.EggUsage - PokemonBalance.EggHatchThreshold);
                var natureCount = Enum.GetValues<PokemonNature>().Length;
                _state.ActivePokemon = new PokemonMonState
                {
                    BaseId = line.BaseId,
                    PathIds = [line.BaseId],
                    PlannedPathIds = plan,
                    StageIndex = 0,
                    UsedAtStage = overflow,
                    Rarity = line.Rarity,
                    TotalForms = plan.Count,
                    IsShiny = _randomSource.NextInt64(64) == 0,
                    Nature = (PokemonNature)_randomSource.NextInt64(natureCount),
                    Names = line.Names.ToDictionary(pair => pair.Key, pair => pair.Value),
                };
                _state.EggUsage = 0;
                _state.PendingHatchId = null;
                ProcessActiveProgress();
                TrySaveState();
                changed = true;
            }
        }
        finally
        {
            _hatchGate.Release();
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return changed;
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
        if (_state.ActivePokemon is { } active)
        {
            active.UsedAtStage = SaturatingTokenAdd(active.UsedAtStage, delta);
            ProcessActiveProgress();
        }
        else
        {
            _state.EggUsage = SaturatingTokenAdd(_state.EggUsage, delta);
        }
    }

    private async Task<int?> ChooseBaseSpeciesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var index = await _pokemonProvider!.GetBaseSpeciesIndexAsync(cancellationToken)
                .ConfigureAwait(false);
            var candidates = index
                .Where(species => PokemonAssets.HasSprite(species.Id)
                    && species.Id != PokemonAssets.DittoSpeciesId)
                .ToArray();
            if (candidates.Length > 0)
            {
                var totalWeight = candidates.Sum(species => (long)Math.Clamp(species.CaptureRate, 1, 255));
                var roll = _randomSource.NextInt64(totalWeight);
                foreach (var candidate in candidates)
                {
                    roll -= Math.Clamp(candidate.CaptureRate, 1, 255);
                    if (roll < 0)
                    {
                        return candidate.Id;
                    }
                }

                return candidates[^1].Id;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The REST fallback below keeps hatching alive when GraphQL is unavailable.
        }

        for (var attempt = 0; attempt < 16; attempt++)
        {
            var speciesId = PokemonAssets.MinimumSpeciesId
                + (int)_randomSource.NextInt64(
                    PokemonAssets.MaximumSpeciesId - PokemonAssets.MinimumSpeciesId + 1L);
            try
            {
                if (await _pokemonProvider!.GetBaseSpeciesAsync(speciesId, cancellationToken)
                        .ConfigureAwait(false) is not null)
                {
                    return speciesId;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private List<int> BuildEvolutionPlan(PokemonEvolutionNode root)
    {
        var plan = new List<int> { root.SpeciesId };
        var node = root;
        while (node.Children.Count > 0)
        {
            node = node.Children[(int)_randomSource.NextInt64(node.Children.Count)];
            plan.Add(node.SpeciesId);
        }

        return plan;
    }

    private void ProcessActiveProgress()
    {
        var guardCount = 0;
        while (_state.ActivePokemon is { } active && guardCount++ < 50)
        {
            var threshold = PhaseThreshold(active);
            if (active.UsedAtStage < threshold)
            {
                return;
            }

            var nextIndex = active.StageIndex + 1;
            if (nextIndex < active.PlannedPathIds.Count)
            {
                active.UsedAtStage -= threshold;
                active.StageIndex = nextIndex;
                var nextId = active.PlannedPathIds[nextIndex];
                if (active.PathIds.Count <= nextIndex)
                {
                    active.PathIds.Add(nextId);
                }
                else
                {
                    active.PathIds[nextIndex] = nextId;
                    active.PathIds.RemoveRange(nextIndex + 1, active.PathIds.Count - nextIndex - 1);
                }

                continue;
            }

            Graduate(active);
        }
    }

    private void Graduate(PokemonMonState active)
    {
        _state.Dex.Add(new PokemonDexEntry
        {
            BaseId = active.BaseId,
            FinalId = active.CurrentId,
            ChainOrder = [.. active.PathIds],
            Rarity = active.Rarity,
            CaughtAt = DateTimeOffset.UtcNow,
            IsShiny = active.IsShiny,
            Nature = active.Nature,
            Names = new Dictionary<int, string>(active.Names),
        });
        _state.ActivePokemon = null;
        _state.EggUsage = 0;
        _state.PendingHatchId = null;
    }

    private static long PhaseThreshold(PokemonMonState active) =>
        PokemonBalance.PhaseThreshold(
            active.Rarity,
            active.TotalForms,
            active.StageIndex);

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
        if (state.PendingHatchId is { } pending && !PokemonAssets.HasSprite(pending))
        {
            state.PendingHatchId = null;
        }

        state.LastDate ??= string.Empty;
        if (state.ClaimedTodayTokensByProvider is { } ledger)
        {
            state.ClaimedTodayTokensByProvider = NormalizeLedger(ledger);
        }

        state.Dex ??= [];
        state.Dex = state.Dex
            .Where(entry => PokemonAssets.HasSprite(entry.BaseId)
                && PokemonAssets.HasSprite(entry.FinalId))
            .ToList();
        foreach (var entry in state.Dex)
        {
            entry.Id = string.IsNullOrWhiteSpace(entry.Id)
                ? Guid.NewGuid().ToString("N")
                : entry.Id;
            entry.ChainOrder ??= [];
            entry.ChainOrder = entry.ChainOrder.Where(PokemonAssets.HasSprite).ToList();
            if (entry.ChainOrder.Count == 0)
            {
                entry.ChainOrder = entry.BaseId == entry.FinalId
                    ? [entry.BaseId]
                    : [entry.BaseId, entry.FinalId];
            }

            entry.Names ??= [];
            entry.Names = entry.Names
                .Where(pair => PokemonAssets.HasSprite(pair.Key)
                    && !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        if (state.ActivePokemon is { } active)
        {
            SanitizeActivePokemon(state, active);
        }
    }

    private static void SanitizeActivePokemon(CompanionState state, PokemonMonState active)
    {
        if (!PokemonAssets.HasSprite(active.BaseId))
        {
            state.ActivePokemon = null;
            return;
        }

        active.PathIds ??= [];
        active.PlannedPathIds ??= [];
        active.Names ??= [];
        var path = active.PathIds.Where(PokemonAssets.HasSprite).ToList();
        if (path.Count == 0 || path[0] != active.BaseId)
        {
            path = [active.BaseId];
        }

        active.StageIndex = Math.Clamp(active.StageIndex, 0, path.Count - 1);
        path = path.Take(active.StageIndex + 1).ToList();
        var plan = active.PlannedPathIds.Where(PokemonAssets.HasSprite).ToList();
        if (plan.Count < path.Count || !plan.Take(path.Count).SequenceEqual(path))
        {
            plan = [.. path];
        }

        active.PathIds = path;
        active.PlannedPathIds = plan;
        active.StageIndex = path.Count - 1;
        active.TotalForms = Math.Max(1, plan.Count);
        active.UsedAtStage = ClampToken(active.UsedAtStage);
        active.Names = active.Names
            .Where(pair => PokemonAssets.HasSprite(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static long ClampToken(long value) =>
        Math.Clamp(value, 0, PokemonBalance.MaxTokenValue);

    private static long SaturatingTokenAdd(long left, long right) =>
        Math.Min(
            PokemonBalance.MaxTokenValue,
            UsageMath.SaturatingAdd(ClampToken(left), ClampToken(right)));

    private static string DateKey(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string NameFor(IReadOnlyDictionary<int, string> names, int speciesId) =>
        names.TryGetValue(speciesId, out var name) ? name : $"#{speciesId}";
}
