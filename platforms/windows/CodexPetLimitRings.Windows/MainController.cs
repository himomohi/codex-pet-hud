using System.Drawing;
using System.Text.Json;
using System.Windows.Threading;
using CodexPetLimitRings.Windows.Interop;
using CodexPetLimitRings.Windows.Services;
using CodexPetLimitRings.Windows.Views;
using Forms = System.Windows.Forms;

namespace CodexPetLimitRings.Windows;

public sealed class MainController : IDisposable
{
    private readonly SettingsStore _store = new();
    private readonly CodexPetStateReader _stateReader = new();
    private readonly UsageService _usageService = new();
    private readonly CacheMaintenanceService _cacheService;
    private readonly ThresholdAlertService _alertService;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly PotionWindow _primaryPotion = new("5H", System.Windows.Media.Color.FromRgb(56, 4, 6), System.Windows.Media.Color.FromRgb(184, 9, 14), System.Windows.Media.Color.FromRgb(255, 61, 20), System.Windows.Media.Color.FromRgb(255, 107, 31));
    private readonly PotionWindow _secondaryPotion = new("WK", System.Windows.Media.Color.FromRgb(6, 18, 61), System.Windows.Media.Color.FromRgb(10, 82, 194), System.Windows.Media.Color.FromRgb(20, 199, 235), System.Windows.Media.Color.FromRgb(46, 224, 255));
    private readonly PetInputProxyWindow _petInputProxy = new();
    private readonly UnifiedDragController _unifiedDrag = new();
    private readonly UsageDetailsWindow _details = new();
    private readonly SettingsWindow _settingsWindow = new();
    private readonly Forms.NotifyIcon _tray = new();
    private Icon? _trayIcon;
    private OverlaySettings _settings;
    private UsageSnapshot _usage = UsageSnapshot.Empty;
    private PetAnchor? _anchor;
    private DateTimeOffset _lastUsageAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastUsageSuccess = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAnchorDiagnostic = DateTimeOffset.MinValue;
    private CancellationTokenSource? _usageCancellation;
    private string? _verificationCapturePath;
    private bool _refreshing;
    private bool _petVisible;
    private long _visibilityGeneration;
    private DragPreview? _dragPreview;

    public MainController()
    {
        _settings = _store.LoadSettings();
        _cacheService = new CacheMaintenanceService(_store);
        _alertService = new ThresholdAlertService(_store);
    }

    public void Start(bool showSettings = false, string? verificationCapturePath = null)
    {
        _verificationCapturePath = verificationCapturePath;
        AppLog.Write($"HUD state source: {_stateReader.StatePath}");
        ConfigureTray();
        _primaryPotion.PotionClicked += () => ShowDetails(_primaryPotion);
        _secondaryPotion.PotionClicked += () => ShowDetails(_secondaryPotion);
        _petInputProxy.PointerPressed += point => BeginUnifiedInteraction(UnifiedDragSurface.Pet, point);
        _petInputProxy.PointerMoved += ContinueUnifiedInteraction;
        _petInputProxy.PointerReleased += EndUnifiedInteraction;
        _petInputProxy.PointerCancelled += CancelUnifiedInteraction;
        _petInputProxy.HoverMoved += ForwardPetHover;
        BindPotionDrag(_primaryPotion);
        BindPotionDrag(_secondaryPotion);
        _unifiedDrag.DragStarted += BeginDragPreview;
        _unifiedDrag.DragMoved += MoveDragPreview;
        _unifiedDrag.DragFinished += EndDragPreview;
        _unifiedDrag.StatusChanged += AppLog.Write;
        _details.RefreshRequested += () => _ = RefreshUsageAsync(force: true);
        _settingsWindow.SettingsChanged += ApplySettings;
        _settingsWindow.CleanupRequested += RunCleanup;
        _settingsWindow.Apply(_settings);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
        if (showSettings) System.Windows.Application.Current.Dispatcher.BeginInvoke(ShowSettings);
    }

    private void Tick()
    {
        try
        {
            RunScheduledCleanupIfNeeded();
            if (_unifiedDrag.IsDragging) return;
            var candidate = _stateReader.ReadVisibleCandidate();
            var anchor = NativeMethods.FindVisiblePetAnchor(candidate, out var anchorDiagnostic);
            if (_dragPreview is not null)
            {
                var settled =
                    anchor is not null &&
                    (Math.Abs(anchor.X - _dragPreview.Anchor.X) > 0.5 ||
                     Math.Abs(anchor.Y - _dragPreview.Anchor.Y) > 0.5);
                if (settled)
                {
                    AppLog.Write(
                        $"Unified drag settled: anchor={anchor!.X:0.##},{anchor.Y:0.##}, " +
                        $"expected={_dragPreview.ExpectedAnchorX:0.##},{_dragPreview.ExpectedAnchorY:0.##}.");
                    _dragPreview = null;
                }
                else if (DateTimeOffset.Now < _dragPreview.HoldUntil)
                {
                    return;
                }
                else
                {
                    AppLog.Write(
                        $"Unified drag settle timed out: expected=" +
                        $"{_dragPreview.ExpectedAnchorX:0.##},{_dragPreview.ExpectedAnchorY:0.##}; " +
                        $"actual={(anchor is null ? "unavailable" : $"{anchor.X:0.##},{anchor.Y:0.##}")}.");
                    _dragPreview = null;
                }
            }
            if (anchor is null)
            {
                if (DateTimeOffset.Now - _lastAnchorDiagnostic >= TimeSpan.FromSeconds(30))
                {
                    var state = candidate is null
                        ? "no visible pet candidate"
                        : $"candidate x={candidate.WindowX:0.##}, y={candidate.WindowY:0.##}, direct={candidate.DirectCoordinates}";
                    AppLog.Write($"Pet anchor unavailable: {state}; {anchorDiagnostic}.");
                    _lastAnchorDiagnostic = DateTimeOffset.Now;
                }
                HideHud();
                return;
            }

            var justShown = !_petVisible;
            if (justShown)
            {
                AppLog.Write(
                    $"Pet anchor acquired: x={anchor.X:0.##}, y={anchor.Y:0.##}, " +
                    $"size={anchor.Width:0.##}x{anchor.Height:0.##}.");
            }
            _petVisible = true;
            _anchor = anchor;
            PlacePotions(anchor);
            PlacePetInputProxy(anchor);
            if (!_primaryPotion.IsVisible) _primaryPotion.Show();
            if (!_secondaryPotion.IsVisible) _secondaryPotion.Show();
            UpdatePotionInputRouting(anchor);
            if (!_petInputProxy.IsVisible)
            {
                _petInputProxy.Show();
                NativeMethods.ConfigurePetInputProxy(_petInputProxy);
            }
            if (_petInputProxy.IsVisible) NativeMethods.PlacePetInputProxy(_petInputProxy);
            if (justShown || DateTimeOffset.Now - _lastUsageAttempt >= TimeSpan.FromSeconds(30))
            {
                _ = RefreshUsageAsync(force: justShown);
            }
        }
        catch (Exception error)
        {
            AppLog.Write($"Tick failed: {error.GetType().Name}: {error.Message}");
            HideHud();
        }
    }

    private void HideHud()
    {
        if (_petVisible)
        {
            AppLog.Write("Pet anchor lost; potion HUD hidden.");
            _visibilityGeneration++;
            _usageCancellation?.Cancel();
        }
        _petVisible = false;
        _unifiedDrag.Cancel();
        _dragPreview = null;
        _anchor = null;
        _petInputProxy.Hide();
        _primaryPotion.Hide();
        _secondaryPotion.Hide();
        _details.Hide();
        UpdateTrayText();
    }

    private void PlacePotions(PetAnchor anchor)
    {
        var placement = HudLayout.Calculate(anchor, _settings);
        _primaryPotion.ApplyScale(placement.Scale);
        _secondaryPotion.ApplyScale(placement.Scale);
        _primaryPotion.Left = placement.PrimaryX;
        _primaryPotion.Top = placement.Y;
        _secondaryPotion.Left = placement.SecondaryX;
        _secondaryPotion.Top = placement.Y;
    }

    private void PlacePetInputProxy(PetAnchor anchor)
    {
        _petInputProxy.Apply(PetInputProxyLayout.Calculate(anchor));
    }

    private void BindPotionDrag(PotionWindow potion)
    {
        potion.PointerPressed += point => BeginUnifiedInteraction(UnifiedDragSurface.Potion, point);
        potion.PointerMoved += ContinueUnifiedInteraction;
        potion.PointerReleased += EndUnifiedInteraction;
        potion.PointerCancelled += CancelUnifiedInteraction;
    }

    private void BeginUnifiedInteraction(UnifiedDragSurface surface, ScreenPointer pointer)
    {
        if (!_petVisible ||
            _anchor is null ||
            !NativeMethods.TryGetPhysicalWindowRect(_petInputProxy, out var petBounds))
        {
            return;
        }
        _unifiedDrag.Press(surface, pointer, _anchor, petBounds);
    }

    private void ContinueUnifiedInteraction(ScreenPointer pointer) =>
        _unifiedDrag.Move(pointer);

    private void EndUnifiedInteraction(ScreenPointer pointer) =>
        _unifiedDrag.Release(pointer);

    private void CancelUnifiedInteraction() =>
        _unifiedDrag.Cancel();

    private void BeginDragPreview(PetAnchor anchor)
    {
        _details.Hide();
        _dragPreview = new DragPreview(
            anchor,
            _primaryPotion.Left,
            _primaryPotion.Top,
            _secondaryPotion.Left,
            _secondaryPotion.Top,
            _petInputProxy.Left,
            _petInputProxy.Top,
            anchor.X,
            anchor.Y,
            DateTimeOffset.MaxValue);
    }

    private void MoveDragPreview(double deltaX, double deltaY)
    {
        var preview = _dragPreview;
        if (preview is null) return;
        _primaryPotion.Left = preview.PrimaryLeft + deltaX;
        _primaryPotion.Top = preview.PrimaryTop + deltaY;
        _secondaryPotion.Left = preview.SecondaryLeft + deltaX;
        _secondaryPotion.Top = preview.SecondaryTop + deltaY;
        _petInputProxy.Left = preview.ProxyLeft + deltaX;
        _petInputProxy.Top = preview.ProxyTop + deltaY;
    }

    private void EndDragPreview(
        PetAnchor anchor,
        double deltaX,
        double deltaY,
        bool delivered)
    {
        var preview = _dragPreview;
        if (preview is null) return;
        if (!delivered)
        {
            _dragPreview = null;
            Tick();
            return;
        }
        MoveDragPreview(deltaX, deltaY);
        _dragPreview = preview with
        {
            ExpectedAnchorX = anchor.X + deltaX,
            ExpectedAnchorY = anchor.Y + deltaY,
            HoldUntil = DateTimeOffset.Now + TimeSpan.FromSeconds(2)
        };
    }

    private void ForwardPetHover()
    {
        if (!_petVisible || _anchor is null || _unifiedDrag.IsPressed) return;
        NativeMethods.ForwardPetHover(_anchor.NativeWindowHandle);
    }

    private void UpdatePotionInputRouting(PetAnchor anchor)
    {
        NativeMethods.PlacePotionWindow(
            _primaryPotion,
            anchor.NativeWindowHandle,
            directPotionClicksEnabled: true);
        NativeMethods.PlacePotionWindow(
            _secondaryPotion,
            anchor.NativeWindowHandle,
            directPotionClicksEnabled: true);
    }

    private async Task RefreshUsageAsync(bool force)
    {
        if (!_petVisible || _refreshing) return;
        if (!force && DateTimeOffset.Now - _lastUsageAttempt < TimeSpan.FromSeconds(30)) return;
        var generation = _visibilityGeneration;
        using var cancellation = new CancellationTokenSource();
        _usageCancellation = cancellation;
        _refreshing = true;
        _lastUsageAttempt = DateTimeOffset.Now;
        _details.Update(_usage, true);
        try
        {
            var refreshed = await _usageService.RefreshAsync(cancellation.Token);
            if (refreshed is not null && _petVisible && generation == _visibilityGeneration)
            {
                _usage = refreshed;
                _lastUsageSuccess = DateTimeOffset.Now;
                _primaryPotion.UpdateUsage(_usage.PrimaryRemaining, _usage.PrimaryReset, _usage.Source);
                _secondaryPotion.UpdateUsage(_usage.SecondaryRemaining, _usage.SecondaryReset, _usage.Source);
                AppLog.Write(
                    $"Usage refreshed: 5H={FormatUsageForLog(_usage.PrimaryRemaining)}, " +
                    $"WK={FormatUsageForLog(_usage.SecondaryRemaining)}, source={_usage.Source}.");
                if (_verificationCapturePath is { } capturePath)
                {
                    _verificationCapturePath = null;
                    _ = CaptureVerificationAsync(capturePath);
                }
                foreach (var alert in _alertService.Evaluate(_usage, _settings)) ShowAlert(alert);
            }
            else if (refreshed is null)
            {
                MarkUsageStaleIfNeeded();
            }
        }
        catch (HttpRequestException error) { AppLog.Write($"Usage request failed: {error.Message}"); MarkUsageStaleIfNeeded(); }
        catch (TaskCanceledException) { }
        catch (JsonException error) { AppLog.Write($"Usage JSON failed: {error.Message}"); MarkUsageStaleIfNeeded(); }
        catch (InvalidOperationException error) { AppLog.Write($"Usage payload failed: {error.Message}"); MarkUsageStaleIfNeeded(); }
        finally
        {
            _refreshing = false;
            if (generation != _visibilityGeneration) _lastUsageAttempt = DateTimeOffset.MinValue;
            if (ReferenceEquals(_usageCancellation, cancellation)) _usageCancellation = null;
            _details.Update(_usage, false);
            UpdateTrayText();
        }
    }

    private void MarkUsageStaleIfNeeded()
    {
        if (_usage.Source == "none" ||
            _lastUsageSuccess == DateTimeOffset.MinValue ||
            DateTimeOffset.Now - _lastUsageSuccess < TimeSpan.FromMinutes(2) ||
            _usage.Source == "stale") return;
        _usage = _usage with { Source = "stale" };
        _primaryPotion.UpdateUsage(_usage.PrimaryRemaining, _usage.PrimaryReset, _usage.Source);
        _secondaryPotion.UpdateUsage(_usage.SecondaryRemaining, _usage.SecondaryReset, _usage.Source);
        AppLog.Write("Usage data marked stale after repeated refresh failures.");
    }

    private void ShowDetails(PotionWindow source)
    {
        if (!_petVisible || _anchor is null) return;
        _details.Update(_usage, _refreshing);
        PositionDetails(source, _details.MinHeight);
        if (!_details.IsVisible) _details.Show();
        _details.UpdateLayout();
        PositionDetails(source, _details.ActualHeight);
        _details.Activate();
    }

    private void PositionDetails(PotionWindow source, double detailsHeight)
    {
        if (_anchor is null) return;
        var detailsWidth = double.IsNaN(_details.Width) ? _details.ActualWidth : _details.Width;
        var preferredLeft = source == _primaryPotion
            ? source.Left - detailsWidth - 12
            : source.Left + source.Width + 12;
        _details.Left = Math.Clamp(preferredLeft, _anchor.WorkX, Math.Max(_anchor.WorkX, _anchor.WorkRight - detailsWidth));
        _details.Top = Math.Clamp(source.Top + (source.Height - detailsHeight) / 2, _anchor.WorkY, Math.Max(_anchor.WorkY, _anchor.WorkBottom - detailsHeight));
    }

    private void ShowSettings()
    {
        _settingsWindow.Apply(_settings);
        if (!_settingsWindow.IsVisible) _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplySettings(OverlaySettings settings)
    {
        settings.Normalize();
        _settings = settings;
        _store.SaveSettings(_settings);
        if (_anchor is not null) PlacePotions(_anchor);
    }

    private void RunScheduledCleanupIfNeeded()
    {
        if (!_settings.AutoCleanup) return;
        var last = _settings.LastCleanupAt is null ? DateTimeOffset.MinValue : DateTimeOffset.FromUnixTimeSeconds(_settings.LastCleanupAt.Value);
        if (DateTimeOffset.Now - last >= TimeSpan.FromDays(1)) RunCleanup();
    }

    private void RunCleanup()
    {
        _settings.LastFreedBytes = _cacheService.Clean();
        _settings.LastCleanupAt = DateTimeOffset.Now.ToUnixTimeSeconds();
        _store.SaveSettings(_settings);
        _settingsWindow.Apply(_settings);
    }

    private void ShowAlert(ThresholdAlert alert)
    {
        if (!_settings.NativeNotificationsEnabled) return;
        _tray.BalloonTipTitle = alert.Title;
        _tray.BalloonTipText = alert.Body;
        _tray.BalloonTipIcon = alert.Threshold <= 5 ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info;
        _tray.ShowBalloonTip(5000);
    }

    private void ConfigureTray()
    {
        _trayIcon = string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? null
            : Icon.ExtractAssociatedIcon(Environment.ProcessPath);
        _tray.Icon = _trayIcon ?? SystemIcons.Application;
        _tray.Text = "Codex 포션 HUD";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowSettings();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("사용량 지금 갱신", null, (_, _) => _ = RefreshUsageAsync(force: true));
        menu.Items.Add("포션 상세 보기", null, (_, _) => ShowDetails(_primaryPotion));
        menu.Items.Add("세부 설정…", null, (_, _) => ShowSettings());
        menu.Items.Add("오버레이 위치 초기화", null, (_, _) => { _settings.HorizontalOffset = 0; _settings.VerticalOffset = 0; ApplySettings(_settings); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("캐시 지금 정리", null, (_, _) => RunCleanup());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => System.Windows.Application.Current.Shutdown());
        _tray.ContextMenuStrip = menu;
    }

    private void UpdateTrayText()
    {
        if (!_petVisible) { _tray.Text = "Codex 포션 HUD · 펫 숨김"; return; }
        var primary = _usage.PrimaryRemaining is null ? "--" : $"{Math.Round(_usage.PrimaryRemaining.Value):0}%";
        var weekly = _usage.SecondaryRemaining is null ? "--" : $"{Math.Round(_usage.SecondaryRemaining.Value):0}%";
        var state = _usage.Source == "stale" ? " · 갱신 지연" : string.Empty;
        _tray.Text = $"Codex 포션 HUD · 5H {primary} · WK {weekly}{state}";
    }

    private static string FormatUsageForLog(double? remaining) =>
        remaining is null
            ? "--"
            : $"{Math.Round(remaining.Value, 2):0.##}%";

    private async Task CaptureVerificationAsync(string path)
    {
        await Task.Delay(750);
        try
        {
            if (!_petVisible || _anchor is null)
            {
                AppLog.Write("Verification capture skipped: pet anchor is no longer visible.");
                return;
            }
            _primaryPotion.UpdateLayout();
            _secondaryPotion.UpdateLayout();
            if (!NativeMethods.TryGetPhysicalWindowRect(_primaryPotion, out var primaryBounds) ||
                !NativeMethods.TryGetPhysicalWindowRect(_secondaryPotion, out var secondaryBounds) ||
                _primaryPotion.ActualWidth <= 0 ||
                _primaryPotion.ActualHeight <= 0)
            {
                AppLog.Write("Verification capture skipped: potion window bounds unavailable.");
                return;
            }

            var scaleX = primaryBounds.Width / _primaryPotion.ActualWidth;
            var scaleY = primaryBounds.Height / _primaryPotion.ActualHeight;
            var anchorBounds = new Rectangle(
                primaryBounds.Left + (int)Math.Round((_anchor.X - _primaryPotion.Left) * scaleX),
                primaryBounds.Top + (int)Math.Round((_anchor.Y - _primaryPotion.Top) * scaleY),
                Math.Max(1, (int)Math.Round(_anchor.Width * scaleX)),
                Math.Max(1, (int)Math.Round(_anchor.Height * scaleY)));
            var captureBounds = Rectangle.Union(Rectangle.Union(primaryBounds, secondaryBounds), anchorBounds);
            captureBounds.Inflate(72, 72);
            captureBounds = Rectangle.Intersect(captureBounds, Forms.SystemInformation.VirtualScreen);
            if (captureBounds.Width <= 0 || captureBounds.Height <= 0)
            {
                AppLog.Write("Verification capture skipped: calculated capture area is empty.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var bitmap = new Bitmap(captureBounds.Width, captureBounds.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(captureBounds.Left, captureBounds.Top, 0, 0, captureBounds.Size);
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            AppLog.Write(
                $"Verification capture saved: {path}; area={captureBounds.Left},{captureBounds.Top}," +
                $"{captureBounds.Width}x{captureBounds.Height}.");
        }
        catch (Exception error)
        {
            AppLog.Write($"Verification capture failed: {error.GetType().Name}: {error.Message}");
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _petVisible = false;
        _usageCancellation?.Cancel();
        _unifiedDrag.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _trayIcon?.Dispose();
        _usageService.Dispose();
        _petInputProxy.Close();
        _primaryPotion.Close();
        _secondaryPotion.Close();
        _details.ClosePermanently();
        _settingsWindow.ClosePermanently();
    }

    private sealed record DragPreview(
        PetAnchor Anchor,
        double PrimaryLeft,
        double PrimaryTop,
        double SecondaryLeft,
        double SecondaryTop,
        double ProxyLeft,
        double ProxyTop,
        double ExpectedAnchorX,
        double ExpectedAnchorY,
        DateTimeOffset HoldUntil);
}
