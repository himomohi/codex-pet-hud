using System.Windows;

namespace CodexPetLimitRings.Windows.Views;

public partial class SettingsWindow : Window
{
    private bool _applying;
    private bool _allowClose;
    private OverlaySettings _settings = new();
    public event Action<OverlaySettings>? SettingsChanged;
    public event Action? CleanupRequested;

    public SettingsWindow() => InitializeComponent();

    public void Apply(OverlaySettings settings)
    {
        _applying = true;
        _settings = settings;
        Scale.Value = settings.Scale;
        HorizontalOffset.Value = settings.HorizontalOffset;
        VerticalOffset.Value = settings.VerticalOffset;
        PotionGap.Value = settings.PotionGap;
        UsageAlerts.IsChecked = settings.UsageAlertsEnabled;
        NativeNotifications.IsChecked = settings.NativeNotificationsEnabled;
        Alert20.IsChecked = settings.AlertThresholds.Contains(20);
        Alert10.IsChecked = settings.AlertThresholds.Contains(10);
        Alert5.IsChecked = settings.AlertThresholds.Contains(5);
        AutoCleanup.IsChecked = settings.AutoCleanup;
        CleanupStatus.Text = settings.LastCleanupAt is null
            ? "아직 정리 기록이 없어요."
            : $"마지막 확보 {FormatBytes(settings.LastFreedBytes)}";
        _applying = false;
    }

    private void Control_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_applying || !IsLoaded) return;
        _settings.Scale = Scale.Value;
        _settings.HorizontalOffset = HorizontalOffset.Value;
        _settings.VerticalOffset = VerticalOffset.Value;
        _settings.PotionGap = PotionGap.Value;
        _settings.UsageAlertsEnabled = UsageAlerts.IsChecked == true;
        _settings.NativeNotificationsEnabled = NativeNotifications.IsChecked == true;
        _settings.AlertThresholds = new[] { (20, Alert20), (10, Alert10), (5, Alert5) }
            .Where(item => item.Item2.IsChecked == true).Select(item => item.Item1).ToArray();
        _settings.AutoCleanup = AutoCleanup.IsChecked == true;
        SettingsChanged?.Invoke(_settings);
    }

    private void ResetButton_OnClick(object sender, RoutedEventArgs e)
    {
        Apply(new OverlaySettings());
        SettingsChanged?.Invoke(_settings);
    }

    private void CleanupButton_OnClick(object sender, RoutedEventArgs e) => CleanupRequested?.Invoke();
    public void ClosePermanently() { _allowClose = true; Close(); }
    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) { if (!_allowClose) { e.Cancel = true; Hide(); } }
    private static string FormatBytes(long bytes) => bytes >= 1_048_576 ? $"{bytes / 1_048_576d:0.0} MB" : $"{bytes / 1024d:0} KB";
}
