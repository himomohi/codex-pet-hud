namespace CodexPetLimitRings.Windows;

public readonly record struct ScreenPointer(double X, double Y);

public static class UnifiedDragPolicy
{
    public const double ActivationDistance = 4;

    public static bool IsDrag(ScreenPointer start, ScreenPointer current)
    {
        var x = current.X - start.X;
        var y = current.Y - start.Y;
        return x * x + y * y >= ActivationDistance * ActivationDistance;
    }

    public static ScreenPointer ToVirtualPointer(
        ScreenPointer physicalStart,
        ScreenPointer virtualStart,
        ScreenPointer physicalCurrent) =>
        new(
            virtualStart.X + physicalCurrent.X - physicalStart.X,
            virtualStart.Y + physicalCurrent.Y - physicalStart.Y);

    public static ScreenPointer ToDipDelta(
        ScreenPointer start,
        ScreenPointer current,
        double dpiScale)
    {
        var safeScale = double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1;
        return new(
            (current.X - start.X) / safeScale,
            (current.Y - start.Y) / safeScale);
    }
}
