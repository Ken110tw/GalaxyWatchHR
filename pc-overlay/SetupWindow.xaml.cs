using System.Windows;
using System.Windows.Media;
using WatchHROverlay.Services;

namespace WatchHROverlay;

public partial class SetupWindow : Window
{
    private readonly int _udpPort;
    private readonly Action _onToggleLanguage;
    private CancellationTokenSource? _installCts;
    private bool _isBusy;

    public SetupWindow(int udpPort, Action onToggleLanguage)
    {
        InitializeComponent();
        _udpPort = udpPort;
        _onToggleLanguage = onToggleLanguage;

        LocalizationService.LanguageChanged += OnLanguageChanged;
        Loaded += SetupWindow_Loaded;
        Closed += SetupWindow_Closed;
    }

    private async void SetupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalization();
        await RefreshStatusAsync();
    }

    private void SetupWindow_Closed(object? sender, EventArgs e)
    {
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        _installCts?.Cancel();
        _installCts?.Dispose();
    }

    private void OnLanguageChanged()
    {
        Dispatcher.BeginInvoke(async () =>
        {
            ApplyLocalization();
            await RefreshStatusAsync();
        });
    }

    private void ApplyLocalization()
    {
        Title = LocalizationService.T("Galaxy Watch 設定與安裝精靈", "Galaxy Watch Setup & Installer");
        TxtHeaderTitle.Text = LocalizationService.T("♥ Galaxy Watch 區網心率設定精靈", "♥ Galaxy Watch LAN HR Setup");
        TxtHeaderSub.Text = LocalizationService.T(
            "一鍵放行防火牆並無線安裝 App 到你的 Galaxy Watch（免開終端機）",
            "Configure firewall & install the watch app wirelessly in one click");
        BtnLangToggle.Content = LocalizationService.T("English", "繁體中文");

        TxtStep1Title.Text = LocalizationService.T("1. 電腦網路與防火牆", "1. PC Network & Firewall");
        BtnCopyIp.Content = LocalizationService.T("複製 IP", "Copy IP");
        BtnFirewall.Content = LocalizationService.T("一鍵放行防火牆", "Allow Firewall");

        TxtStep2Title.Text = LocalizationService.T("2. 無線安裝到 Galaxy Watch", "2. Wireless Install to Galaxy Watch");
        TxtWatchGuide.Text = LocalizationService.T(
            "手錶步驟：設定 → 關於手錶 → 軟體資訊 → 連點「軟體版本」7 次 → 返回進入「開發人員選項」→ 開啟 ADB 與無線偵錯。",
            "On Watch: Settings → About watch → Software info → tap Software version 7 times → Developer options → enable ADB & Wireless debugging.");
        ChkNeedPair.Content = LocalizationService.T("首次連線此電腦（需要輸入配對碼）", "First-time setup on this PC (pairing code required)");
        TxtPairAddrLabel.Text = LocalizationService.T("配對畫面 IP:Port（例如 192.168.1.120:37891）", "Pairing IP:Port (e.g. 192.168.1.120:37891)");
        TxtPairCodeLabel.Text = LocalizationService.T("6 位數配對碼", "6-digit code");
        TxtConnectAddrLabel.Text = LocalizationService.T("無線偵錯主畫面 IP:Port（Port 與配對畫面不同）", "Wireless debugging main IP:Port (different port from pairing)");
        BtnInstallWatch.Content = LocalizationService.T("一鍵安裝並自動開啟背景權限", "Install & Grant Background Permissions");

        TxtLanIp.Text = LocalizationService.T(
            $"本機區網 IP：{NetworkInfoHelper.GetPrimaryLanIp()}:{_udpPort}",
            $"PC LAN IP: {NetworkInfoHelper.GetPrimaryLanIp()}:{_udpPort}");
    }

    private async Task RefreshStatusAsync()
    {
        var fwOk = await FirewallHelper.IsRuleConfiguredAsync(_udpPort);
        TxtFirewallStatus.Text = fwOk
            ? LocalizationService.T($"防火牆狀態：已放行 UDP {_udpPort}", $"Firewall: Allowed UDP {_udpPort}")
            : LocalizationService.T($"防火牆狀態：尚未放行 UDP {_udpPort}", $"Firewall: UDP {_udpPort} not configured");
        TxtFirewallStatus.Foreground = new SolidColorBrush(
            fwOk ? System.Windows.Media.Color.FromRgb(74, 222, 128) : System.Windows.Media.Color.FromRgb(251, 191, 36));

        var adbPath = WatchInstallerService.FindAdbPath();
        TxtAdbStatus.Text = adbPath is not null
            ? LocalizationService.T("ADB：已就緒", "ADB: Ready")
            : LocalizationService.T("ADB：未安裝（點擊安裝時可自動下載）", "ADB: Not found (auto-download available)");
        TxtAdbStatus.Foreground = new SolidColorBrush(
            adbPath is not null ? System.Windows.Media.Color.FromRgb(74, 222, 128) : System.Windows.Media.Color.FromRgb(251, 191, 36));
    }

    private void BtnLangToggle_Click(object sender, RoutedEventArgs e)
    {
        _onToggleLanguage();
    }

    private void BtnCopyIp_Click(object sender, RoutedEventArgs e)
    {
        var ip = NetworkInfoHelper.GetPrimaryLanIp();
        System.Windows.Clipboard.SetText(ip);
        AppendLog(LocalizationService.T($"已複製本機 IP：{ip}", $"Copied PC LAN IP: {ip}"));
    }

    private async void BtnFirewall_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            AppendLog(LocalizationService.T("正在請求系統管理員權限以設定防火牆...", "Requesting administrator permission for firewall rule..."));
            var (_, msg) = await FirewallHelper.ConfigureFirewallElevatedAsync(_udpPort);
            AppendLog(msg);
            await RefreshStatusAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ChkNeedPair_Changed(object sender, RoutedEventArgs e)
    {
        if (PairPanel is not null)
        {
            PairPanel.Visibility = ChkNeedPair.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void BtnInstallWatch_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var needPair = ChkNeedPair.IsChecked == true;
        string? pairAddr = null;
        string? pairCode = null;

        if (needPair)
        {
            if (!WatchInstallerService.TryValidateEndpoint(TxtPairAddr.Text, out var validPair))
            {
                AppendLog(LocalizationService.T("請輸入正確的配對 IP:Port（例如 192.168.1.120:37891）。", "Enter a valid pairing IP:Port (e.g. 192.168.1.120:37891)."));
                return;
            }
            if (!WatchInstallerService.TryValidatePairCode(TxtPairCode.Text, out var validCode))
            {
                AppendLog(LocalizationService.T("請輸入 6 位數字配對碼。", "Enter a valid 6-digit pairing code."));
                return;
            }
            pairAddr = validPair;
            pairCode = validCode;
        }

        if (!WatchInstallerService.TryValidateEndpoint(TxtConnectAddr.Text, out var connectAddr))
        {
            AppendLog(LocalizationService.T("請輸入正確的連線 IP:Port（手錶無線偵錯主畫面的位址）。", "Enter a valid connection IP:Port (shown on Wireless debugging main screen)."));
            return;
        }

        var adbPath = WatchInstallerService.FindAdbPath();
        if (adbPath is null)
        {
            var prompt = System.Windows.MessageBox.Show(
                this,
                LocalizationService.T(
                    "電腦尚未安裝 adb（Android Platform-Tools）。\n是否立即從 Google 官方伺服器自動下載（約 6 MB）並繼續安裝？",
                    "ADB (Android Platform-Tools) was not found on this PC.\nDownload official Google Platform-Tools (~6 MB) now and continue?"),
                LocalizationService.T("下載 Google Platform-Tools", "Download Google Platform-Tools"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (prompt != MessageBoxResult.Yes)
            {
                AppendLog(LocalizationService.T("已取消下載 ADB。", "ADB download cancelled."));
                return;
            }
        }

        SetBusy(true);
        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();

        try
        {
            adbPath ??= await WatchInstallerService.DownloadOfficialPlatformToolsAsync(AppendLog, _installCts.Token);
            await RefreshStatusAsync();

            await WatchInstallerService.InstallToWatchAsync(
                adbPath,
                needPair,
                pairAddr,
                pairCode,
                connectAddr,
                AppendLog,
                _installCts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendLog(LocalizationService.T("操作已取消。", "Operation cancelled."));
        }
        catch (Exception ex)
        {
            AppendLog(LocalizationService.T($"安裝過程發生錯誤：{ex.Message}", $"Installation error: {ex.Message}"));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        BtnFirewall.IsEnabled = !busy;
        BtnInstallWatch.IsEnabled = !busy;
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var stamp = DateTime.Now.ToString("HH:mm:ss");
            TxtLog.AppendText($"[{stamp}] {message}\n");
            TxtLog.ScrollToEnd();
        });
    }
}
