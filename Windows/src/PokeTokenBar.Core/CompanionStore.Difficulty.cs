namespace PokeTokenBar.Core;

public sealed partial class CompanionStore
{
    private double _growthDifficulty = PokemonBalance.DefaultDifficulty;
    private double _shopDifficulty = PokemonBalance.DefaultDifficulty;

    public double GrowthDifficulty { get { lock (_stateLock) return _growthDifficulty; } }
    public double ShopDifficulty { get { lock (_stateLock) return _shopDifficulty; } }
    public long EggHatchThreshold { get { lock (_stateLock) return PokemonBalance.Scaled(PokemonBalance.EggHatchThreshold, _growthDifficulty); } }
    public long Price(CompanionItemKind kind) { lock (_stateLock) return ItemPriceUnsafe(kind); }
    public long FreshEggPrice(FreshEggTier tier) { lock (_stateLock) return FreshEggPriceUnsafe(tier); }
    private long ItemPriceUnsafe(CompanionItemKind kind) => PokemonBalance.Scaled(CompanionItemRules.Price(kind), _shopDifficulty);
    private long FreshEggPriceUnsafe(FreshEggTier tier) => PokemonBalance.Scaled(CompanionItemRules.FreshEggPrice(tier), _shopDifficulty);

    private void LoadDifficultySettings(AppSettings settings)
    {
        var normalized = settings.Normalize();
        _growthDifficulty = normalized.GrowthDifficulty;
        _shopDifficulty = normalized.ShopDifficulty;
        if (_state.GrowthDifficultyBasis == _growthDifficulty) return;
        if (!_persistenceEnabled) throw new IOException("진행 상태를 읽지 못해 난이도를 적용할 수 없습니다.");
        RebaseGrowth(_state, _growthDifficulty);
        TrySaveState();
        if (_lastPersistenceError is { } error) throw new IOException(error);
    }

    // Persist growth before preferences. The saved unit basis makes either side
    // of a crash recoverable; a rejected settings write restores original credits.
    public void SaveSettings(AppSettingsStore settingsStore, AppSettings requested)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(requested);
        var normalized = requested.Normalize();
        bool changed;
        lock (_stateLock)
        {
            var previousGrowth = _growthDifficulty;
            var previousShop = _shopDifficulty;
            var previousBasis = _state.GrowthDifficultyBasis;
            var previousEgg = _state.EggUsage;
            var previousStage = _state.ActivePokemon?.UsedAtStage;
            var growthChanged = previousGrowth != normalized.GrowthDifficulty;
            changed = growthChanged || previousShop != normalized.ShopDifficulty;
            var growthSaved = false;
            try
            {
                if (growthChanged)
                {
                    if (!_persistenceEnabled) throw new IOException("진행 저장이 비활성화되어 난이도를 변경할 수 없습니다.");
                    RebaseGrowth(_state, normalized.GrowthDifficulty);
                    TrySaveState();
                    if (_lastPersistenceError is { } error) throw new IOException(error);
                    growthSaved = true;
                }
                settingsStore.Save(normalized);
                _growthDifficulty = normalized.GrowthDifficulty;
                _shopDifficulty = normalized.ShopDifficulty;
            }
            catch
            {
                _state.GrowthDifficultyBasis = previousBasis;
                _state.EggUsage = previousEgg;
                if (_state.ActivePokemon is { } active && previousStage is { } credits) active.UsedAtStage = credits;
                if (growthSaved) TrySaveState();
                throw;
            }
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void RebaseGrowth(CompanionState state, double difficulty)
    {
        var previous = state.GrowthDifficultyBasis;
        if (previous == difficulty) return;
        if (state.ActivePokemon is { } active)
            active.UsedAtStage = RescaleGrowth(active.UsedAtStage, BasePhaseThreshold(active), previous, difficulty);
        else
            state.EggUsage = RescaleGrowth(state.EggUsage, PokemonBalance.EggHatchThreshold, previous, difficulty);
        state.GrowthDifficultyBasis = difficulty;
    }

    private static long RescaleGrowth(long credits, long baseThreshold, double previous, double next)
    {
        var oldThreshold = PokemonBalance.Scaled(baseThreshold, previous);
        var newThreshold = PokemonBalance.Scaled(baseThreshold, next);
        var scaled = (long)Math.Min(PokemonBalance.MaxTokenValue, decimal.Floor((decimal)credits * newThreshold / oldThreshold));
        // Never complete an unfinished stage due to rounding; retain overflow.
        return credits < oldThreshold ? Math.Min(newThreshold - 1, scaled) : scaled;
    }
}
