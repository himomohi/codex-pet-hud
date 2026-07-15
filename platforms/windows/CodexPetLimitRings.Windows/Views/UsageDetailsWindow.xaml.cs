using System.Windows;

namespace CodexPetLimitRings.Windows.Views;

public partial class UsageDetailsWindow : Window
{
    private bool _allowClose;
    public event Action? RefreshRequested;

    public UsageDetailsWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
    }

    public void Update(UsageSnapshot usage, bool refreshing)
    {
        PrimaryValue.Text = FormatPercent(usage.PrimaryRemaining);
        SecondaryValue.Text = FormatPercent(usage.SecondaryRemaining);
        PrimaryReset.Text = $"초기화 {FormatReset(usage.PrimaryReset)}";
        SecondaryReset.Text = $"초기화 {FormatReset(usage.SecondaryReset)}";
        SourceText.Text = usage.Source switch
        {
            "live" => $"Live · {usage.ReadAt.LocalDateTime:t}",
            "stale" => $"최근 데이터 · {usage.ReadAt.LocalDateTime:t} · 갱신 지연",
            _ => "데이터 대기"
        };
        RefreshButton.Content = refreshing ? "갱신 중…" : "지금 갱신";
        RefreshButton.IsEnabled = !refreshing;
    }

    private static string FormatPercent(double? value) => value is null ? "데이터 없음" : $"{Math.Clamp(Math.Round(value.Value), 0, 100):0}% 남음";
    private static string FormatReset(long? timestamp)
    {
        if (timestamp is null) return "—";
        try { return DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).LocalDateTime.ToString("M월 d일 tt h:mm"); }
        catch (ArgumentOutOfRangeException) { return "—"; }
    }
    private void RefreshButton_OnClick(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
    public void ClosePermanently() { _allowClose = true; Close(); }
    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) { if (!_allowClose) { e.Cancel = true; Hide(); } }
}
