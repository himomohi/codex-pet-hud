namespace CodexPetLimitRings.Windows.Services;

public sealed class ThresholdAlertService(SettingsStore store)
{
    private readonly AlertDeliveryState _state = store.LoadAlertState();

    public IReadOnlyList<ThresholdAlert> Evaluate(UsageSnapshot usage, OverlaySettings settings)
    {
        if (!settings.UsageAlertsEnabled || settings.AlertThresholds.Length == 0) return [];
        var alerts = new List<ThresholdAlert>();
        EvaluateWindow("5시간 포션", usage.PrimaryRemaining, usage.PrimaryReset, settings, _state.Primary, alerts);
        EvaluateWindow("주간 포션", usage.SecondaryRemaining, usage.SecondaryReset, settings, _state.Secondary, alerts);
        store.SaveAlertState(_state);
        return alerts;
    }

    private static void EvaluateWindow(
        string label,
        double? remaining,
        long? resetAt,
        OverlaySettings settings,
        ThresholdWindowState state,
        List<ThresholdAlert> output)
    {
        if (remaining is null || resetAt is null || resetAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return;
        if (state.ResetAt != resetAt)
        {
            state.ResetAt = resetAt;
            state.Delivered.Clear();
        }
        var crossed = settings.AlertThresholds
            .Where(value => remaining <= value && !state.Delivered.Contains(value))
            .OrderBy(value => value)
            .ToArray();
        if (crossed.Length == 0) return;
        var urgent = crossed[0];
        state.Delivered = state.Delivered
            .Concat(settings.AlertThresholds.Where(value => remaining <= value))
            .Distinct()
            .OrderByDescending(value => value)
            .ToList();
        output.Add(new ThresholdAlert(
            $"{label} {urgent}% 이하",
            $"현재 {Math.Max(0, Math.Round(remaining.Value))}% 남았어요.",
            urgent));
    }
}
