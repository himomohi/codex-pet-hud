using System.Text.Json.Serialization;

namespace CodexPetLimitRings.Windows;

public sealed class OverlaySettings
{
    public double Scale { get; set; } = 1;
    public double HorizontalOffset { get; set; }
    public double VerticalOffset { get; set; }
    public double PotionGap { get; set; } = 10;
    public bool UsageAlertsEnabled { get; set; } = true;
    public bool NativeNotificationsEnabled { get; set; }
    public int[] AlertThresholds { get; set; } = [20, 10, 5];
    public bool AutoCleanup { get; set; } = true;
    public long? LastCleanupAt { get; set; }
    public long LastFreedBytes { get; set; }

    public void Normalize()
    {
        if (!double.IsFinite(Scale)) Scale = 1;
        if (!double.IsFinite(HorizontalOffset)) HorizontalOffset = 0;
        if (!double.IsFinite(VerticalOffset)) VerticalOffset = 0;
        if (!double.IsFinite(PotionGap)) PotionGap = 10;
        Scale = Math.Clamp(Scale, 0.5, 1.8);
        HorizontalOffset = Math.Clamp(HorizontalOffset, -400, 400);
        VerticalOffset = Math.Clamp(VerticalOffset, -300, 300);
        PotionGap = Math.Clamp(PotionGap, 0, 160);
        AlertThresholds = (AlertThresholds ?? []).Where(value => value is 20 or 10 or 5).Distinct().OrderByDescending(value => value).ToArray();
        if (LastCleanupAt is { } timestamp)
        {
            try { _ = DateTimeOffset.FromUnixTimeSeconds(timestamp); }
            catch (ArgumentOutOfRangeException) { LastCleanupAt = null; }
        }
        LastFreedBytes = Math.Max(0, LastFreedBytes);
    }
}

public sealed record PetAnchor(double X, double Y, double Width, double Height, string? DisplayId)
{
    public double Right => X + Width;
    public double CenterY => Y + Height / 2;
}

public sealed record PetWindowCandidate(
    double WindowX,
    double WindowY,
    double WindowWidth,
    double WindowHeight,
    double MascotLeft,
    double MascotTop,
    double MascotWidth,
    double MascotHeight,
    string? DisplayId);

public sealed record UsageSnapshot(
    double? PrimaryUsed,
    double? SecondaryUsed,
    long? PrimaryReset,
    long? SecondaryReset,
    string Source,
    DateTimeOffset ReadAt)
{
    public static UsageSnapshot Empty { get; } = new(null, null, null, null, "none", DateTimeOffset.MinValue);
    public double? PrimaryRemaining => PrimaryUsed is null || !double.IsFinite(PrimaryUsed.Value) ? null : Math.Clamp(100 - PrimaryUsed.Value, 0, 100);
    public double? SecondaryRemaining => SecondaryUsed is null || !double.IsFinite(SecondaryUsed.Value) ? null : Math.Clamp(100 - SecondaryUsed.Value, 0, 100);
}

public sealed class ThresholdWindowState
{
    public long? ResetAt { get; set; }
    public List<int> Delivered { get; set; } = [];

    public void Normalize() => Delivered ??= [];
}

public sealed class AlertDeliveryState
{
    public ThresholdWindowState Primary { get; set; } = new();
    public ThresholdWindowState Secondary { get; set; } = new();

    public void Normalize()
    {
        Primary ??= new ThresholdWindowState();
        Secondary ??= new ThresholdWindowState();
        Primary.Normalize();
        Secondary.Normalize();
    }
}

public sealed record ThresholdAlert(string Title, string Body, int Threshold);
