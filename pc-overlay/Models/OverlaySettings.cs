using System.IO;
using System.Text.Json;

namespace WatchHROverlay.Models;

public sealed class OverlaySettings
{
    public double Left { get; set; } = 80;
    public double Top { get; set; } = 80;
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 0.95;
    public bool IsLocked { get; set; } = false;
    public bool ClickThrough { get; set; } = false;
    public int UdpPort { get; set; } = 19888;
    public string Language { get; set; } = "auto"; // "auto", "zh", "en"

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GalaxyWatchHR",
        "settings.json");

    public static OverlaySettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new OverlaySettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var loaded = JsonSerializer.Deserialize<OverlaySettings>(json);
            if (loaded is null)
            {
                return new OverlaySettings();
            }

            loaded.Scale = Math.Clamp(loaded.Scale, 0.6, 2.2);
            loaded.Opacity = Math.Clamp(loaded.Opacity, 0.3, 1.0);
            loaded.UdpPort = loaded.UdpPort is >= 1024 and <= 65535 ? loaded.UdpPort : 19888;
            loaded.Language = loaded.Language is "zh" or "en" ? loaded.Language : "auto";
            return loaded;
        }
        catch
        {
            return new OverlaySettings();
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Non-fatal: continue even if settings persistence fails temporarily.
        }
    }
}
