namespace PokeTokenBar.Core;

public sealed partial class CompanionStore
{
    public static bool DittoDisguiseHit(PokemonRarity rarity, int totalForms, long roll) =>
        rarity == PokemonRarity.Common && totalForms >= 2 && roll % 128 == 0;

    public bool IsCurrentPokemonRevealedDitto
    {
        get { lock (_stateLock) return _state.ActivePokemon?.DittoRevealed ?? false; }
    }

    private async Task<bool> RevealDittoAsync(CancellationToken token)
    {
        if (_pokemonProvider is null) return false;
        lock (_stateLock)
        {
            if (_state.ActivePokemon is not { IsDittoDisguised: true } active
                || active.UsedAtStage < PhaseThreshold(active)) return false;
        }

        await _hatchGate.WaitAsync(token).ConfigureAwait(false);
        var changed = false;
        try
        {
            PokemonMonState subject;
            lock (_stateLock)
            {
                if (_state.ActivePokemon is not { IsDittoDisguised: true } active
                    || active.UsedAtStage < PhaseThreshold(active)) return false;
                subject = active;
            }
            PokemonEvolutionLine line;
            try
            {
                line = await _pokemonProvider.GetEvolutionLineAsync(PokemonAssets.DittoSpeciesId, token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { return false; }
            if (line.BaseId != PokemonAssets.DittoSpeciesId
                || line.Tree.SpeciesId != PokemonAssets.DittoSpeciesId || line.Tree.Children.Count != 0)
                return false;

            lock (_stateLock)
            {
                // Egg purchases may replace the subject while the request is in flight.
                if (!ReferenceEquals(subject, _state.ActivePokemon) || !subject.IsDittoDisguised
                    || subject.UsedAtStage < PhaseThreshold(subject)) return false;
                var carryOver = subject.UsedAtStage - PhaseThreshold(subject);
                subject.BaseId = PokemonAssets.DittoSpeciesId;
                subject.PathIds = [PokemonAssets.DittoSpeciesId];
                subject.PlannedPathIds = [PokemonAssets.DittoSpeciesId];
                subject.StageIndex = 0;
                subject.TotalForms = 1;
                subject.Rarity = line.Rarity;
                subject.Names = line.Names.ToDictionary(pair => pair.Key, pair => pair.Value);
                subject.UsedAtStage = carryOver;
                subject.DittoRevealed = true;
                ProcessActiveProgress();
                TrySaveState();
                changed = true;
            }
        }
        finally { _hatchGate.Release(); }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        return changed;
    }
}
