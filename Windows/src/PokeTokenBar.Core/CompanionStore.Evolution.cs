namespace PokeTokenBar.Core;

public sealed partial class CompanionStore
{
    private async Task<bool> PrepareEvolutionAsync(CancellationToken token)
    {
        if (_pokemonProvider is null) return false;
        lock (_stateLock)
            if (_state.ActivePokemon is null || ReferenceEquals(_state.ActivePokemon, _validatedEvolutionSubject))
                return false;

        await _hatchGate.WaitAsync(token).ConfigureAwait(false);
        var changed = false;
        try
        {
            PokemonMonState subject;
            lock (_stateLock)
            {
                if (_state.ActivePokemon is not { } active || ReferenceEquals(active, _validatedEvolutionSubject)) return false;
                subject = active;
            }
            PokemonEvolutionLine line;
            try { line = await _pokemonProvider.GetEvolutionLineAsync(subject.BaseId, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { return false; }
            lock (_stateLock)
            {
                if (!ReferenceEquals(subject, _state.ActivePokemon) || line.BaseId != subject.BaseId) return false;
                var reached = subject.PathIds.Take(subject.StageIndex + 1).ToArray();
                var current = EvolutionPlanner.Follow(line.Tree, reached);
                // Never erase an already reached form because a response is incomplete.
                if (current is null) return false;
                var plannedEnd = EvolutionPlanner.Follow(line.Tree, subject.PlannedPathIds);
                var reusable = plannedEnd is { Children.Count: 0 }
                    && subject.PlannedPathIds.Take(reached.Length).SequenceEqual(reached);
                var plan = reusable ? subject.PlannedPathIds
                    : reached.Concat(BuildEvolutionPlan(current, subject.BaseId).Skip(1)).ToList();
                changed = !plan.SequenceEqual(subject.PlannedPathIds) || subject.TotalForms != plan.Count;
                subject.PlannedPathIds = plan;
                subject.TotalForms = plan.Count;
                foreach (var pair in line.Names) subject.Names.TryAdd(pair.Key, pair.Value);
                _validatedEvolutionSubject = subject;
                var beforeId = subject.CurrentId;
                ProcessActiveProgress();
                changed |= !ReferenceEquals(subject, _state.ActivePokemon) || subject.CurrentId != beforeId;
                TrySaveState();
            }
        }
        finally { _hatchGate.Release(); }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        return changed;
    }
}
