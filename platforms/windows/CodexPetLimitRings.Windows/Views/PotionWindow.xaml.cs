using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodexPetLimitRings.Windows.Interop;
using InputMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace CodexPetLimitRings.Windows.Views;

public partial class PotionWindow : Window
{
    private const double GlassSize = 67;
    private readonly string _accessibleLabel;
    private double _appliedScale = double.NaN;
    private bool _pointerPressed;
    private bool _pointerMoved;
    private ScreenPointer _pressPointer;
    public event Action? PotionClicked;
    internal event Action<ScreenPointer>? PointerPressed;
    internal event Action<ScreenPointer>? PointerMoved;
    internal event Action<ScreenPointer>? PointerReleased;
    internal event Action? PointerCancelled;

    public PotionWindow(string label, System.Windows.Media.Color dark, System.Windows.Media.Color mid, System.Windows.Media.Color bright, System.Windows.Media.Color surface)
    {
        InitializeComponent();
        LabelText.Text = label;
        _accessibleLabel = label == "5H" ? "5시간 포션" : "주간 포션";
        LiquidDark.Color = dark;
        LiquidMid.Color = mid;
        LiquidBright.Color = bright;
        LiquidSurface.Fill = new SolidColorBrush(surface);
        AutomationProperties.SetName(this, _accessibleLabel);
        AutomationProperties.SetName(Root, _accessibleLabel);
        Root.IsHitTestVisible = true;
        SourceInitialized += (_, _) =>
        {
            NativeMethods.ConfigurePotionInput(this, directPotionClicksEnabled: true);
        };
        PreviewMouseLeftButtonDown += OnMouseLeftButtonDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseLeftButtonUp += OnMouseLeftButtonUp;
        LostMouseCapture += OnLostMouseCapture;
    }

    public void UpdateUsage(double? remaining, long? resetAt, string source)
    {
        var percent = remaining is null ? 0 : Math.Clamp(remaining.Value, 0, 100);
        PercentText.Text = remaining is null ? "—" : $"{Math.Round(percent):0}%";
        var height = GlassSize * percent / 100;
        Liquid.Height = height;
        Canvas.SetTop(Liquid, GlassSize - height);
        var hasLiquid = remaining is not null && height > 0.5;
        Liquid.Visibility = hasLiquid ? Visibility.Visible : Visibility.Hidden;
        LiquidSurface.Visibility = hasLiquid && percent < 99.5 ? Visibility.Visible : Visibility.Hidden;
        LiquidBubble.Visibility = hasLiquid ? Visibility.Visible : Visibility.Hidden;
        Canvas.SetTop(LiquidSurface, GlassSize - height - 2.5);
        Canvas.SetTop(LiquidBubble, Math.Max(GlassSize - height + 4, GlassSize * 0.72));
        var resetText = FormatResetRemaining(resetAt);
        var usageText = remaining is null ? "사용량 데이터 없음" : $"남은 사용량 {Math.Round(percent):0}%";
        var accessibleText = $"{_accessibleLabel}, {usageText}, 초기화까지 {resetText}";
        AutomationProperties.SetName(this, accessibleText);
        AutomationProperties.SetName(Root, accessibleText);
        Root.ToolTip = string.Join(Environment.NewLine,
            usageText,
            source == "live" ? "실시간 기준" : source == "none" ? "데이터 대기" : "최근 기록 기준",
            $"초기화까지 {resetText}");
    }

    private static string FormatResetRemaining(long? resetAt)
    {
        if (resetAt is null) return "확인 중";
        TimeSpan remaining;
        try { remaining = DateTimeOffset.FromUnixTimeSeconds(resetAt.Value) - DateTimeOffset.UtcNow; }
        catch (ArgumentOutOfRangeException) { return "확인 중"; }
        if (remaining <= TimeSpan.Zero) return "곧 초기화";
        if (remaining.TotalDays >= 1) return $"{(int)remaining.TotalDays}일 {remaining.Hours}시간";
        if (remaining.TotalHours >= 1) return $"{(int)remaining.TotalHours}시간 {remaining.Minutes}분";
        return $"{Math.Max(1, remaining.Minutes)}분";
    }

    public void ApplyScale(double scale)
    {
        if (double.IsFinite(_appliedScale) && Math.Abs(_appliedScale - scale) < 0.0001) return;
        _appliedScale = scale;
        var typeScale = Math.Clamp(scale, 0.75, 1.5);
        var labelScale = Math.Clamp(scale, 0.9, 1.5);
        Width = 92 * scale;
        Height = 110 * scale;
        PercentBackdrop.Width = 34 * typeScale;
        PercentBackdrop.Height = 18 * typeScale;
        PercentBackdrop.CornerRadius = new CornerRadius(3 * typeScale);
        PercentText.FontSize = 12 * typeScale;
        Canvas.SetLeft(PercentBackdrop, 46 * scale - PercentBackdrop.Width / 2);
        Canvas.SetTop(PercentBackdrop, 58 * scale - PercentBackdrop.Height / 2);
        LabelContainer.Width = 34 * labelScale;
        LabelContainer.Height = 14 * labelScale;
        LabelContainer.CornerRadius = new CornerRadius(3 * labelScale);
        LabelText.FontSize = 10 * labelScale;
        Canvas.SetLeft(LabelContainer, 46 * scale - LabelContainer.Width / 2);
        Canvas.SetTop(LabelContainer, 93 * scale - LabelContainer.Height / 2);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left) return;
        eventArgs.Handled = true;
        _pointerPressed = true;
        _pointerMoved = false;
        _pressPointer = ToScreenPointer(eventArgs);
        Mouse.Capture(this, CaptureMode.Element);
        PointerPressed?.Invoke(_pressPointer);
    }

    private void OnMouseMove(object sender, InputMouseEventArgs eventArgs)
    {
        if (!_pointerPressed) return;
        eventArgs.Handled = true;
        var pointer = ToScreenPointer(eventArgs);
        _pointerMoved |= UnifiedDragPolicy.IsDrag(_pressPointer, pointer);
        PointerMoved?.Invoke(pointer);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (!_pointerPressed || eventArgs.ChangedButton != MouseButton.Left) return;
        eventArgs.Handled = true;
        var pointer = ToScreenPointer(eventArgs);
        var clicked = !_pointerMoved;
        _pointerPressed = false;
        PointerReleased?.Invoke(pointer);
        if (IsMouseCaptured) Mouse.Capture(null);
        if (clicked) PotionClicked?.Invoke();
    }

    private void OnLostMouseCapture(object sender, InputMouseEventArgs eventArgs)
    {
        if (!_pointerPressed) return;
        _pointerPressed = false;
        PointerCancelled?.Invoke();
    }

    private ScreenPointer ToScreenPointer(InputMouseEventArgs eventArgs)
    {
        var point = PointToScreen(eventArgs.GetPosition(this));
        return new ScreenPointer(point.X, point.Y);
    }
}
