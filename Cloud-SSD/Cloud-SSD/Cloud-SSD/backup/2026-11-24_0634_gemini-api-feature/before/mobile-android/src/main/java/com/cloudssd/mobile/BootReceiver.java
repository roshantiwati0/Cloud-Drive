package com.cloudssd.mobile;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** After the phone restarts: "Find my phone" keeps listening for the PC without the app being opened first. */
public class BootReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        if (!Intent.ACTION_BOOT_COMPLETED.equals(intent.getAction()) && !"android.intent.action.MY_PACKAGE_REPLACED".equals(intent.getAction())) return;
        context.getSharedPreferences("cloudssd", Context.MODE_PRIVATE).edit().remove("peer_paused").apply();
        PeerLinkService.ensure(context);
    }
}
