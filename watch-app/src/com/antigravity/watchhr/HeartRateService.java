package com.antigravity.watchhr;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.SharedPreferences;
import android.content.pm.ServiceInfo;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.NetworkRequest;
import android.net.wifi.WifiManager;
import android.os.BatteryManager;
import android.os.Build;
import android.os.IBinder;
import android.os.PowerManager;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InterfaceAddress;
import java.net.NetworkInterface;
import java.net.SocketTimeoutException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

public class HeartRateService extends Service implements SensorEventListener {
    public static final String ACTION_START = "com.antigravity.watchhr.START";
    public static final String ACTION_STOP = "com.antigravity.watchhr.STOP";
    public static final String EXTRA_TARGET_IP = "target_ip";

    public static final String PREFS_NAME = "watch_hr_prefs";
    public static final String KEY_TARGET_IP = "target_ip";
    public static final int DEFAULT_PORT = 19888;

    private static final String CHANNEL_ID = "watch_hr_channel";
    private static final int NOTIFICATION_ID = 1001;
    private static final long ZERO_GLITCH_HOLD_MS = 12_000L;

    private SensorManager sensorManager;
    private PowerManager.WakeLock wakeLock;
    private WifiManager.WifiLock wifiLock;
    private ConnectivityManager connectivityManager;
    private ConnectivityManager.NetworkCallback wifiNetworkCallback;

    private ScheduledExecutorService executor;
    private volatile DatagramSocket socket;
    private volatile Network boundWifiNetwork;
    private volatile String targetIp = "";
    private volatile int latestBpm = 0;
    private volatile int latestAccuracy = 0;
    private volatile long lastValidBpmMs = 0L;
    private volatile boolean running = false;

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        String action = intent != null ? intent.getAction() : ACTION_START;
        if (ACTION_STOP.equals(action)) {
            stopSelf();
            return START_NOT_STICKY;
        }

        String requestedIp = intent != null ? intent.getStringExtra(EXTRA_TARGET_IP) : "";
        targetIp = sanitizeIp(requestedIp);

        createNotificationChannel();
        boolean zh = LangHelper.isChinese(this);
        startAsHealthForeground(LangHelper.t(zh, "心率區網背景傳輸中", "Streaming heart rate over LAN"));

        if (!running) {
            startStreaming();
        } else {
            AppState.updateStatus(
                targetIp.isEmpty() ? AppState.StatusCode.SEARCHING : AppState.StatusCode.CONNECTING,
                targetIp,
                targetIp
            );
        }

        return START_STICKY;
    }

    private void startAsHealthForeground(String text) {
        Notification notification = buildNotification(text);
        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_HEALTH);
        } else {
            startForeground(NOTIFICATION_ID, notification);
        }
    }

    private void startStreaming() {
        running = true;
        latestBpm = 0;
        lastValidBpmMs = 0L;
        AppState.update(
            true,
            0,
            targetIp.isEmpty() ? AppState.StatusCode.STARTING_SEARCH : AppState.StatusCode.CONNECTING,
            targetIp,
            targetIp
        );

        acquireLocks();
        requestWifiNetwork();
        registerHeartRateSensor();

        executor = Executors.newSingleThreadScheduledExecutor();
        executor.scheduleWithFixedDelay(this::tickNetwork, 200, 1000, TimeUnit.MILLISECONDS);
    }

    private void acquireLocks() {
        PowerManager pm = (PowerManager) getSystemService(Context.POWER_SERVICE);
        if (pm != null) {
            wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "WatchHR::StreamWakeLock");
            wakeLock.setReferenceCounted(false);
            wakeLock.acquire(12 * 60 * 60 * 1000L);
        }

        WifiManager wm = (WifiManager) getApplicationContext().getSystemService(Context.WIFI_SERVICE);
        if (wm != null) {
            wifiLock = wm.createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "WatchHR::WifiLock");
            wifiLock.setReferenceCounted(false);
            wifiLock.acquire();
        }
    }

    private void requestWifiNetwork() {
        connectivityManager = (ConnectivityManager) getSystemService(Context.CONNECTIVITY_SERVICE);
        if (connectivityManager == null) {
            return;
        }

        NetworkRequest request = new NetworkRequest.Builder()
            .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
            .build();

        wifiNetworkCallback = new ConnectivityManager.NetworkCallback() {
            @Override
            public void onAvailable(Network network) {
                boundWifiNetwork = network;
                rebindSocketToWifi();
            }

            @Override
            public void onLost(Network network) {
                if (network.equals(boundWifiNetwork)) {
                    boundWifiNetwork = null;
                }
            }
        };

        try {
            connectivityManager.requestNetwork(request, wifiNetworkCallback);
        } catch (Exception ignored) {
        }
    }

    private synchronized void rebindSocketToWifi() {
        closeSocket();
    }

    private void registerHeartRateSensor() {
        sensorManager = (SensorManager) getSystemService(Context.SENSOR_SERVICE);
        if (sensorManager == null) {
            AppState.updateStatus(AppState.StatusCode.NO_SENSOR_SERVICE, "", targetIp);
            return;
        }

        Sensor hrSensor = sensorManager.getDefaultSensor(Sensor.TYPE_HEART_RATE);
        if (hrSensor == null) {
            AppState.updateStatus(AppState.StatusCode.NO_HR_SENSOR, "", targetIp);
            return;
        }

        sensorManager.registerListener(this, hrSensor, SensorManager.SENSOR_DELAY_NORMAL);
    }

    private void tickNetwork() {
        if (!running) {
            return;
        }

        try {
            DatagramSocket sock = ensureSocket();
            if (targetIp.isEmpty()) {
                AppState.updateStatus(AppState.StatusCode.SEARCHING, "", "");
                String foundIp = discoverPc(sock);
                if (!foundIp.isEmpty()) {
                    targetIp = foundIp;
                    saveDiscoveredIp(foundIp);
                    AppState.updateStatus(AppState.StatusCode.CONNECTED, foundIp, foundIp);
                } else {
                    return;
                }
            }

            sendHeartRatePacket(sock, targetIp);
        } catch (Exception ex) {
            AppState.updateStatus(AppState.StatusCode.RETRYING, ex.getClass().getSimpleName(), targetIp);
            closeSocket();
        }
    }

    private synchronized DatagramSocket ensureSocket() throws Exception {
        if (socket != null && !socket.isClosed()) {
            return socket;
        }

        DatagramSocket s = new DatagramSocket();
        s.setBroadcast(true);
        s.setSoTimeout(450);

        Network wifiNet = boundWifiNetwork;
        if (wifiNet != null) {
            try {
                wifiNet.bindSocket(s);
            } catch (Exception ignored) {
            }
        }

        socket = s;
        return s;
    }

    private String discoverPc(DatagramSocket sock) {
        byte[] probe = "{\"type\":\"discover\",\"device\":\"GalaxyWatch\"}".getBytes(StandardCharsets.UTF_8);

        for (InetAddress broadcastAddr : getBroadcastAddresses()) {
            try {
                DatagramPacket packet = new DatagramPacket(probe, probe.length, broadcastAddr, DEFAULT_PORT);
                sock.send(packet);
            } catch (Exception ignored) {
            }
        }

        byte[] buf = new byte[512];
        DatagramPacket reply = new DatagramPacket(buf, buf.length);
        try {
            sock.receive(reply);
            String body = new String(reply.getData(), 0, reply.getLength(), StandardCharsets.UTF_8);
            if (body.contains("\"offer\"") && reply.getAddress() != null) {
                return reply.getAddress().getHostAddress();
            }
        } catch (SocketTimeoutException ignored) {
        } catch (Exception ignored) {
        }
        return "";
    }

    private List<InetAddress> getBroadcastAddresses() {
        List<InetAddress> addresses = new ArrayList<>();
        try {
            for (NetworkInterface nic : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!nic.isUp() || nic.isLoopback()) {
                    continue;
                }
                for (InterfaceAddress ifAddr : nic.getInterfaceAddresses()) {
                    InetAddress bcast = ifAddr.getBroadcast();
                    if (bcast != null && !addresses.contains(bcast)) {
                        addresses.add(bcast);
                    }
                }
            }
        } catch (Exception ignored) {
        }

        try {
            InetAddress global = InetAddress.getByName("255.255.255.255");
            if (!addresses.contains(global)) {
                addresses.add(global);
            }
        } catch (Exception ignored) {
        }
        return addresses;
    }

    private void sendHeartRatePacket(DatagramSocket sock, String ip) throws Exception {
        long ts = System.currentTimeMillis() / 1000L;
        int battery = readBatteryPercent();
        String json = "{\"type\":\"hr\",\"bpm\":" + latestBpm
            + ",\"ts\":" + ts
            + ",\"battery\":" + battery
            + ",\"accuracy\":" + latestAccuracy + "}";

        byte[] bytes = json.getBytes(StandardCharsets.UTF_8);
        InetAddress addr = InetAddress.getByName(ip);
        DatagramPacket packet = new DatagramPacket(bytes, bytes.length, addr, DEFAULT_PORT);
        sock.send(packet);

        AppState.StatusCode code = latestBpm > 0 ? AppState.StatusCode.STREAMING : AppState.StatusCode.MEASURING;
        AppState.updateStatus(code, ip, ip);
    }

    private int readBatteryPercent() {
        Intent status = registerReceiver(null, new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
        if (status == null) {
            return -1;
        }
        int level = status.getIntExtra(BatteryManager.EXTRA_LEVEL, -1);
        int scale = status.getIntExtra(BatteryManager.EXTRA_SCALE, -1);
        if (level < 0 || scale <= 0) {
            return -1;
        }
        return Math.round((level * 100f) / scale);
    }

    private void saveDiscoveredIp(String ip) {
        SharedPreferences prefs = getSharedPreferences(PREFS_NAME, MODE_PRIVATE);
        prefs.edit().putString(KEY_TARGET_IP, ip).apply();
    }

    @Override
    public void onSensorChanged(SensorEvent event) {
        if (event == null || event.sensor == null || event.sensor.getType() != Sensor.TYPE_HEART_RATE) {
            return;
        }
        if (event.values == null || event.values.length == 0) {
            return;
        }

        int bpm = Math.round(event.values[0]);
        if (bpm < 0 || bpm > 250) {
            return;
        }

        long now = System.currentTimeMillis();
        if (bpm == 0) {
            // Hold last valid BPM briefly during optical wrist-motion glitches (F:-10)
            if (latestBpm > 0 && (now - lastValidBpmMs) < ZERO_GLITCH_HOLD_MS) {
                return;
            }
            latestBpm = 0;
            latestAccuracy = event.accuracy;
            AppState.updateBpm(0);
            return;
        }

        latestBpm = bpm;
        lastValidBpmMs = now;
        latestAccuracy = event.accuracy;
        AppState.updateBpm(bpm);

        final ScheduledExecutorService exec = executor;
        final String ip = targetIp;
        if (running && exec != null && !exec.isShutdown() && !ip.isEmpty()) {
            exec.execute(() -> {
                try {
                    sendHeartRatePacket(ensureSocket(), ip);
                } catch (Exception ignored) {
                }
            });
        }
    }

    @Override
    public void onAccuracyChanged(Sensor sensor, int accuracy) {
        latestAccuracy = accuracy;
    }

    private static String sanitizeIp(String raw) {
        if (raw == null) {
            return "";
        }
        String trimmed = raw.trim();
        if (trimmed.matches("^\\d{1,3}(\\.\\d{1,3}){3}$")) {
            return trimmed;
        }
        return "";
    }

    private void createNotificationChannel() {
        boolean zh = LangHelper.isChinese(this);
        NotificationChannel channel = new NotificationChannel(
            CHANNEL_ID,
            LangHelper.t(zh, "區網心率傳輸", "LAN Heart Rate"),
            NotificationManager.IMPORTANCE_LOW
        );
        NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm != null) {
            nm.createNotificationChannel(channel);
        }
    }

    private Notification buildNotification(String text) {
        boolean zh = LangHelper.isChinese(this);
        Intent openIntent = new Intent(this, MainActivity.class);
        PendingIntent pi = PendingIntent.getActivity(
            this,
            0,
            openIntent,
            PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE
        );

        return new Notification.Builder(this, CHANNEL_ID)
            .setContentTitle(LangHelper.t(zh, "區網心率監測", "LAN Heart Rate"))
            .setContentText(text)
            .setSmallIcon(android.R.drawable.ic_menu_compass)
            .setOngoing(true)
            .setCategory(Notification.CATEGORY_WORKOUT)
            .setContentIntent(pi)
            .build();
    }

    private synchronized void closeSocket() {
        if (socket != null) {
            socket.close();
            socket = null;
        }
    }

    @Override
    public void onDestroy() {
        running = false;
        if (sensorManager != null) {
            sensorManager.unregisterListener(this);
        }
        if (executor != null) {
            executor.shutdownNow();
            executor = null;
        }
        closeSocket();

        if (connectivityManager != null && wifiNetworkCallback != null) {
            try {
                connectivityManager.unregisterNetworkCallback(wifiNetworkCallback);
            } catch (Exception ignored) {
            }
            wifiNetworkCallback = null;
        }

        if (wifiLock != null && wifiLock.isHeld()) {
            wifiLock.release();
        }
        if (wakeLock != null && wakeLock.isHeld()) {
            wakeLock.release();
        }

        AppState.update(false, 0, AppState.StatusCode.STOPPED, "", targetIp);
        super.onDestroy();
    }
}
