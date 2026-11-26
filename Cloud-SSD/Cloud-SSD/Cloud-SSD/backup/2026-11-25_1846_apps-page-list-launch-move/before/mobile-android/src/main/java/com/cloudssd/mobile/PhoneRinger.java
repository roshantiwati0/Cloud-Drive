package com.cloudssd.mobile;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.media.AudioAttributes;
import android.media.AudioManager;
import android.media.MediaPlayer;
import android.media.RingtoneManager;
import android.net.Uri;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.VibrationEffect;
import android.os.Vibrator;

/**
 * "Ring" from the PC: the alarm sound at full alarm volume (so it is heard on silent) and vibration for 20 seconds,
 * with a note that says who asked. Tapping or swiping the note, its Stop button, or touching the alarm volume (a
 * volume key or the slider) stops it. The alarm volume is put back afterwards.
 */
final class PhoneRinger {
    private static final int NOTE_ID = 7702;
    private static final long RING_MS = 20_000;
    private static final String VOLUME_CHANGED = "android.media.VOLUME_CHANGED_ACTION";
    private static MediaPlayer player;
    private static int oldVolume = -1;
    private static int ringVolume = -1;
    private static BroadcastReceiver volumeWatch;
    private static final Handler main = new Handler(Looper.getMainLooper());
    /** Ringing now (the lock-screen "Found it" screen closes when this turns false). */
    private static volatile boolean ringing;

    private PhoneRinger() {
    }

    static void ring(Context context, final String from) {
        final Context c = context.getApplicationContext() != null ? context.getApplicationContext() : context;
        main.post(() -> {
            stopNow(c);
            AudioManager am = (AudioManager) c.getSystemService(Context.AUDIO_SERVICE);
            try {
                Uri uri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM);
                if (uri == null) uri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_RINGTONE);
                if (am != null) {
                    oldVolume = am.getStreamVolume(AudioManager.STREAM_ALARM);
                    ringVolume = am.getStreamMaxVolume(AudioManager.STREAM_ALARM);
                    am.setStreamVolume(AudioManager.STREAM_ALARM, ringVolume, 0);
                }
                MediaPlayer mp = new MediaPlayer();
                mp.setAudioAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_ALARM)
                        .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build());
                mp.setDataSource(c, uri);
                mp.setLooping(true);
                mp.prepare();
                mp.start();
                player = mp;
            } catch (Exception ignored) {
                // No sound available: the vibration and the note still help.
            }
            Vibrator v = (Vibrator) c.getSystemService(Context.VIBRATOR_SERVICE);
            if (v != null && v.hasVibrator()) {
                long[] pattern = {0, 800, 400};
                if (Build.VERSION.SDK_INT >= 26) v.vibrate(VibrationEffect.createWaveform(pattern, 0));
                else v.vibrate(pattern, 0);
            }
            ringing = true;
            showNote(c, from);
            watchVolume(c);
            main.postDelayed(() -> stopNow(c), RING_MS);
        });
    }

    static void stop(final Context c) {
        main.post(() -> stopNow(c));
    }

    /**
     * Someone pressing a volume key or moving a volume slider while it rings has found the phone: stop (the alarm
     * volume is put back). Volume keys usually move the media volume, not the alarm's, so any stream counts; only
     * this ringer's own "alarm to full" is ignored.
     */
    private static void watchVolume(final Context c) {
        BroadcastReceiver r = new BroadcastReceiver() {
            @Override
            public void onReceive(Context ctx, Intent i) {
                int stream = i.getIntExtra("android.media.EXTRA_VOLUME_STREAM_TYPE", -1);
                int now = i.getIntExtra("android.media.EXTRA_VOLUME_STREAM_VALUE", -1);
                int before = i.getIntExtra("android.media.EXTRA_PREV_VOLUME_STREAM_VALUE", -1);
                if (stream < 0 || now < 0 || now == before) return;
                if (stream == AudioManager.STREAM_ALARM && now == ringVolume) return;
                stopNow(c);
            }
        };
        try {
            // A system-only broadcast, so no other app can send it; exported is needed to hear it on Android 13+.
            if (Build.VERSION.SDK_INT >= 33) c.registerReceiver(r, new IntentFilter(VOLUME_CHANGED), null, main, Context.RECEIVER_EXPORTED);
            else c.registerReceiver(r, new IntentFilter(VOLUME_CHANGED), null, main);
            volumeWatch = r;
        } catch (Exception ignored) {
            // Without it the note, its Stop button and the 20 second limit still stop the ring.
        }
    }

    static boolean isRinging() {
        return ringing;
    }

    private static void stopNow(Context c) {
        ringing = false;
        main.removeCallbacksAndMessages(null);
        if (volumeWatch != null) {
            try {
                c.unregisterReceiver(volumeWatch);
            } catch (Exception ignored) {
            }
            volumeWatch = null;
        }
        ringVolume = -1;
        if (player != null) {
            try {
                player.stop();
            } catch (Exception ignored) {
            }
            player.release();
            player = null;
        }
        AudioManager am = (AudioManager) c.getSystemService(Context.AUDIO_SERVICE);
        if (am != null && oldVolume >= 0) {
            try {
                am.setStreamVolume(AudioManager.STREAM_ALARM, oldVolume, 0);
            } catch (Exception ignored) {
            }
            oldVolume = -1;
        }
        Vibrator v = (Vibrator) c.getSystemService(Context.VIBRATOR_SERVICE);
        if (v != null) v.cancel();
        NotificationManager nm = (NotificationManager) c.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm != null) nm.cancel(NOTE_ID);
    }

    private static void showNote(Context c, String from) {
        NotificationManager nm = (NotificationManager) c.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm == null) return;
        if (Build.VERSION.SDK_INT >= 26 && nm.getNotificationChannel(PeerLink.CHANNEL) == null)
            nm.createNotificationChannel(new NotificationChannel(PeerLink.CHANNEL, "Requests from your PC", NotificationManager.IMPORTANCE_HIGH));
        int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 23 ? PendingIntent.FLAG_IMMUTABLE : 0);
        PendingIntent stop = PendingIntent.getBroadcast(c, 3, new Intent(c, PeerDecisionReceiver.class).putExtra("stopRing", true), flags);
        Notification.Builder b = Build.VERSION.SDK_INT >= 26 ? new Notification.Builder(c, PeerLink.CHANNEL) : new Notification.Builder(c);
        b.setSmallIcon(android.R.drawable.ic_lock_idle_alarm)
                .setContentTitle(Tr.t(c, "{0} is ringing this phone", from == null || from.isEmpty() ? Tr.t(c, "Your PC") : from))
                .setContentText(Tr.t(c, "Found it? Tap here to stop."))
                .setContentIntent(stop)
                .setAutoCancel(true)
                .setDeleteIntent(stop)
                .setCategory(Notification.CATEGORY_ALARM)
                .addAction(new Notification.Action.Builder(null, Tr.t(c, "Stop"), stop).build());
        // On a locked or sleeping phone: a full screen with a big "Found it" button, over the lock screen (Android 14+
        // allows it once the person let the app show full-screen notes; else the note shows as usual).
        PendingIntent full = PendingIntent.getActivity(c, 4, new Intent(c, RingActivity.class).putExtra("from", from == null ? "" : from)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_NO_USER_ACTION), flags);
        b.setFullScreenIntent(full, true);
        try {
            nm.notify(NOTE_ID, b.build());
        } catch (SecurityException ignored) {
        }
    }
}
