using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CodexPetLimitRings.Windows.Interop;

namespace CodexPetLimitRings.Windows.Views;

public partial class PotionWindow : Window
{
    public event Action? PotionClicked;

    public PotionWindow(string label, System.Windows.Media.Color top, System.Windows.Media.Color bottom)
    {
        InitializeComponent();
        LabelText.Text = label;
        Liquid.Fill = new LinearGradientBrush(top, bottom, 90);
        SourceInitialized += (_, _) => NativeMethods.MakeNoActivate(this);
    }

    public void UpdateUsage(double? remaining, long? resetAt, string source)
    {
        PercentText.Text = remaining is null ? "—" : $"{Math.Clamp(Math.Round(remaining.Value), 0, 100):0}%";
        Opacity = remaining is null ? 0.65 : 1;
        Root.ToolTip = string.Join(Environment.NewLine,
            remaining is null ? "사용량 데이터 없음" : $"남은 사용량 {Math.Clamp(Math.Round(remaining.Value), 0, 100):0}%",
            source == "live" ? "실시간 기준" : source == "none" ? "데이터 대기" : "최근 기록 기준",
            $"초기화까지 {FormatResetRemaining(resetAt)}");
    }

    private static string FormatResetRemaining(long? resetAt)
    {
        if (resetAt is null) return "확인 중";
        var remaining = DateTimeOffset.FromUnixTimeSeconds(resetAt.Value) - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) return "곧 초기화";
        if (remaining.TotalDays >= 1) return $"{(int)remaining.TotalDays}일 {remaining.Hours}시간";
        if (remaining.TotalHours >= 1) return $"{(int)remaining.TotalHours}시간 {remaining.Minutes}분";
        return $"{Math.Max(1, remaining.Minutes)}분";
    }

    public void ApplyScale(double scale)
    {
        Root.LayoutTransform = new ScaleTransform(scale, scale);
        Width = 92 * scale;
        Height = 110 * scale;
    }

    private void Root_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => PotionClicked?.Invoke();
}
