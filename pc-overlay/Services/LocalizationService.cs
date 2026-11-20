using System.Globalization;

namespace WatchHROverlay.Services;

public static class LocalizationService
{
    private static bool _isChinese = DetectSystemChinese();

    public static bool IsChinese => _isChinese;

    public static event Action? LanguageChanged;

    public static void ApplyPreference(string preference)
    {
        _isChinese = preference switch
        {
            "zh" => true,
            "en" => false,
            _ => DetectSystemChinese()
        };
        LanguageChanged?.Invoke();
    }

    public static bool DetectSystemChinese()
    {
        var name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return string.Equals(name, "zh", StringComparison.OrdinalIgnoreCase);
    }

    public static string T(string zh, string en) => _isChinese ? zh : en;
}
