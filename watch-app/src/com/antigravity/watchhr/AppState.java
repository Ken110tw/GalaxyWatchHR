package com.antigravity.watchhr;

import android.os.Handler;
import android.os.Looper;

public final class AppState {
    public enum StatusCode {
        IDLE,
        STARTING_SEARCH,
        CONNECTING,
        SEARCHING,
        CONNECTED,
        STREAMING,
        MEASURING,
        RETRYING,
        STOPPED,
        CLEARED,
        NEED_SENSOR_PERM,
        NO_SENSOR_SERVICE,
        NO_HR_SENSOR
    }

    public interface Listener {
        void onStateChanged(boolean running, int bpm, StatusCode statusCode, String statusDetail, String targetIp);
    }

    private static final Handler MAIN_HANDLER = new Handler(Looper.getMainLooper());
    private static volatile boolean running = false;
    private static volatile int currentBpm = 0;
    private static volatile StatusCode statusCode = StatusCode.IDLE;
    private static volatile String statusDetail = "";
    private static volatile String activeTargetIp = "";
    private static volatile Listener listener = null;

    private AppState() {
    }

    public static void setListener(Listener l) {
        listener = l;
        notifyListener();
    }

    public static boolean isRunning() {
        return running;
    }

    public static void update(boolean isRunning, int bpm, StatusCode code, String detail, String targetIp) {
        running = isRunning;
        currentBpm = bpm;
        statusCode = code;
        statusDetail = detail == null ? "" : detail;
        activeTargetIp = targetIp == null ? "" : targetIp;
        notifyListener();
    }

    public static void updateBpm(int bpm) {
        currentBpm = bpm;
        notifyListener();
    }

    public static void updateStatus(StatusCode code, String detail, String targetIp) {
        statusCode = code;
        statusDetail = detail == null ? "" : detail;
        if (targetIp != null) {
            activeTargetIp = targetIp;
        }
        notifyListener();
    }

    public static void refresh() {
        notifyListener();
    }

    private static void notifyListener() {
        final Listener l = listener;
        if (l == null) {
            return;
        }
        final boolean r = running;
        final int b = currentBpm;
        final StatusCode sc = statusCode;
        final String sd = statusDetail;
        final String ip = activeTargetIp;
        MAIN_HANDLER.post(() -> l.onStateChanged(r, b, sc, sd, ip));
    }
}
