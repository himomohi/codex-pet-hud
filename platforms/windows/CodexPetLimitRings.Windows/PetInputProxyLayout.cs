namespace CodexPetLimitRings.Windows;

public readonly record struct PetInputProxyPlacement(
    double X,
    double Y,
    double Width,
    double Height);

public static class PetInputProxyLayout
{
    public static PetInputProxyPlacement Calculate(PetAnchor anchor) =>
        new(
            anchor.X,
            anchor.Y,
            Math.Max(1, anchor.Width),
            Math.Max(1, anchor.Height));
}
