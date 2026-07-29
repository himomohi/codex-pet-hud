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
    private readonly UsageDetailsWindow _details = new();
    private readonly SettingsWindow _settingsWindow = new();
    private readonly Forms.NotifyIcon _tray = new();
    private Icon? _trayIcon;
    private OverlaySettings _settings;
    private UsageSnapshot _usage = UsageSnapshot.Empty;
    private PetAnchor? _anchor;
    private DateTimeOffset _lastUsageAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastUsageSuccess = DateTimeOffset.MinValue;
    private CancellationTokenSource? _usageCancellation;
    private bool _refreshing;
    private bool _petVisible;
    private long _visibilityGeneration;

    public MainController()
    {
        _settings = _store.LoadSettings();
        _cacheService = new CacheMaintenanceService(_store);
        _alertService = new ThresholdAlertService(_store);
    }

    public void Start(bool showSettings = false)
    {
        ConfigureTray();
        _primaryPotion.PotionClicked += () => ShowDetails(_primaryPotion);
        _secondaryPotion.PotionClicked += () => ShowDetails(_secondaryPotion);
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
            var candidate = _stateReader.ReadVisibleCandidate();
            var anchor = NativeMethods.FindVisiblePetAnchor(candidate);
            if (anchor is null)
            {
                HideHud();
                return;
            }

            var justShown = !_petVisible;
            _petVisible = true;
            _anchor = anchor;
            PlacePotions(anchor);
            if (!_primaryPotion.IsVisible) _primaryPotion.Show();
            if (!_secondaryPotion.IsVisible) _secondaryPotion.Show();
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
            _visibilityGeneration++;
            _usageCancellation?.Cancel();
        }
        _petVisible = false;
        _anchor = null;
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

    public void Dispose()
    {
        _timer.Stop();
        _usageCancellation?.Cancel();
        _tray.Visible = false;
        _tray.Dispose();
        _trayIcon?.Dispose();
        _usageService.Dispose();
        _primaryPotion.Close();
        _secondaryPotion.Close();
        _details.ClosePermanently();
        _settingsWindow.ClosePermanently();
    }
}
