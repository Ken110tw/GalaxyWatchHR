using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WatchHROverlay.Services;

public static class WatchInstallerService
{
    private const string OfficialPlatformToolsUrl =
        "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
    private const string WatchPackageName = "com.antigravity.watchhr";
    private const string EmbeddedApkName = "GalaxyWatchHR.apk";

    private static readonly Regex PairCodeRegex = new(@"^\d{6}$", RegexOptions.Compiled);

    private static string CachedToolsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GalaxyWatchHR");

    private static string CachedAdbPath => Path.Combine(CachedToolsRoot, "platform-tools", "adb.exe");

    public static string? FindAdbPath()
    {
        if (File.Exists(CachedAdbPath))
        {
            return CachedAdbPath;
        }

        var sdkAdb = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Android", "Sdk", "platform-tools", "adb.exe");
        if (File.Exists(sdkAdb))
        {
            return sdkAdb;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "adb.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static async Task<string> DownloadOfficialPlatformToolsAsync(
        Action<string> log,
        CancellationToken ct)
    {
        Directory.CreateDirectory(CachedToolsRoot);
        var zipPath = Path.Combine(CachedToolsRoot, "platform-tools-latest-windows.zip");
        var extractRoot = Path.GetFullPath(CachedToolsRoot);

        log(LocalizationService.T(
            "正在從 Google 官方伺服器下載 Android Platform-Tools（約 6 MB）...",
            "Downloading official Google Android Platform-Tools (~6 MB)..."));

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        using var response = await http.GetAsync(OfficialPlatformToolsUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using (var fs = File.Create(zipPath))
        {
            await response.Content.CopyToAsync(fs, ct);
        }

        log(LocalizationService.T("下載完成，正在解壓縮...", "Download complete. Extracting..."));

        var targetDir = Path.Combine(extractRoot, "platform-tools");
        if (Directory.Exists(targetDir))
        {
            Directory.Delete(targetDir, recursive: true);
        }

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                var destPath = Path.GetFullPath(Path.Combine(extractRoot, entry.FullName));
                if (!destPath.StartsWith(extractRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Blocked unsafe zip entry path.");
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }
        }

        File.Delete(zipPath);

        if (!File.Exists(CachedAdbPath))
        {
            throw new FileNotFoundException("adb.exe was not found after extraction.");
        }

        log(LocalizationService.T($"已就緒：{CachedAdbPath}", $"Ready: {CachedAdbPath}"));
        return CachedAdbPath;
    }

    public static bool TryValidateEndpoint(string raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        var parts = trimmed.Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var port) || port is < 1 or > 65535)
        {
            return false;
        }

        normalized = $"{ip}:{port}";
        return true;
    }

    public static bool TryValidatePairCode(string raw, out string normalized)
    {
        normalized = (raw ?? string.Empty).Trim();
        return PairCodeRegex.IsMatch(normalized);
    }

    public static string ExtractWatchApk()
    {
        var baseDir = AppContext.BaseDirectory;
        var sideBySideApk = Path.Combine(baseDir, EmbeddedApkName);
        if (File.Exists(sideBySideApk))
        {
            return sideBySideApk;
        }

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(EmbeddedApkName)
            ?? throw new FileNotFoundException("Embedded GalaxyWatchHR.apk not found.");

        Directory.CreateDirectory(CachedToolsRoot);
        var extracted = Path.Combine(CachedToolsRoot, EmbeddedApkName);
        using var fs = File.Create(extracted);
        stream.CopyTo(fs);
        return extracted;
    }

    public static async Task<bool> InstallToWatchAsync(
        string adbPath,
        bool requirePair,
        string? pairEndpoint,
        string? pairCode,
        string connectEndpoint,
        Action<string> log,
        CancellationToken ct)
    {
        var apkPath = ExtractWatchApk();

        if (requirePair)
        {
            log(LocalizationService.T($"[1/4] 正在配對 {pairEndpoint}...", $"[1/4] Pairing with {pairEndpoint}..."));
            var (ok, output) = await RunAdbAsync(adbPath, new[] { "pair", pairEndpoint!, pairCode! }, ct);
            log(output);
            if (!ok)
            {
                log(LocalizationService.T("配對失敗，請確認配對 IP:Port 與 6 位配對碼是否仍在手錶畫面上。", "Pairing failed. Check that the pairing IP:Port and 6-digit code are still active on your watch."));
                return false;
            }
        }

        log(LocalizationService.T($"[2/4] 正在連線至手錶 {connectEndpoint}...", $"[2/4] Connecting to watch {connectEndpoint}..."));
        var (connOk, connOut) = await RunAdbAsync(adbPath, new[] { "connect", connectEndpoint }, ct);
        log(connOut);
        if (!connOk || connOut.Contains("failed", StringComparison.OrdinalIgnoreCase) || connOut.Contains("cannot", StringComparison.OrdinalIgnoreCase))
        {
            log(LocalizationService.T("連線失敗，請確認手錶無線偵錯已開啟且與電腦在同一 Wi-Fi。", "Connection failed. Make sure Wireless Debugging is ON and both devices share the same Wi-Fi."));
            return false;
        }

        log(LocalizationService.T("[3/4] 正在安裝 GalaxyWatchHR.apk（約 25 KB）...", "[3/4] Installing GalaxyWatchHR.apk (~25 KB)..."));
        var (instOk, instOut) = await RunAdbAsync(adbPath, new[] { "-s", connectEndpoint, "install", "-r", apkPath }, ct);
        log(instOut);
        if (!instOk || !instOut.Contains("Success", StringComparison.OrdinalIgnoreCase))
        {
            log(LocalizationService.T("安裝失敗，請確認手錶畫面沒有休眠斷線後重試。", "Install failed. Keep the watch screen awake and retry."));
            return false;
        }

        log(LocalizationService.T("[4/4] 正在自動授予背景心率權限與省電白名單...", "[4/4] Granting background sensor permissions & battery whitelist..."));
        var grants = new[]
        {
            new[] { "-s", connectEndpoint, "shell", "pm", "grant", WatchPackageName, "android.permission.BODY_SENSORS" },
            new[] { "-s", connectEndpoint, "shell", "pm", "grant", WatchPackageName, "android.permission.BODY_SENSORS_BACKGROUND" },
            new[] { "-s", connectEndpoint, "shell", "pm", "grant", WatchPackageName, "android.permission.health.READ_HEART_RATE" },
            new[] { "-s", connectEndpoint, "shell", "pm", "grant", WatchPackageName, "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND" },
            new[] { "-s", connectEndpoint, "shell", "pm", "grant", WatchPackageName, "android.permission.POST_NOTIFICATIONS" },
            new[] { "-s", connectEndpoint, "shell", "dumpsys", "deviceidle", "whitelist", $"+{WatchPackageName}" },
            new[] { "-s", connectEndpoint, "shell", "cmd", "appops", "set", WatchPackageName, "RUN_ANY_IN_BACKGROUND", "allow" },
            new[] { "-s", connectEndpoint, "shell", "am", "start", "-n", $"{WatchPackageName}/.MainActivity" }
        };

        foreach (var args in grants)
        {
            await RunAdbAsync(adbPath, args, ct);
        }

        log(LocalizationService.T(
            "全部完成！手錶已自動開啟「區網心率」，請點擊手錶上的「開始傳輸」（完成後可關閉手錶無線偵錯以省電）。",
            "All done! 'LAN Heart Rate' is now open on your watch. Tap 'Start' (you can turn off Wireless Debugging afterwards to save battery)."));
        return true;
    }

    private static async Task<(bool success, string output)> RunAdbAsync(
        string adbPath,
        IEnumerable<string> args,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = adbPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start adb.exe.");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        var combined = (await stdoutTask + "\n" + await stderrTask).Trim();
        return (proc.ExitCode == 0, combined);
    }
}
