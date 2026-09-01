namespace PokeTokenBar.Core.Tests;

public sealed class FloatingPetInteractionTests
{
    [Theory]
    [InlineData(10, 10, 11, 12, true)]
    [InlineData(10, 10, 10, 10, true)]
    [InlineData(10, 10, 14, 10, false)]
    [InlineData(10, 10, 20, 10, false)]
    public void Click_threshold_distinguishes_click_from_drag(
        double startX,
        double startY,
        double endX,
        double endY,
        bool expected)
    {
        Assert.Equal(
            expected,
            FloatingPetInteraction.IsClick(startX, startY, endX, endY));
    }

    [Fact]
    public void Position_is_clamped_inside_the_selected_work_area()
    {
        var area = new FloatingPetBounds(100, 50, 1_100, 750);

        Assert.Equal(
            new FloatingPetPosition(100, 50),
            FloatingPetInteraction.ClampToWorkArea(-20, -30, 96, 96, area));
        Assert.Equal(
            new FloatingPetPosition(1_004, 654),
            FloatingPetInteraction.ClampToWorkArea(2_000, 1_000, 96, 96, area));
    }
}
