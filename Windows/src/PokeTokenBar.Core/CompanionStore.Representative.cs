namespace PokeTokenBar.Core;

public sealed partial class CompanionStore
{
    public int? RepresentativeSpeciesId
    {
        get { lock (_stateLock) return _state.RepresentativeSpeciesId; }
    }

    public RepresentativePokemon Representative
    {
        get
        {
            lock (_stateLock)
            {
                if (_state.RepresentativeSpeciesId is { } id
                    && TryGetOwnedSpecies(_state, id, out var name, out var shiny))
                    return new(id, name, shiny, true);
                var active = _state.ActivePokemon;
                return new(active?.CurrentId, active?.CurrentName ?? "새 알", active?.VisibleShiny ?? false, false);
            }
        }
    }

    public bool SetRepresentativeSpecies(int? speciesId)
    {
        lock (_stateLock)
        {
            // Planned future forms and hidden Ditto identities are not owned.
            if (speciesId is { } id && !TryGetOwnedSpecies(_state, id, out _, out _)) return false;
            if (_state.RepresentativeSpeciesId == speciesId) return true;
            if (!_persistenceEnabled) return false;
            var previous = _state.RepresentativeSpeciesId;
            _state.RepresentativeSpeciesId = speciesId;
            TrySaveState();
            if (_lastPersistenceError is not null)
            {
                _state.RepresentativeSpeciesId = previous;
                return false;
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static void ReconcileRepresentativeSelection(CompanionState state)
    {
        if (state.RepresentativeSpeciesId is { } id && !TryGetOwnedSpecies(state, id, out _, out _))
            state.RepresentativeSpeciesId = null;
    }

    private static bool TryGetOwnedSpecies(CompanionState state, int id, out string name, out bool shiny)
    {
        name = $"#{id}";
        shiny = false;
        if (!PokemonAssets.HasSprite(id)) return false;
        var owned = false;
        foreach (var entry in state.Dex)
        {
            if (!entry.ChainOrder.Contains(id)) continue;
            owned = true;
            shiny |= entry.IsShiny;
            if (entry.Names.TryGetValue(id, out var storedName)) name = storedName;
        }
        if (state.ActivePokemon is { } active && active.PathIds.Take(active.StageIndex + 1).Contains(id))
        {
            owned = true;
            shiny |= active.VisibleShiny;
            if (active.Names.TryGetValue(id, out var storedName)) name = storedName;
        }
        return owned;
    }
}
