using System.IO;
using Microsoft.Win32;

namespace WatchHROverlay.Services;

public static class StartupManager
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "GalaxyWatchHR";

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var raw = key?.GetValue(AppValueName) as string;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var cleaned = raw.Trim().Trim('"');
            return File.Exists(cleaned);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    return false;
                }

                key.SetValue(AppValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
