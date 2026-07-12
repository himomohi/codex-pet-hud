using System.Drawing;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly PotionWindow _primaryPotion = new("5H", System.Windows.Media.Color.FromRgb(251, 73, 52), System.Windows.Media.Color.FromRgb(127, 29, 29));
    private readonly PotionWindow _secondaryPotion = new("WK", System.Windows.Media.Color.FromRgb(96, 165, 250), System.Windows.Media.Color.FromRgb(23, 37, 84));
    private readonly UsageDetailsWindow _details = new();
    private readonly SettingsWindow _settingsWindow = new();
    private readonly Forms.NotifyIcon _tray = new();
    private OverlaySettings _settings;
    private UsageSnapshot _usage = UsageSnapshot.Empty;
    private PetAnchor? _anchor;
    private DateTimeOffset _lastUsageAttempt = DateTimeOffset.MinValue;
    private bool _refreshing;
    private bool _petVisible;

    public MainController()
    {
        _settings = _store.LoadSettings();
        _cacheService = new CacheMaintenanceService(_store);
        _alertService = new ThresholdAlertService(_store);
    }

    public void Start()
    {
        ConfigureTray();
        _primaryPotion.PotionClicked += ShowDetails;
        _secondaryPotion.PotionClicked += ShowDetails;
        _details.RefreshRequested += () => _ = RefreshUsageAsync(force: true);
        _settingsWindow.SettingsChanged += ApplySettings;
        _settingsWindow.CleanupRequested += RunCleanup;
        _settingsWindow.Apply(_settings);
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
        _ = TickAsync();
    }

    private async Task TickAsync()
    {
        var anchor = _stateReader.ReadVisibleAnchor();
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
            await RefreshUsageAsync(force: justShown);
        }
        RunScheduledCleanupIfNeeded();
    }

    private void HideHud()
    {
        _petVisible = false;
        _anchor = null;
        _primaryPotion.Hide();
        _secondaryPotion.Hide();
        _details.Hide();
        UpdateTrayText();
    }

    private void PlacePotions(PetAnchor anchor)
    {
        var screen = Forms.Screen.FromPoint(new Point((int)Math.Round(anchor.X + anchor.Width / 2), (int)Math.Round(anchor.Y + anchor.Height / 2)));
        var work = screen.WorkingArea;
        var scale = _settings.Scale;
        var availableSide = Math.Max(0, Math.Min(anchor.X - work.Left, work.Right - anchor.Right));
        var gap = _settings.PotionGap * scale;
        var potionWidth = 92 * scale;
        if (potionWidth + gap > availableSide && availableSide > 46)
        {
            gap = Math.Max(0, availableSide - potionWidth);
            if (potionWidth > availableSide)
            {
                scale = Math.Max(0.5, Math.Min(scale, availableSide / 92));
                potionWidth = 92 * scale;
                gap = Math.Max(0, availableSide - potionWidth);
            }
        }

        _primaryPotion.ApplyScale(scale);
        _secondaryPotion.ApplyScale(scale);
        var potionHeight = 110 * scale;
        var y = anchor.CenterY - potionHeight / 2 + _settings.VerticalOffset;
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - potionHeight));
        var leftX = anchor.X - gap - potionWidth + _settings.HorizontalOffset;
        var rightX = anchor.Right + gap + _settings.HorizontalOffset;
        leftX = Math.Clamp(leftX, work.Left, Math.Max(work.Left, work.Right - potionWidth));
        rightX = Math.Clamp(rightX, work.Left, Math.Max(work.Left, work.Right - potionWidth));

        _primaryPotion.Left = leftX;
        _primaryPotion.Top = y;
        _secondaryPotion.Left = rightX;
        _secondaryPotion.Top = y;
    }

    private async Task RefreshUsageAsync(bool force)
    {
        if (!_petVisible || _refreshing) return;
        if (!force && DateTimeOffset.Now - _lastUsageAttempt < TimeSpan.FromSeconds(30)) return;
        _refreshing = true;
        _lastUsageAttempt = DateTimeOffset.Now;
        _details.Update(_usage, true);
        try
        {
            var refreshed = await _usageService.RefreshAsync();
            if (refreshed is not null)
            {
                _usage = refreshed;
                _primaryPotion.UpdateUsage(_usage.PrimaryRemaining, _usage.PrimaryReset, _usage.Source);
                _secondaryPotion.UpdateUsage(_usage.SecondaryRemaining, _usage.SecondaryReset, _usage.Source);
                foreach (var alert in _alertService.Evaluate(_usage, _settings)) ShowAlert(alert);
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        finally
        {
            _refreshing = false;
            _details.Update(_usage, false);
            UpdateTrayText();
        }
    }

    private void ShowDetails()
    {
        if (!_petVisible || _anchor is null) return;
        _details.Update(_usage, _refreshing);
        var work = Forms.Screen.FromPoint(new Point((int)_anchor.X, (int)_anchor.Y)).WorkingArea;
        _details.Left = Math.Clamp(_anchor.X - _details.Width - 16, work.Left, Math.Max(work.Left, work.Right - _details.Width));
        _details.Top = Math.Clamp(_anchor.Y - _details.Height - 12, work.Top, Math.Max(work.Top, work.Bottom - _details.Height));
        if (!_details.IsVisible) _details.Show();
        _details.Activate();
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
        _tray.Icon = SystemIcons.Information;
        _tray.Text = "Codex 포션 HUD";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowSettings();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("사용량 지금 갱신", null, (_, _) => _ = RefreshUsageAsync(force: true));
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
        _tray.Text = $"Codex 포션 HUD · 5H {primary} · WK {weekly}";
    }

    public void Dispose()
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _usageService.Dispose();
        _primaryPotion.Close();
        _secondaryPotion.Close();
        _details.ClosePermanently();
        _settingsWindow.ClosePermanently();
    }
}
