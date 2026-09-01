namespace PokeTokenBar.Core;

public readonly record struct FloatingPetBounds(
    double Left,
    double Top,
    double Right,
    double Bottom);

public readonly record struct FloatingPetPosition(double Left, double Top);

public static class FloatingPetInteraction
{
    public const double ClickThresholdSquared = 16;

    public static bool IsClick(
        double startLeft,
        double startTop,
        double endLeft,
        double endTop)
    {
        var dx = endLeft - startLeft;
        var dy = endTop - startTop;
        return dx * dx + dy * dy < ClickThresholdSquared;
    }

    public static FloatingPetPosition ClampToWorkArea(
        double left,
        double top,
        double width,
        double height,
        FloatingPetBounds workArea) =>
        new(
            Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width)),
            Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height)));
}
