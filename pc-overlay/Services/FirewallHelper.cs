using System.ComponentModel;
using System.Diagnostics;

namespace WatchHROverlay.Services;

public static class FirewallHelper
{
    public static string GetRuleName(int port) => $"GalaxyWatchHR UDP {port}";

    public static async Task<bool> IsRuleConfiguredAsync(int port)
    {
        if (port is < 1024 or > 65535)
        {
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("advfirewall");
        psi.ArgumentList.Add("firewall");
        psi.ArgumentList.Add("show");
        psi.ArgumentList.Add("rule");
        psi.ArgumentList.Add($"name={GetRuleName(port)}");

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            await proc.WaitForExitAsync().ConfigureAwait(false);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<(bool success, string message)> ConfigureFirewallElevatedAsync(int port)
    {
        if (port is < 1024 or > 65535)
        {
            return (false, LocalizationService.T("無效的連接埠編號。", "Invalid UDP port number."));
        }

        var ruleName = GetRuleName(port);
        var cmdArgs = $"/c netsh advfirewall firewall delete rule name=\"{ruleName}\" >nul 2>&1 & " +
                      $"netsh advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=UDP " +
                      $"localport={port} remoteip=localsubnet profile=private,public";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = cmdArgs,
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return (false, LocalizationService.T("無法啟動系統管理員程序。", "Unable to start elevated process."));
            }

            await proc.WaitForExitAsync().ConfigureAwait(false);
            if (proc.ExitCode == 0)
            {
                return (true, LocalizationService.T(
                    $"已放行同網段 UDP {port}（私人／公用區網皆適用）！",
                    $"Allowed local-subnet UDP {port} (Private & Public networks)!"));
            }

            return (false, LocalizationService.T(
                $"防火牆設定失敗（錯誤碼 {proc.ExitCode}）。",
                $"Firewall configuration failed (exit code {proc.ExitCode})."));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, LocalizationService.T("已取消系統管理員授權。", "Administrator prompt was cancelled."));
        }
        catch (Exception ex)
        {
            return (false, LocalizationService.T($"發生錯誤：{ex.Message}", $"Error: {ex.Message}"));
        }
    }
}
