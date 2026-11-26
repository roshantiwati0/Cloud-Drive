package com.cloudssd.mobile;

import android.app.NotificationManager;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** "Allow 1 hour" or "Deny" on the note that the PC wants to see this phone's files. */
public class PeerDecisionReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        if (intent.getBooleanExtra("stopRing", false)) {
            PhoneRinger.stop(context);
            return;
        }
        if (intent.getBooleanExtra("stopListening", false)) {
            // "Stop" on the listening note: quiet until the app is opened again.
            context.getSharedPreferences("cloudssd", Context.MODE_PRIVATE).edit().putBoolean("peer_paused", true).apply();
            context.stopService(new Intent(context, PeerLinkService.class));
            return;
        }
        String pc = intent.getStringExtra("pc");
        int minutes = intent.getIntExtra("minutes", -1);
        if (pc != null && minutes >= 0) {
            // The note for adding files allows that too; the plain "see files" note keeps what was allowed before.
            if (intent.hasExtra("write")) PeerLink.Grants.set(context, pc, intent.getStringExtra("scope"), minutes, intent.getBooleanExtra("write", false));
            else PeerLink.Grants.set(context, pc, intent.getStringExtra("scope"), minutes);
        }
        NotificationManager nm = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm != null) nm.cancel(PeerLink.NOTE_ID);
        // Allowed from the note with the app closed: keep answering for as long as it was allowed.
        if (pc != null && minutes >= 0) PeerLinkService.ensure(context);
    }
}
