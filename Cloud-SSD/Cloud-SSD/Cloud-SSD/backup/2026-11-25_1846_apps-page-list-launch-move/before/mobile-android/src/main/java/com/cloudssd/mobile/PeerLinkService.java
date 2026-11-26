package com.cloudssd.mobile;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.Build;
import android.os.IBinder;

import org.json.JSONObject;

/**
 * Keeps this phone answering its PC with the app closed: Ring, the PC looking at the phone's files, the clipboard.
 * Android (and phones like vivo) end an app soon after it leaves the screen; a foreground service with a quiet note
 * keeps it. It runs only while it is needed ({@link PeerLink#neededInBackground}): "Find my phone" is on, or the PC
 * may look at the files right now. The note's Stop ends it until the app is opened again.
 */
public class PeerLinkService extends Service {
    static final int NOTE_ID = 7703;
    /** Low, not min: no sound, but its Stop button shows (vivo hides the buttons of min notes under "Silent"). */
    private static final String CHANNEL = "peer_listen";
    private static volatile boolean running;

    /** Starts it when needed. Only from the app, a note's button or boot: Android refuses it from the background. */
    static void ensure(Context c) {
        if (!PeerLink.neededInBackground(c)) {
            stopIfNotNeeded(c);
            return;
        }
        if (running) return;
        Intent i = new Intent(c, PeerLinkService.class);
        try {
            if (Build.VERSION.SDK_INT >= 26) c.startForegroundService(i);
            else c.startService(i);
        } catch (Exception ignored) {
            // Not allowed right now (in the background on Android 12+): the app's next start tries again.
        }
    }

    static void stopIfNotNeeded(Context c) {
        if (running && !PeerLink.neededInBackground(c)) c.stopService(new Intent(c, PeerLinkService.class));
    }

    static boolean isRunning() {
        return running;
    }

    /** Shows the note again (in the language just picked); only from the app on screen. */
    static void relabel(Context c) {
        if (!running) return;
        try {
            c.startService(new Intent(c, PeerLinkService.class));
        } catch (Exception ignored) {
            // Not allowed right now: the note keeps its old words until the next start.
        }
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        Notification n = note();
        try {
            if (Build.VERSION.SDK_INT >= 29) startForeground(NOTE_ID, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE);
            else startForeground(NOTE_ID, n);
        } catch (Exception e) {
            // Refused (no longer allowed, or the type is not permitted on this phone): give up quietly.
            stopSelf();
            return START_NOT_STICKY;
        }
        running = true;
        PeerLink.get(this).start();
        PeerLink.get(this).kick();
        if (!PeerLink.neededInBackground(this)) stopSelf();
        return START_STICKY;
    }

    @Override
    public void onDestroy() {
        running = false;
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    private Notification note() {
        NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
        if (Build.VERSION.SDK_INT >= 26 && nm != null && nm.getNotificationChannel(CHANNEL) == null) {
            nm.deleteNotificationChannel("peer_bg");   // the first (min) one, from a test build
            NotificationChannel ch = new NotificationChannel(CHANNEL, "Listening for your PC", NotificationManager.IMPORTANCE_LOW);
            ch.setShowBadge(false);
            nm.createNotificationChannel(ch);
        }
        String pc = PcProfiles.activeId(this);
        JSONObject rec = pc == null ? null : PcProfiles.get(this, pc);
        String name = rec == null ? "" : rec.optString("label", rec.optString("name", ""));
        if (name.isEmpty()) name = Tr.t(this, "your PC");
        int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 23 ? PendingIntent.FLAG_IMMUTABLE : 0);
        // Tapping the note opens Settings → Find my phone, where it can be switched off: some phones (vivo) do not show
        // a quiet note's Stop button at all.
        PendingIntent open = PendingIntent.getActivity(this, 10, new Intent(this, MainActivity.class).setAction(MainActivity.ACTION_FIND_MY_PHONE)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP), flags);
        PendingIntent stop = PendingIntent.getBroadcast(this, 11, new Intent(this, PeerDecisionReceiver.class).putExtra("stopListening", true), flags);
        boolean find = getSharedPreferences("cloudssd", Context.MODE_PRIVATE).getBoolean("find_my_phone", false);
        Notification.Builder b = Build.VERSION.SDK_INT >= 26 ? new Notification.Builder(this, CHANNEL) : new Notification.Builder(this);
        b.setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
                .setContentTitle(Tr.t(this, "Cloud-SSD is listening for {0}", name))
                .setContentText(Tr.t(this, find ? "So it can ring this phone even with the app closed. Tap to switch it off."
                        : "It may look at this phone's files for now. Tap to see or stop it."))
                .setContentIntent(open)
                .setOngoing(true)
                .addAction(new Notification.Action.Builder(null, Tr.t(this, "Stop"), stop).build());
        if (Build.VERSION.SDK_INT < 26) b.setPriority(Notification.PRIORITY_LOW);
        return b.build();
    }
}
