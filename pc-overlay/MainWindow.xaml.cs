using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WatchHROverlay.Models;
using WatchHROverlay.Services;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace WatchHROverlay;

public partial class MainWindow : Window
{
    private const int SignalTimeoutSeconds = 15;

    private readonly OverlaySettings _settings;
    private readonly UdpHeartRateReceiver _receiver;
    private readonly DispatcherTimer _beatTimer;
    private readonly DispatcherTimer _watchdogTimer;
    private readonly DispatcherTimer _demoTimer;
    private readonly Random _random = new();

    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _traySetupItem;
    private Forms.ToolStripMenuItem? _trayIpItem;
    private Forms.ToolStripMenuItem? _trayDemoItem;
    private Forms.ToolStripMenuItem? _trayLockItem;
    private Forms.ToolStripMenuItem? _trayClickThroughItem;
    private Forms.ToolStripMenuItem? _trayAutoStartItem;
    private Forms.ToolStripMenuItem? _trayLangItem;
    private Forms.ToolStripMenuItem? _trayResetItem;
    private Forms.ToolStripMenuItem? _trayExitItem;

    private SetupWindow? _setupWindow;
    private int _currentBpm;
    private int? _lastBattery;
    private int _demoBpm = 76;
    private bool _isDemoMode;
    private DateTime _lastPacketUtc = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();

        _settings = OverlaySettings.Load();
        LocalizationService.ApplyPreference(_settings.Language);
        LocalizationService.LanguageChanged += OnLanguageChanged;

        _receiver = new UdpHeartRateReceiver(_settings.UdpPort);
        _receiver.HeartRateReceived += OnHeartRateReceived;
        _receiver.StatusMessage += OnReceiverStatusMessage;

        _beatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
        _beatTimer.Tick += (_, _) => PlayHeartbeatAnimation();

        _watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _watchdogTimer.Tick += (_, _) => CheckSignalTimeout();

        _demoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _demoTimer.Tick += (_, _) => EmitDemoHeartbeat();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySavedSettings();
        WindowInterop.HideFromAltTab(this);
        SetupTrayIcon();
        ApplyLocalization();

        try
        {
            _receiver.Start();
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationService.T(
                $"埠號 {_settings.UdpPort} 無法監聽: {ex.Message}",
                $"Cannot bind port {_settings.UdpPort}: {ex.Message}");
        }

        _watchdogTimer.Start();
    }

    private void ApplySavedSettings()
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Clamp(_settings.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - 160));
        Top = Math.Clamp(_settings.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - 90));

        RootScaleTransform.ScaleX = _settings.Scale;
        RootScaleTransform.ScaleY = _settings.Scale;
        Opacity = _settings.Opacity;

        MenuLockPosition.IsChecked = _settings.IsLocked;
        MenuClickThrough.IsChecked = _settings.ClickThrough;
        MenuAutoStart.IsChecked = StartupManager.IsAutoStartEnabled();
        UpdateLanguageChecks();

        RootGrid.Cursor = _settings.IsLocked ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.SizeAll;
        WindowInterop.SetClickThrough(this, _settings.ClickThrough);
    }

    private void OnLanguageChanged()
    {
        Dispatcher.BeginInvoke(ApplyLocalization);
    }

    private void ApplyLocalization()
    {
        var ip = NetworkInfoHelper.GetPrimaryLanIp();

        Title = LocalizationService.T("Galaxy Watch 心率監測", "Galaxy Watch Heart Rate");
        MenuSetupWizard.Header = LocalizationService.T("手錶安裝與防火牆精靈...", "Watch Setup & Firewall...");
        MenuIpInfo.Header = LocalizationService.T($"本機 IP：{ip}:{_settings.UdpPort}（點擊複製）", $"PC IP: {ip}:{_settings.UdpPort} (Click to copy)");
        MenuDemoMode.Header = LocalizationService.T("模擬心率測試模式", "Demo Heart Rate Mode");
        MenuScaleRoot.Header = LocalizationService.T("縮放大小", "Scale");
        MenuScaleS.Header = LocalizationService.T("小 (0.8x)", "Small (0.8x)");
        MenuScaleM.Header = LocalizationService.T("中 (1.0x)", "Medium (1.0x)");
        MenuScaleL.Header = LocalizationService.T("大 (1.35x)", "Large (1.35x)");
        MenuScaleXL.Header = LocalizationService.T("超大 (1.7x)", "Extra Large (1.7x)");
        MenuOpacityRoot.Header = LocalizationService.T("透明度", "Opacity");
        MenuLanguageRoot.Header = LocalizationService.T("語言 / Language", "Language / 語言");
        MenuLangAuto.Header = LocalizationService.T("自動偵測 (系統語系)", "Auto (System)");
        MenuLockPosition.Header = LocalizationService.T("鎖定位置 (防止誤拖動)", "Lock Position");
        MenuClickThrough.Header = LocalizationService.T("滑鼠穿透模式 (可從系統匣還原)", "Click-Through Mode (Restore via Tray)");
        MenuAutoStart.Header = LocalizationService.T("開機自動啟動", "Start with Windows");
        MenuResetPos.Header = LocalizationService.T("重設至螢幕左上角", "Reset Position");
        MenuExit.Header = LocalizationService.T("結束程式", "Exit");

        if (_trayIcon is not null)
        {
            _trayIcon.Text = LocalizationService.T("Galaxy Watch 心率懸浮視窗", "Galaxy Watch HR Overlay");
        }
        if (_traySetupItem is not null) _traySetupItem.Text = LocalizationService.T("手錶安裝與防火牆精靈...", "Watch Setup & Firewall...");
        if (_trayIpItem is not null) _trayIpItem.Text = LocalizationService.T($"本機 IP: {ip}:{_settings.UdpPort}", $"PC IP: {ip}:{_settings.UdpPort}");
        if (_trayDemoItem is not null) _trayDemoItem.Text = LocalizationService.T("模擬心率測試模式", "Demo Heart Rate Mode");
        if (_trayLockItem is not null) _trayLockItem.Text = LocalizationService.T("鎖定位置", "Lock Position");
        if (_trayClickThroughItem is not null) _trayClickThroughItem.Text = LocalizationService.T("滑鼠穿透模式", "Click-Through Mode");
        if (_trayAutoStartItem is not null) _trayAutoStartItem.Text = LocalizationService.T("開機自動啟動", "Start with Windows");
        if (_trayLangItem is not null) _trayLangItem.Text = LocalizationService.T("切換語言 (Switch to English)", "Switch to 繁體中文");
        if (_trayResetItem is not null) _trayResetItem.Text = LocalizationService.T("重設視窗位置", "Reset Position");
        if (_trayExitItem is not null) _trayExitItem.Text = LocalizationService.T("結束程式", "Exit");

        UpdateLanguageChecks();
        RefreshStatusLabel();
    }

    private void UpdateLanguageChecks()
    {
        MenuLangAuto.IsChecked = _settings.Language == "auto";
        MenuLangZh.IsChecked = _settings.Language == "zh";
        MenuLangEn.IsChecked = _settings.Language == "en";
    }

    private void RefreshStatusLabel()
    {
        if (_isDemoMode)
        {
            StatusText.Text = LocalizationService.T("模擬測試中", "Demo Mode");
            StatusText.Visibility = Visibility.Visible;
            return;
        }

        if (_currentBpm <= 0)
        {
            var waiting = _lastPacketUtc == DateTime.MinValue || (DateTime.UtcNow - _lastPacketUtc).TotalSeconds > SignalTimeoutSeconds;
            StatusText.Text = waiting
                ? LocalizationService.T($"等待手錶連線 ({NetworkInfoHelper.GetPrimaryLanIp()})", $"Waiting for watch ({NetworkInfoHelper.GetPrimaryLanIp()})")
                : LocalizationService.T("偵測心率中...", "Measuring HR...");
            StatusText.Visibility = Visibility.Visible;
            return;
        }

        if (_lastBattery is <= 20)
        {
            StatusText.Text = LocalizationService.T($"手錶電量低 {_lastBattery}%", $"Watch battery {_lastBattery}%");
            StatusText.Visibility = Visibility.Visible;
        }
        else
        {
            StatusText.Visibility = Visibility.Collapsed;
        }
    }

    private void SetupTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        _traySetupItem = new Forms.ToolStripMenuItem("Setup", null, (_, _) => Dispatcher.Invoke(OpenSetupWizard));
        menu.Items.Add(_traySetupItem);

        _trayIpItem = new Forms.ToolStripMenuItem("IP", null, (_, _) => Dispatcher.Invoke(CopyLanIp));
        menu.Items.Add(_trayIpItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        _trayDemoItem = new Forms.ToolStripMenuItem("Demo") { CheckOnClick = true, Checked = _isDemoMode };
        _trayDemoItem.Click += (_, _) => Dispatcher.Invoke(() => ToggleDemoMode(_trayDemoItem.Checked));
        menu.Items.Add(_trayDemoItem);

        _trayLockItem = new Forms.ToolStripMenuItem("Lock") { CheckOnClick = true, Checked = _settings.IsLocked };
        _trayLockItem.Click += (_, _) => Dispatcher.Invoke(() => SetLocked(_trayLockItem.Checked));
        menu.Items.Add(_trayLockItem);

        _trayClickThroughItem = new Forms.ToolStripMenuItem("Click-Through") { CheckOnClick = true, Checked = _settings.ClickThrough };
        _trayClickThroughItem.Click += (_, _) => Dispatcher.Invoke(() => SetClickThrough(_trayClickThroughItem.Checked));
        menu.Items.Add(_trayClickThroughItem);

        _trayAutoStartItem = new Forms.ToolStripMenuItem("Auto-Start") { CheckOnClick = true, Checked = StartupManager.IsAutoStartEnabled() };
        _trayAutoStartItem.Click += (_, _) => Dispatcher.Invoke(() => SetAutoStart(_trayAutoStartItem.Checked));
        menu.Items.Add(_trayAutoStartItem);

        _trayLangItem = new Forms.ToolStripMenuItem("Language", null, (_, _) => Dispatcher.Invoke(ToggleLanguageDirect));
        menu.Items.Add(_trayLangItem);

        _trayResetItem = new Forms.ToolStripMenuItem("Reset", null, (_, _) => Dispatcher.Invoke(ResetPosition));
        menu.Items.Add(_trayResetItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        _trayExitItem = new Forms.ToolStripMenuItem("Exit", null, (_, _) => Dispatcher.Invoke(Close));
        menu.Items.Add(_trayExitItem);

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Galaxy Watch HR Overlay",
            Icon = CreateHeartTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    private static Icon CreateHeartTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);

        using var path = new GraphicsPath();
        path.AddBezier(16, 28, 3, 19, 3, 7, 10, 5);
        path.AddBezier(10, 5, 14, 4, 16, 8, 16, 10);
        path.AddBezier(16, 10, 16, 8, 18, 4, 22, 5);
        path.AddBezier(22, 5, 29, 7, 29, 19, 16, 28);

        using var brush = new SolidBrush(System.Drawing.Color.FromArgb(255, 255, 64, 105));
        g.FillPath(brush, path);

        var hIcon = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(hIcon);
    }

    private void OnHeartRateReceived(HeartRatePacket packet, IPEndPoint remoteEndPoint)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_isDemoMode)
            {
                return;
            }

            _lastPacketUtc = packet.ReceivedAtUtc;
            _lastBattery = packet.Battery;
            ApplyHeartRate(packet.Bpm);
        });
    }

    private void OnReceiverStatusMessage(string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_currentBpm <= 0)
            {
                StatusText.Text = message;
                StatusText.Visibility = Visibility.Visible;
            }
        });
    }

    private void ApplyHeartRate(int bpm)
    {
        _currentBpm = bpm;

        if (bpm <= 0)
        {
            BpmText.Text = "--";
            SetHeartPalette(MediaColor.FromRgb(148, 163, 184), 0.35);
            _beatTimer.Stop();
            RefreshStatusLabel();
            return;
        }

        BpmText.Text = bpm.ToString();

        var (color, glowOpacity) = GetZoneStyle(bpm);
        SetHeartPalette(color, glowOpacity);

        var clampedBpm = Math.Clamp(bpm, 40, 200);
        _beatTimer.Interval = TimeSpan.FromSeconds(60.0 / clampedBpm);
        if (!_beatTimer.IsEnabled)
        {
            PlayHeartbeatAnimation();
            _beatTimer.Start();
        }

        RefreshStatusLabel();
    }

    private static (MediaColor color, double glowOpacity) GetZoneStyle(int bpm)
    {
        return bpm switch
        {
            >= 140 => (MediaColor.FromRgb(255, 23, 68), 0.95),
            >= 100 => (MediaColor.FromRgb(255, 59, 92), 0.8),
            _ => (MediaColor.FromRgb(255, 77, 109), 0.65)
        };
    }

    private void SetHeartPalette(MediaColor color, double glowOpacity)
    {
        var brush = new SolidColorBrush(color);
        HeartIcon.Fill = brush;
        PulseRing.Stroke = brush;
        HeartGlow.Color = color;
        HeartGlow.Opacity = glowOpacity;
    }

    private void PlayHeartbeatAnimation()
    {
        var scaleAnim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(420)
        };
        scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
        scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.26, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0.98, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(175)), new CubicEase { EasingMode = EasingMode.EaseInOut }));
        scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(255)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), new CubicEase { EasingMode = EasingMode.EaseInOut }));

        HeartScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        HeartScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

        var ringScale = new DoubleAnimation(0.75, 1.85, TimeSpan.FromMilliseconds(480))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var ringOpacity = new DoubleAnimation(0.65, 0.0, TimeSpan.FromMilliseconds(480))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        PulseRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, ringScale);
        PulseRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, ringScale);
        PulseRing.BeginAnimation(OpacityProperty, ringOpacity);
    }

    private void CheckSignalTimeout()
    {
        if (_isDemoMode)
        {
            return;
        }

        if (_lastPacketUtc == DateTime.MinValue || (DateTime.UtcNow - _lastPacketUtc).TotalSeconds <= SignalTimeoutSeconds)
        {
            return;
        }

        _currentBpm = 0;
        _lastBattery = null;
        _beatTimer.Stop();
        BpmText.Text = "--";
        SetHeartPalette(MediaColor.FromRgb(148, 163, 184), 0.35);
        RefreshStatusLabel();
    }

    private void EmitDemoHeartbeat()
    {
        var delta = _random.Next(-3, 5);
        _demoBpm = Math.Clamp(_demoBpm + delta, 64, 152);
        _lastBattery = 92;
        ApplyHeartRate(_demoBpm);
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.IsLocked || _settings.ClickThrough)
        {
            return;
        }

        DragMove();
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
    }

    private void Root_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        var nextScale = Math.Clamp(_settings.Scale + (e.Delta > 0 ? 0.1 : -0.1), 0.6, 2.2);
        SetScale(nextScale);
    }

    private void Root_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_settings.IsLocked || _settings.ClickThrough)
        {
            return;
        }

        HoverBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)));
    }

    private void Root_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        HoverBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(220)));
    }

    private void MenuSetupWizard_Click(object sender, RoutedEventArgs e) => OpenSetupWizard();

    private void OpenSetupWizard()
    {
        if (_setupWindow is not null && _setupWindow.IsLoaded)
        {
            _setupWindow.Activate();
            return;
        }

        _setupWindow = new SetupWindow(_settings.UdpPort, ToggleLanguageDirect);
        _setupWindow.Closed += (_, _) => _setupWindow = null;
        _setupWindow.Show();
    }

    private void MenuCopyIp_Click(object sender, RoutedEventArgs e) => CopyLanIp();

    private void CopyLanIp()
    {
        var ip = NetworkInfoHelper.GetPrimaryLanIp();
        System.Windows.Clipboard.SetText(ip);
        StatusText.Text = LocalizationService.T($"已複製 IP：{ip}", $"Copied IP: {ip}");
        StatusText.Visibility = Visibility.Visible;
    }

    private void MenuDemoMode_Click(object sender, RoutedEventArgs e) => ToggleDemoMode(MenuDemoMode.IsChecked);

    private void ToggleDemoMode(bool enabled)
    {
        _isDemoMode = enabled;
        MenuDemoMode.IsChecked = enabled;
        if (_trayDemoItem is not null)
        {
            _trayDemoItem.Checked = enabled;
        }

        if (enabled)
        {
            EmitDemoHeartbeat();
            _demoTimer.Start();
        }
        else
        {
            _demoTimer.Stop();
            _lastPacketUtc = DateTime.MinValue;
            _currentBpm = 0;
            _beatTimer.Stop();
            BpmText.Text = "--";
            SetHeartPalette(MediaColor.FromRgb(148, 163, 184), 0.35);
            RefreshStatusLabel();
        }
    }

    private void MenuScale_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && double.TryParse(item.Tag?.ToString(), out var scale))
        {
            SetScale(scale);
        }
    }

    private void SetScale(double scale)
    {
        _settings.Scale = Math.Round(scale, 2);
        RootScaleTransform.ScaleX = _settings.Scale;
        RootScaleTransform.ScaleY = _settings.Scale;
        _settings.Save();
    }

    private void MenuOpacity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && double.TryParse(item.Tag?.ToString(), out var opacity))
        {
            _settings.Opacity = opacity;
            Opacity = opacity;
            _settings.Save();
        }
    }

    private void MenuLanguage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string lang)
        {
            SetLanguage(lang);
        }
    }

    private void ToggleLanguageDirect()
    {
        SetLanguage(LocalizationService.IsChinese ? "en" : "zh");
    }

    private void SetLanguage(string lang)
    {
        _settings.Language = lang is "zh" or "en" ? lang : "auto";
        _settings.Save();
        LocalizationService.ApplyPreference(_settings.Language);
    }

    private void MenuLockPosition_Click(object sender, RoutedEventArgs e) => SetLocked(MenuLockPosition.IsChecked);

    private void SetLocked(bool locked)
    {
        _settings.IsLocked = locked;
        MenuLockPosition.IsChecked = locked;
        if (_trayLockItem is not null)
        {
            _trayLockItem.Checked = locked;
        }

        RootGrid.Cursor = locked ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.SizeAll;
        if (locked)
        {
            HoverBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120)));
        }

        _settings.Save();
    }

    private void MenuClickThrough_Click(object sender, RoutedEventArgs e) => SetClickThrough(MenuClickThrough.IsChecked);

    private void SetClickThrough(bool enabled)
    {
        _settings.ClickThrough = enabled;
        MenuClickThrough.IsChecked = enabled;
        if (_trayClickThroughItem is not null)
        {
            _trayClickThroughItem.Checked = enabled;
        }

        WindowInterop.SetClickThrough(this, enabled);
        if (enabled)
        {
            HoverBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120)));
        }

        _settings.Save();
    }

    private void MenuAutoStart_Click(object sender, RoutedEventArgs e) => SetAutoStart(MenuAutoStart.IsChecked);

    private void SetAutoStart(bool enabled)
    {
        var ok = StartupManager.SetAutoStart(enabled);
        var actualState = ok ? enabled : StartupManager.IsAutoStartEnabled();
        MenuAutoStart.IsChecked = actualState;
        if (_trayAutoStartItem is not null)
        {
            _trayAutoStartItem.Checked = actualState;
        }
    }

    private void MenuResetPosition_Click(object sender, RoutedEventArgs e) => ResetPosition();

    private void ResetPosition()
    {
        Left = 80;
        Top = 80;
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        _setupWindow?.Close();

        _demoTimer.Stop();
        _beatTimer.Stop();
        _watchdogTimer.Stop();
        _receiver.Dispose();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
    }
}
