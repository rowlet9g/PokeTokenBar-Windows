using PokeTokenBar.Core;

namespace PokeTokenBar.Core.Tests;

public sealed class CompanionMilestoneTrackerTests
{
    private static readonly CompanionMilestoneSnapshot Egg = new(null, null, 0, false, 0);
    private static readonly CompanionMilestoneSnapshot Base = new(602, "저리어", 1, true, 0);
    private static readonly CompanionMilestoneSnapshot Evolved = new(603, "저리릴", 2, true, 0);

    [Fact]
    public void Existing_companion_at_startup_does_not_emit_a_notification()
    {
        var tracker = new CompanionMilestoneTracker(Base);

        Assert.Null(tracker.Observe(Base));
    }

    [Fact]
    public void Egg_to_active_companion_emits_hatch_once()
    {
        var tracker = new CompanionMilestoneTracker(Egg);

        var milestone = tracker.Observe(Base);

        Assert.Equal(CompanionMilestoneKind.Hatched, milestone?.Kind);
        Assert.Equal("저리어", milestone?.PokemonName);
        Assert.True(milestone?.IsShiny);
        Assert.Null(tracker.Observe(Base));
    }

    [Fact]
    public void Species_or_stage_change_emits_evolution()
    {
        var tracker = new CompanionMilestoneTracker(Base);

        var milestone = tracker.Observe(Evolved);

        Assert.Equal(CompanionMilestoneKind.Evolved, milestone?.Kind);
        Assert.Equal("저리릴", milestone?.PokemonName);
    }

    [Fact]
    public void Active_companion_entering_the_dex_emits_graduation()
    {
        var tracker = new CompanionMilestoneTracker(Evolved);
        var graduated = Egg with { DexCount = 1 };

        var milestone = tracker.Observe(graduated);

        Assert.Equal(CompanionMilestoneKind.Graduated, milestone?.Kind);
        Assert.Equal("저리릴", milestone?.PokemonName);
        Assert.Equal(1, milestone?.DexCount);
    }

    [Fact]
    public void Dex_changes_without_an_active_transition_do_not_emit_a_notification()
    {
        var tracker = new CompanionMilestoneTracker(Egg);

        Assert.Null(tracker.Observe(Egg with { DexCount = 1 }));
    }
}
