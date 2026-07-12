using System.Windows;

namespace CodexPetLimitRings.Windows.Views;

public partial class UsageDetailsWindow : Window
{
    private bool _allowClose;
    public event Action? RefreshRequested;

    public UsageDetailsWindow() => InitializeComponent();

    public void Update(UsageSnapshot usage, bool refreshing)
    {
        PrimaryValue.Text = FormatPercent(usage.PrimaryRemaining);
        SecondaryValue.Text = FormatPercent(usage.SecondaryRemaining);
        PrimaryReset.Text = $"초기화 {FormatReset(usage.PrimaryReset)}";
        SecondaryReset.Text = $"초기화 {FormatReset(usage.SecondaryReset)}";
        SourceText.Text = usage.Source == "live" ? $"Live · {usage.ReadAt.LocalDateTime:t}" : "데이터 대기";
        RefreshButton.Content = refreshing ? "갱신 중…" : "지금 갱신";
        RefreshButton.IsEnabled = !refreshing;
    }

    private static string FormatPercent(double? value) => value is null ? "데이터 없음" : $"{Math.Clamp(Math.Round(value.Value), 0, 100):0}% 남음";
    private static string FormatReset(long? timestamp) => timestamp is null ? "—" : DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).LocalDateTime.ToString("M월 d일 tt h:mm");
    private void RefreshButton_OnClick(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
    public void ClosePermanently() { _allowClose = true; Close(); }
    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) { if (!_allowClose) { e.Cancel = true; Hide(); } }
}
