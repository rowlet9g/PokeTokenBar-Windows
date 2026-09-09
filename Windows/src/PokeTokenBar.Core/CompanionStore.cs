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
    private bool _persistenceEnabled = true;
    private string _stateLoadDescription = "State loading has not started.";

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

    public CompanionState ExportStateSnapshot()
    {
        lock (_stateLock)
        {
            return SaveTransfer.CloneState(_state);
        }
    }

    /// <summary>
    /// Retries persistence for the current in-memory state. This is used during
    /// shutdown so a transient file-lock failure cannot discard the latest
    /// evolution that was already applied in memory.
    /// </summary>
    public void Persist()
    {
        lock (_stateLock)
        {
            TrySaveState();
        }
    }

    public async Task ImportStateAsync(
        CompanionState imported,
        IReadOnlyDictionary<string, long> todayTokensByProvider,
        DateOnly today,
        bool hasUsageData,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(todayTokensByProvider);
        await _hatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                var previous = SaveTransfer.CloneState(_state);
                var replacement = SaveTransfer.CloneState(imported);
                Sanitize(replacement);
                if (hasUsageData && todayTokensByProvider.Count > 0)
                {
                    replacement.InstallBaselineSet = true;
                    replacement.ClaimedTodayTokensByProvider = NormalizeLedger(todayTokensByProvider);
                    replacement.LastDate = DateKey(today);
                }
                else
                {
                    replacement.InstallBaselineSet = false;
                    replacement.ClaimedTodayTokensByProvider = null;
                    replacement.LastDate = string.Empty;
                }

                var directory = Path.GetDirectoryName(_filePath)
                    ?? throw new InvalidOperationException("Companion state path has no parent directory.");
                var backupPath = Path.Combine(
                    directory,
                    $"{SaveTransfer.BackupFilePrefix}{now:yyyy-MM-dd-HHmmss-fff}.json");
                SaveTransfer.WriteStateFile(backupPath, previous);

                try
                {
                    SaveTransfer.WriteStateFile(_filePath, replacement);
                    _state = replacement;
                    _persistenceEnabled = true;
                    _lastPersistenceError = null;
                    _stateLoadDescription = "Imported a versioned save file.";
                    TrimImportBackups(directory);
                }
                catch
                {
                    _state = previous;
                    throw;
                }
            }
        }
        finally
        {
            _hatchGate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

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

    public long SpentTokens
    {
        get
        {
            lock (_stateLock)
            {
                return _state.SpentTokens;
            }
        }
    }

    public bool OwnsShinyCharm => ItemCount(CompanionItemKind.ShinyCharm) > 0;

    public IReadOnlyList<OwnedCompanionItem> OwnedItems
    {
        get
        {
            lock (_stateLock)
            {
                return Enum.GetValues<CompanionItemKind>()
                    .Select(kind => new OwnedCompanionItem(kind, ItemCountUnsafe(kind)))
                    .Where(item => item.Count > 0)
                    .ToArray();
            }
        }
    }

    public int ItemCount(CompanionItemKind kind)
    {
        lock (_stateLock)
        {
            return ItemCountUnsafe(kind);
        }
    }

    public bool CanBuyItem(CompanionItemKind kind)
    {
        lock (_stateLock)
        {
            return CanBuyItemUnsafe(kind);
        }
    }

    public bool BuyItem(CompanionItemKind kind)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (!CanBuyItemUnsafe(kind))
            {
                return false;
            }

            var price = CompanionItemRules.Price(kind);
            _state.SpentTokens = SaturatingTokenAdd(_state.SpentTokens, price);
            var key = CompanionItemRules.StorageKey(kind);
            _state.Inventory[key] = Math.Min(int.MaxValue, ItemCountUnsafe(kind) + 1);
            TrySaveState();
            changed = true;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    public bool CanUseRareCandy
    {
        get
        {
            lock (_stateLock)
            {
                return _state.ActivePokemon is not null
                    && ItemCountUnsafe(CompanionItemKind.RareCandy) > 0;
            }
        }
    }

    public RareCandyUseResult UseRareCandy()
    {
        RareCandyUseResult result;
        lock (_stateLock)
        {
            if (_state.ActivePokemon is not { } active
                || ItemCountUnsafe(CompanionItemKind.RareCandy) <= 0)
            {
                return RareCandyUseResult.Unavailable;
            }

            var beforeStage = active.StageIndex;
            DecrementItemUnsafe(CompanionItemKind.RareCandy);
            active.UsedAtStage = SaturatingTokenAdd(
                active.UsedAtStage,
                CompanionItemRules.RareCandyExperience);
            ProcessActiveProgress();
            result = _state.ActivePokemon switch
            {
                null => RareCandyUseResult.Graduated,
                { StageIndex: var stage } when stage > beforeStage => RareCandyUseResult.Evolved,
                _ => RareCandyUseResult.Progressed,
            };
            TrySaveState();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public bool CanUseMint
    {
        get
        {
            lock (_stateLock)
            {
                return _state.ActivePokemon is not null
                    && ItemCountUnsafe(CompanionItemKind.Mint) > 0;
            }
        }
    }

    public PokemonNature? UseMint()
    {
        PokemonNature selected;
        lock (_stateLock)
        {
            if (_state.ActivePokemon is not { } active
                || ItemCountUnsafe(CompanionItemKind.Mint) <= 0)
            {
                return null;
            }

            var candidates = Enum.GetValues<PokemonNature>()
                .Where(nature => nature != active.Nature)
                .ToArray();
            selected = candidates[(int)_randomSource.NextInt64(candidates.Length)];
            active.Nature = selected;
            DecrementItemUnsafe(CompanionItemKind.Mint);
            TrySaveState();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return selected;
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

    public PokemonRarity? EggGuarantee
    {
        get
        {
            lock (_stateLock)
            {
                return _state.ActivePokemon is null ? _state.EggGuarantee : null;
            }
        }
    }

    public bool CanBuyFreshEgg(FreshEggTier tier)
    {
        lock (_stateLock)
        {
            return _state.ActivePokemon is not null
                && Math.Max(0, _state.UsedSinceInstall - _state.SpentTokens)
                    >= CompanionItemRules.FreshEggPrice(tier);
        }
    }

    public bool BuyFreshEgg(FreshEggTier tier)
    {
        lock (_stateLock)
        {
            if (_state.ActivePokemon is null
                || Math.Max(0, _state.UsedSinceInstall - _state.SpentTokens)
                    < CompanionItemRules.FreshEggPrice(tier))
            {
                return false;
            }

            _state.SpentTokens = SaturatingTokenAdd(
                _state.SpentTokens,
                CompanionItemRules.FreshEggPrice(tier));
            _state.Dex.Add(CreateReleasedDexEntry(_state.ActivePokemon));
            _state.ActivePokemon = null;
            _state.EggUsage = 0;
            _state.PendingHatchId = null;
            _state.EggGuarantee = CompanionItemRules.FreshEggGuarantee(tier);
            TrySaveState();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
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

    public async Task<IReadOnlyList<PokemonLineStage>> GetEvolutionPreviewAsync(CancellationToken token = default)
    {
        int? baseId;
        IReadOnlyList<PokemonLineStage> stages;
        lock (_stateLock)
        {
            baseId = _state.ActivePokemon?.BaseId;
            stages = CurrentLineStages;
        }
        if (baseId is null || _pokemonProvider is null) return stages;
        var line = await _pokemonProvider.GetEvolutionLineAsync(baseId.Value, token).ConfigureAwait(false);
        return EvolutionPreview.Build(stages, line);
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
                        new Dictionary<int, string>(active.Names),
                        active.Rarity,
                        null,
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
                        new Dictionary<int, string>(entry.Names),
                        entry.Rarity,
                        entry.CaughtAt,
                        entry.IsShiny,
                        entry.Nature,
                        false,
                        entry.IsReleased)));
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

    public string StateLoadDescription
    {
        get
        {
            lock (_stateLock)
            {
                return _stateLoadDescription;
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

                if (!CompanionItemRules.MeetsGuarantee(line.Rarity, _state.EggGuarantee))
                {
                    _state.PendingHatchId = null;
                    TrySaveState();
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
                    IsShiny = CompanionItemRules.IsShinyRoll(
                        _randomSource.NextInt64(long.MaxValue),
                        OwnsShinyCharm),
                    Nature = (PokemonNature)_randomSource.NextInt64(natureCount),
                    Names = line.Names.ToDictionary(pair => pair.Key, pair => pair.Value),
                };
                _state.EggUsage = 0;
                _state.EggGuarantee = null;
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
            PokemonRarity? guarantee;
            lock (_stateLock)
            {
                guarantee = _state.EggGuarantee;
            }

            var index = await _pokemonProvider!.GetBaseSpeciesIndexAsync(cancellationToken)
                .ConfigureAwait(false);
            var candidates = index
                .Where(species => PokemonAssets.HasSprite(species.Id)
                    && species.Id != PokemonAssets.DittoSpeciesId)
                .Where(species => CompanionItemRules.MeetsGuarantee(
                    PokemonBalance.RarityFrom(
                        species.CaptureRate,
                        isLegendary: false,
                        isMythical: false),
                    guarantee))
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

    private static PokemonDexEntry CreateReleasedDexEntry(PokemonMonState active)
    {
        var reached = active.PathIds
            .Take(Math.Clamp(active.StageIndex + 1, 1, Math.Max(1, active.PathIds.Count)))
            .ToList();
        if (reached.Count == 0)
        {
            reached.Add(active.BaseId);
        }

        var now = DateTimeOffset.UtcNow;
        var reachedSet = reached.ToHashSet();
        return new PokemonDexEntry
        {
            BaseId = active.BaseId,
            FinalId = reached[^1],
            ChainOrder = reached,
            Rarity = active.Rarity,
            CaughtAt = now,
            ReleasedAt = now,
            IsShiny = active.IsShiny,
            Nature = active.Nature,
            Names = active.Names
                .Where(pair => reachedSet.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value),
        };
    }

    private static long PhaseThreshold(PokemonMonState active) =>
        PokemonBalance.PhaseThreshold(
            active.Rarity,
            active.TotalForms,
            active.StageIndex);

    private CompanionState LoadState()
    {
        const int readAttempts = 3;
        for (var attempt = 1; attempt <= readAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    _filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var decoded = JsonSerializer.Deserialize<CompanionState>(stream, JsonOptions)
                    ?? throw new JsonException("Companion state was empty.");
                Sanitize(decoded);
                _stateLoadDescription = decoded.ActivePokemon is { } active
                    ? $"Loaded state with active species #{active.CurrentId}."
                    : $"Loaded state with no active species and {decoded.Dex.Count} graduated entries.";
                return decoded;
            }
            catch (FileNotFoundException)
            {
                _stateLoadDescription = "No companion state file existed at startup.";
                return new CompanionState();
            }
            catch (DirectoryNotFoundException)
            {
                _stateLoadDescription = "The companion state directory did not exist at startup.";
                return new CompanionState();
            }
            catch (JsonException error)
            {
                _stateLoadDescription = $"The companion state was invalid JSON: {error.Message}";
                BackupCorruptState(error.Message);
                return new CompanionState();
            }
            catch (IOException error) when (attempt < readAttempts)
            {
                _lastPersistenceError = error.Message;
                Thread.Sleep(50 * attempt);
            }
            catch (IOException error)
            {
                DisablePersistenceAfterLoadFailure(error.Message);
                return new CompanionState();
            }
            catch (UnauthorizedAccessException error)
            {
                DisablePersistenceAfterLoadFailure(error.Message);
                return new CompanionState();
            }
        }

        throw new InvalidOperationException("Companion state read attempts ended unexpectedly.");
    }

    private void TrySaveState()
    {
        if (!_persistenceEnabled)
        {
            return;
        }

        string? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
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
                return;
            }
            catch (IOException error)
            {
                lastError = error.Message;
            }
            catch (UnauthorizedAccessException error)
            {
                lastError = error.Message;
            }
            finally
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // A stale temporary file is preferable to losing in-memory progress.
                }
                catch (UnauthorizedAccessException)
                {
                    // A stale temporary file is preferable to losing in-memory progress.
                }
            }

            if (attempt < 3)
            {
                Thread.Sleep(50 * attempt);
            }
        }

        _lastPersistenceError = lastError;
    }

    private void DisablePersistenceAfterLoadFailure(string errorDescription)
    {
        _persistenceEnabled = false;
        _stateLoadDescription = $"The companion state could not be read: {errorDescription}";
        _lastPersistenceError =
            $"Companion state could not be loaded; saving is disabled until restart: {errorDescription}";
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

    internal static void SanitizeImportedState(CompanionState state) => Sanitize(state);

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
            state.EggGuarantee = null;
        }

        state.Inventory ??= [];
        state.Inventory = Enum.GetValues<CompanionItemKind>()
            .Select(kind => (Key: CompanionItemRules.StorageKey(kind), Kind: kind))
            .Where(item => state.Inventory.TryGetValue(item.Key, out var count) && count > 0)
            .ToDictionary(
                item => item.Key,
                item => CompanionItemRules.IsPassive(item.Kind)
                    ? 1
                    : Math.Clamp(state.Inventory[item.Key], 1, 1_000_000),
                StringComparer.Ordinal);
    }

    private static void TrimImportBackups(string directory)
    {
        try
        {
            var backups = Directory.EnumerateFiles(
                    directory,
                    $"{SaveTransfer.BackupFilePrefix}*.json",
                    SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.Name, StringComparer.Ordinal)
                .Skip(SaveTransfer.BackupsToKeep)
                .ToArray();
            foreach (var backup in backups)
            {
                backup.Delete();
            }
        }
        catch (IOException)
        {
            // A successful import must not be rolled back only because old backup cleanup failed.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private int ItemCountUnsafe(CompanionItemKind kind) =>
        _state.Inventory.TryGetValue(CompanionItemRules.StorageKey(kind), out var count)
            ? Math.Max(0, count)
            : 0;

    private bool CanBuyItemUnsafe(CompanionItemKind kind)
    {
        if (CompanionItemRules.IsPassive(kind) && ItemCountUnsafe(kind) > 0)
        {
            return false;
        }

        return Math.Max(0, _state.UsedSinceInstall - _state.SpentTokens)
            >= CompanionItemRules.Price(kind);
    }

    private void DecrementItemUnsafe(CompanionItemKind kind)
    {
        var key = CompanionItemRules.StorageKey(kind);
        var next = Math.Max(0, ItemCountUnsafe(kind) - 1);
        if (next == 0)
        {
            _state.Inventory.Remove(key);
        }
        else
        {
            _state.Inventory[key] = next;
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
