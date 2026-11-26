package com.cloudssd.mobile;

import android.app.Activity;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * "Your PC is ringing this phone" over the lock screen, with one big "Found it" button: someone looking for the phone
 * does not need to unlock it to stop the ring. It turns the screen on, and closes by itself when the ring stops any
 * other way (the 20 seconds, a volume key, the note).
 */
public class RingActivity extends Activity {
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable watch = new Runnable() {
        @Override
        public void run() {
            if (!PhoneRinger.isRinging()) {
                finish();
                return;
            }
            handler.postDelayed(this, 500);
        }
    };

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        if (Build.VERSION.SDK_INT >= 27) {
            setShowWhenLocked(true);
            setTurnScreenOn(true);
        } else {
            getWindow().addFlags(WindowManager.LayoutParams.FLAG_SHOW_WHEN_LOCKED | WindowManager.LayoutParams.FLAG_TURN_SCREEN_ON);
        }
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        String from = getIntent().getStringExtra("from");
        if (from == null || from.isEmpty()) from = Tr.t(this, "Your PC");
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setGravity(Gravity.CENTER);
        box.setBackgroundColor(0xFF121316);
        int pad = dp(28);
        box.setPadding(pad, pad, pad, pad);

        TextView bell = new TextView(this);
        bell.setText("🔔");
        bell.setTextSize(TypedValue.COMPLEX_UNIT_SP, 72);
        bell.setGravity(Gravity.CENTER);
        TextView title = new TextView(this);
        title.setText(Tr.t(this, "{0} is ringing this phone", from));
        title.setTextColor(0xFFE3E3E8);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 24);
        title.setGravity(Gravity.CENTER);
        title.setPadding(0, dp(16), 0, dp(8));
        TextView sub = new TextView(this);
        sub.setText(Tr.t(this, "Found it? Tap the button to stop."));
        sub.setTextColor(0xFFB0B0B8);
        sub.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
        sub.setGravity(Gravity.CENTER);
        sub.setPadding(0, 0, 0, dp(32));
        Button stop = new Button(this);
        stop.setText(Tr.t(this, "Found it — stop"));
        stop.setAllCaps(false);
        stop.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20);
        stop.setPadding(dp(32), dp(18), dp(32), dp(18));
        stop.setOnClickListener(v -> found());

        box.addView(bell);
        box.addView(title);
        box.addView(sub);
        box.addView(stop, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        setContentView(box);
    }

    @Override
    protected void onResume() {
        super.onResume();
        handler.post(watch);
    }

    @Override
    protected void onPause() {
        handler.removeCallbacks(watch);
        super.onPause();
    }

    /** Back also means "found it". */
    @Override
    public void onBackPressed() {
        found();
    }

    private void found() {
        PhoneRinger.stop(this);
        finish();
    }

    private int dp(int v) {
        return (int) (v * getResources().getDisplayMetrics().density);
    }
}
