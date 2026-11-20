package com.antigravity.watchhr;

import android.content.Context;
import android.content.SharedPreferences;

import java.util.Locale;

public final class LangHelper {
    public static final String KEY_LANG = "app_lang";
    public static final String LANG_AUTO = "auto";
    public static final String LANG_ZH = "zh";
    public static final String LANG_EN = "en";

    private LangHelper() {
    }

    public static boolean isChinese(Context context) {
        SharedPreferences prefs = context.getSharedPreferences(HeartRateService.PREFS_NAME, Context.MODE_PRIVATE);
        String pref = prefs.getString(KEY_LANG, LANG_AUTO);
        if (LANG_ZH.equals(pref)) {
            return true;
        }
        if (LANG_EN.equals(pref)) {
            return false;
        }
        String sysLang = Locale.getDefault().getLanguage();
        return "zh".equalsIgnoreCase(sysLang);
    }

    public static boolean toggleLanguage(Context context) {
        boolean nextZh = !isChinese(context);
        SharedPreferences prefs = context.getSharedPreferences(HeartRateService.PREFS_NAME, Context.MODE_PRIVATE);
        prefs.edit().putString(KEY_LANG, nextZh ? LANG_ZH : LANG_EN).apply();
        return nextZh;
    }

    public static String t(boolean zh, String zhText, String enText) {
        return zh ? zhText : enText;
    }

    public static String formatStatus(boolean zh, AppState.StatusCode code, String detail) {
        String d = detail == null ? "" : detail;
        switch (code) {
            case IDLE:
                return t(zh, "待機中", "Idle");
            case STARTING_SEARCH:
                return t(zh, "啟動 Wi-Fi 並搜尋電腦...", "Starting Wi-Fi & searching...");
            case CONNECTING:
                return t(zh, "連線至 " + d, "Connecting to " + d);
            case SEARCHING:
                return t(zh, "搜尋區網電腦中...", "Searching LAN PC...");
            case CONNECTED:
                return t(zh, "已連線 " + d, "Connected " + d);
            case STREAMING:
                return t(zh, "背景傳送中 → " + d, "Streaming → " + d);
            case MEASURING:
                return t(zh, "量測心率中 → " + d, "Measuring HR → " + d);
            case RETRYING:
                return t(zh, "傳送重試中 (" + d + ")", "Retrying (" + d + ")");
            case STOPPED:
                return t(zh, "已停止", "Stopped");
            case CLEARED:
                return t(zh, "已清除，開始時將自動搜尋", "Cleared; auto-search on start");
            case NEED_SENSOR_PERM:
                return t(zh, "請允許身體感測器權限", "Sensor permission required");
            case NO_SENSOR_SERVICE:
                return t(zh, "找不到感測器服務", "Sensor service unavailable");
            case NO_HR_SENSOR:
                return t(zh, "裝置無心率感測器", "No heart rate sensor found");
            default:
                return t(zh, "待機中", "Idle");
        }
    }
}
