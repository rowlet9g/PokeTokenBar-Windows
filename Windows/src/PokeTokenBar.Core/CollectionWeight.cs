namespace PokeTokenBar.Core;

public static class CollectionWeight
{
    // Match upstream integer division; even completed species remain selectable.
    public static int Adjusted(int weight, bool isCollected) =>
        Math.Max(1, isCollected ? weight / 2 : weight);
}
