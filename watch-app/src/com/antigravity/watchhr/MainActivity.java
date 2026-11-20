package com.antigravity.watchhr;

import android.Manifest;
import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.PowerManager;
import android.provider.Settings;
import android.widget.Button;
import android.widget.EditText;
import android.widget.TextView;

import java.util.ArrayList;
import java.util.List;

public class MainActivity extends Activity implements AppState.Listener {
    private static final int REQ_FOREGROUND_PERMS = 101;
    private static final int REQ_BACKGROUND_SENSOR = 102;
    private static final String PERM_READ_HEART_RATE = "android.permission.health.READ_HEART_RATE";
    private static final String PERM_READ_HEALTH_BG = "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND";

    private TextView txtTitle;
    private TextView txtBpm;
    private TextView txtStatus;
    private Button btnToggle;
    private TextView txtIpLabel;
    private EditText edtTargetIp;
    private Button btnAutoDiscover;
    private Button btnLanguage;

    private boolean pendingStartAfterPermission = false;
    private boolean promptedBackgroundOnce = false;
    private boolean promptedBatteryOnce = false;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        txtTitle = findViewById(R.id.txtTitle);
        txtBpm = findViewById(R.id.txtBpm);
        txtStatus = findViewById(R.id.txtStatus);
        btnToggle = findViewById(R.id.btnToggle);
        txtIpLabel = findViewById(R.id.txtIpLabel);
        edtTargetIp = findViewById(R.id.edtTargetIp);
        btnAutoDiscover = findViewById(R.id.btnAutoDiscover);
        btnLanguage = findViewById(R.id.btnLanguage);

        SharedPreferences prefs = getSharedPreferences(HeartRateService.PREFS_NAME, MODE_PRIVATE);
        String savedIp = prefs.getString(HeartRateService.KEY_TARGET_IP, "");
        edtTargetIp.setText(savedIp);

        btnToggle.setOnClickListener(v -> onToggleClicked());
        btnAutoDiscover.setOnClickListener(v -> onAutoDiscoverClicked());
        btnLanguage.setOnClickListener(v -> onLanguageToggleClicked());

        applyStaticStrings();
    }

    @Override
    protected void onStart() {
        super.onStart();
        applyStaticStrings();
        AppState.setListener(this);
    }

    @Override
    protected void onStop() {
        AppState.setListener(null);
        super.onStop();
    }

    private void applyStaticStrings() {
        boolean zh = LangHelper.isChinese(this);
        txtTitle.setText(LangHelper.t(zh, "♥ 區網心率", "♥ LAN Heart Rate"));
        txtIpLabel.setText(LangHelper.t(zh, "電腦 IP（留空為自動搜尋）", "PC IP (blank = auto-find)"));
        edtTargetIp.setHint(LangHelper.t(zh, "自動搜尋", "Auto-find"));
        btnAutoDiscover.setText(LangHelper.t(zh, "清除並自動搜尋", "Clear & Auto-find"));
        btnLanguage.setText(LangHelper.t(zh, "語言：繁體中文 / EN", "Lang: English / 繁中"));
    }

    private void onLanguageToggleClicked() {
        LangHelper.toggleLanguage(this);
        applyStaticStrings();
        AppState.refresh();
    }

    private void onToggleClicked() {
        if (AppState.isRunning()) {
            stopHeartRateService();
            return;
        }

        if (!ensureForegroundPermissions()) {
            pendingStartAfterPermission = true;
            return;
        }

        if (!ensureBackgroundSensorPermission()) {
            pendingStartAfterPermission = true;
            return;
        }

        maybeRequestBatteryOptimizationExemption();
        startHeartRateService(edtTargetIp.getText().toString().trim());
    }

    private void onAutoDiscoverClicked() {
        edtTargetIp.setText("");
        SharedPreferences prefs = getSharedPreferences(HeartRateService.PREFS_NAME, MODE_PRIVATE);
        prefs.edit().remove(HeartRateService.KEY_TARGET_IP).apply();

        if (AppState.isRunning()) {
            startHeartRateService("");
        } else {
            AppState.updateStatus(AppState.StatusCode.CLEARED, "", "");
        }
    }

    private boolean ensureForegroundPermissions() {
        List<String> needed = new ArrayList<>();
        if (checkSelfPermission(Manifest.permission.BODY_SENSORS) != PackageManager.PERMISSION_GRANTED) {
            needed.add(Manifest.permission.BODY_SENSORS);
        }
        if (Build.VERSION.SDK_INT >= 36
            && checkSelfPermission(PERM_READ_HEART_RATE) != PackageManager.PERMISSION_GRANTED) {
            needed.add(PERM_READ_HEART_RATE);
        }
        if (Build.VERSION.SDK_INT >= 33
            && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            needed.add(Manifest.permission.POST_NOTIFICATIONS);
        }

        if (needed.isEmpty()) {
            return true;
        }

        requestPermissions(needed.toArray(new String[0]), REQ_FOREGROUND_PERMS);
        return false;
    }

    private boolean ensureBackgroundSensorPermission() {
        if (Build.VERSION.SDK_INT < 33 || promptedBackgroundOnce) {
            return true;
        }

        List<String> needed = new ArrayList<>();
        if (checkSelfPermission(Manifest.permission.BODY_SENSORS_BACKGROUND) != PackageManager.PERMISSION_GRANTED) {
            needed.add(Manifest.permission.BODY_SENSORS_BACKGROUND);
        }
        if (Build.VERSION.SDK_INT >= 36
            && checkSelfPermission(PERM_READ_HEALTH_BG) != PackageManager.PERMISSION_GRANTED) {
            needed.add(PERM_READ_HEALTH_BG);
        }
        if (needed.isEmpty()) {
            return true;
        }

        promptedBackgroundOnce = true;
        requestPermissions(needed.toArray(new String[0]), REQ_BACKGROUND_SENSOR);
        return false;
    }

    private void maybeRequestBatteryOptimizationExemption() {
        if (promptedBatteryOnce) {
            return;
        }
        PowerManager pm = (PowerManager) getSystemService(Context.POWER_SERVICE);
        if (pm == null || pm.isIgnoringBatteryOptimizations(getPackageName())) {
            return;
        }

        promptedBatteryOnce = true;
        try {
            Intent intent = new Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS);
            intent.setData(Uri.parse("package:" + getPackageName()));
            startActivity(intent);
        } catch (Exception ignored) {
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == REQ_FOREGROUND_PERMS) {
            boolean sensorsGranted = checkSelfPermission(Manifest.permission.BODY_SENSORS)
                == PackageManager.PERMISSION_GRANTED;

            if (!sensorsGranted) {
                pendingStartAfterPermission = false;
                AppState.updateStatus(AppState.StatusCode.NEED_SENSOR_PERM, "", "");
                return;
            }

            if (pendingStartAfterPermission && ensureBackgroundSensorPermission()) {
                pendingStartAfterPermission = false;
                maybeRequestBatteryOptimizationExemption();
                startHeartRateService(edtTargetIp.getText().toString().trim());
            }
            return;
        }

        if (requestCode == REQ_BACKGROUND_SENSOR && pendingStartAfterPermission) {
            pendingStartAfterPermission = false;
            maybeRequestBatteryOptimizationExemption();
            startHeartRateService(edtTargetIp.getText().toString().trim());
        }
    }

    private void startHeartRateService(String targetIp) {
        SharedPreferences prefs = getSharedPreferences(HeartRateService.PREFS_NAME, MODE_PRIVATE);
        prefs.edit().putString(HeartRateService.KEY_TARGET_IP, targetIp).apply();

        Intent intent = new Intent(this, HeartRateService.class);
        intent.setAction(HeartRateService.ACTION_START);
        intent.putExtra(HeartRateService.EXTRA_TARGET_IP, targetIp);
        startForegroundService(intent);
    }

    private void stopHeartRateService() {
        Intent intent = new Intent(this, HeartRateService.class);
        intent.setAction(HeartRateService.ACTION_STOP);
        startService(intent);
    }

    @Override
    public void onStateChanged(boolean running, int bpm, AppState.StatusCode statusCode, String statusDetail, String targetIp) {
        boolean zh = LangHelper.isChinese(this);
        txtBpm.setText(bpm > 0 ? String.valueOf(bpm) : "--");
        txtStatus.setText(LangHelper.formatStatus(zh, statusCode, statusDetail));

        btnToggle.setText(running ? LangHelper.t(zh, "停止傳輸", "Stop") : LangHelper.t(zh, "開始傳輸", "Start"));
        btnToggle.setBackgroundResource(running ? R.drawable.bg_btn_stop : R.drawable.bg_btn_start);

        if (targetIp != null && !targetIp.isEmpty() && edtTargetIp.getText().toString().trim().isEmpty()) {
            edtTargetIp.setText(targetIp);
        }
    }
}
