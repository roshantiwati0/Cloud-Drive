package com.cloudssd.mobile;

import android.app.Activity;
import android.app.KeyguardManager;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.hardware.biometrics.BiometricManager;
import android.hardware.biometrics.BiometricPrompt;
import android.os.Build;
import android.os.CancellationSignal;
import android.os.SystemClock;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * App lock: the app asks for the phone's fingerprint, face or screen lock when it opens, and when it comes back after
 * the chosen time. Until then a cover hides the page (also in the recent-apps picture when "right away" is chosen).
 * Android 10+ uses its own biometric dialog (falling back to the PIN or pattern); Android 7–9 its screen-lock check.
 * No libraries. Off unless switched on in Settings, and only when the phone has a screen lock at all.
 */
final class AppLock {
    static final int REQ_UNLOCK = 4101;
    private static final String PREFS = "cloudssd";
    /** Coming back from a picker or share sheet the app itself opened, within this time, does not lock again. */
    private static final long OWN_SCREEN_GRACE_MS = 120_000;

    private final Activity activity;
    private final FrameLayout root;
    private final int background;
    private View cover;
    private boolean authenticating;
    private boolean coldStart = true;
    private boolean stopped;
    private long leftAt;
    private boolean ownScreen;

    AppLock(Activity activity, FrameLayout root, int background) {
        this.activity = activity;
        this.root = root;
        this.background = background;
    }

    private static SharedPreferences prefs(Context c) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    static boolean enabled(Context c) {
        return prefs(c).getBoolean("app_lock", false) && available(c);
    }

    /** 0 = right away, else seconds away before it locks. */
    static int afterSeconds(Context c) {
        return prefs(c).getInt("app_lock_after", 0);
    }

    /** The phone has a screen lock (PIN, pattern, password): without one there is nothing to ask for. */
    static boolean available(Context c) {
        KeyguardManager km = (KeyguardManager) c.getSystemService(Context.KEYGUARD_SERVICE);
        return km != null && km.isDeviceSecure();
    }

    static void set(Context c, boolean on, int afterSeconds) {
        prefs(c).edit().putBoolean("app_lock", on).putInt("app_lock_after", Math.max(0, Math.min(3600, afterSeconds))).apply();
    }

    boolean locked() {
        return cover != null;
    }

    /** The app opened a picker, share sheet or another app on purpose: a quick return does not lock. */
    void ownScreen() {
        if (!authenticating) ownScreen = true;
    }

    void onStop() {
        stopped = true;
        leftAt = SystemClock.elapsedRealtime();
        // "Right away": covered before Android takes the recent-apps picture.
        if (enabled(activity) && afterSeconds(activity) == 0) showCover();
    }

    void onResume() {
        if (!enabled(activity)) {
            hideCover();
            coldStart = false;
            return;
        }
        if (authenticating) return;
        long away = SystemClock.elapsedRealtime() - leftAt;
        long limit = afterSeconds(activity) * 1000L;
        if (ownScreen) limit = Math.max(limit, OWN_SCREEN_GRACE_MS);
        ownScreen = false;
        boolean due = cover != null || coldStart || (stopped && away >= limit);
        stopped = false;
        if (!due) return;
        showCover();
        ask();
    }

    /** Switched off in Settings (from the page, which is only reachable unlocked). */
    void disabled() {
        hideCover();
    }

    void onActivityResult(int resultCode) {
        authenticating = false;
        if (resultCode == Activity.RESULT_OK) unlocked();
    }

    private void unlocked() {
        authenticating = false;
        coldStart = false;
        stopped = false;
        hideCover();
    }

    private void ask() {
        if (authenticating) return;
        authenticating = true;
        String title = Tr.t(activity, "Unlock Cloud-SSD");
        String sub = Tr.t(activity, "Use your fingerprint or screen lock");
        try {
            if (Build.VERSION.SDK_INT >= 29) {
                BiometricPrompt.Builder b = new BiometricPrompt.Builder(activity).setTitle(title).setSubtitle(sub);
                if (Build.VERSION.SDK_INT >= 30) {
                    b.setAllowedAuthenticators(BiometricManager.Authenticators.BIOMETRIC_WEAK | BiometricManager.Authenticators.DEVICE_CREDENTIAL);
                } else {
                    b.setDeviceCredentialAllowed(true);
                }
                b.build().authenticate(new CancellationSignal(), activity.getMainExecutor(), new BiometricPrompt.AuthenticationCallback() {
                    @Override
                    public void onAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result) {
                        unlocked();
                    }

                    @Override
                    public void onAuthenticationError(int errorCode, CharSequence errString) {
                        // Cancelled or too many tries: the cover stays, with its Unlock button.
                        authenticating = false;
                    }
                });
                return;
            }
            KeyguardManager km = (KeyguardManager) activity.getSystemService(Context.KEYGUARD_SERVICE);
            Intent i = km == null ? null : km.createConfirmDeviceCredentialIntent(title, sub);
            if (i == null) {
                // No screen lock any more: nothing to check against.
                unlocked();
                return;
            }
            activity.startActivityForResult(i, REQ_UNLOCK);
        } catch (Exception e) {
            authenticating = false;
        }
    }

    private void showCover() {
        if (cover != null) return;
        FrameLayout c = new FrameLayout(activity);
        c.setBackgroundColor(background);
        c.setClickable(true);   // nothing under it can be touched
        c.setFocusable(true);
        boolean dark = (background & 0xFFFFFF) < 0x808080;
        int text = dark ? 0xFFE3E3E8 : 0xFF1B1B1F;
        LinearLayout box = new LinearLayout(activity);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setGravity(Gravity.CENTER_HORIZONTAL);
        TextView icon = new TextView(activity);
        icon.setText("🔒");
        icon.setTextSize(TypedValue.COMPLEX_UNIT_SP, 48);
        icon.setGravity(Gravity.CENTER);
        TextView title = new TextView(activity);
        title.setText(Tr.t(activity, "Cloud-SSD is locked"));
        title.setTextColor(text);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20);
        title.setGravity(Gravity.CENTER);
        title.setPadding(0, dp(12), 0, dp(20));
        Button unlock = new Button(activity);
        unlock.setText(Tr.t(activity, "Unlock"));
        unlock.setAllCaps(false);
        unlock.setOnClickListener(v -> ask());
        box.addView(icon);
        box.addView(title);
        box.addView(unlock, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        c.addView(box, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.CENTER));
        root.addView(c, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        cover = c;
    }

    private void hideCover() {
        if (cover == null) return;
        root.removeView(cover);
        cover = null;
    }

    private int dp(int v) {
        return (int) (v * activity.getResources().getDisplayMetrics().density);
    }
}
