namespace PokeTokenBar.Core.Tests;

public sealed class EvolutionPreviewTests
{
    [Fact]
    public void Linear_evolution_shows_future_forms_without_changing_realized_history()
    {
        PokemonLineStage[] history = [new(4, "파이리", PokemonLineStageStatus.Current, false)];
        var line = new PokemonEvolutionLine(4, new(4, [new(5, [new(6, [])])]), PokemonRarity.Rare,
            new Dictionary<int, string> { [4] = "파이리", [5] = "리자드", [6] = "리자몽" });
        var preview = EvolutionPreview.Build(history, line);
        Assert.Equal(new int?[] { 4, 5, 6 }, preview.Select(s => s.SpeciesId));
        Assert.All(preview.Skip(1), s => Assert.Equal(PokemonLineStageStatus.HiddenFuture, s.Status));
        Assert.Single(history);
    }

    [Fact]
    public void Branch_is_hidden_after_guaranteed_prefix()
    {
        var line = new PokemonEvolutionLine(1, new(1, [new(2, [new(3, []), new(4, [])])]),
            PokemonRarity.Common, new Dictionary<int, string>());
        var preview = EvolutionPreview.Build([new(1, "base", PokemonLineStageStatus.Current, true)], line);
        Assert.Equal(new int?[] { 1, 2, null }, preview.Select(s => s.SpeciesId));
        Assert.All(preview, s => Assert.True(s.IsShiny));
    }
}
