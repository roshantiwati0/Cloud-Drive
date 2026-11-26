package com.cloudssd.mobile;

import android.graphics.Canvas;
import android.graphics.ColorFilter;
import android.graphics.Paint;
import android.graphics.PixelFormat;
import android.graphics.Rect;
import android.graphics.drawable.Drawable;

/**
 * The window's background behind the system bars: one colour under the status bar (the page's top band, blue in the
 * A+C look) and another for the rest, which shows under the navigation bar (the page's bottom bar). The page sets
 * both through the bridge's setBars when its look changes.
 */
final class BarsDrawable extends Drawable {
    private final Paint top = new Paint();
    private final Paint rest = new Paint();
    private int topHeight;

    BarsDrawable(int topColor, int restColor) {
        top.setColor(topColor);
        rest.setColor(restColor);
    }

    void setColors(int topColor, int restColor) {
        top.setColor(topColor);
        rest.setColor(restColor);
        invalidateSelf();
    }

    /** The height of the status bar (and display cutout) at the top. */
    void setTopHeight(int px) {
        if (px == topHeight) return;
        topHeight = px;
        invalidateSelf();
    }

    @Override
    public void draw(Canvas canvas) {
        Rect b = getBounds();
        canvas.drawRect(b.left, b.top + topHeight, b.right, b.bottom, rest);
        canvas.drawRect(b.left, b.top, b.right, b.top + topHeight, top);
    }

    @Override
    public void setAlpha(int alpha) {
        top.setAlpha(alpha);
        rest.setAlpha(alpha);
    }

    @Override
    public void setColorFilter(ColorFilter cf) {
        top.setColorFilter(cf);
        rest.setColorFilter(cf);
    }

    @Override
    @SuppressWarnings("deprecation") // still abstract in Drawable, so it must be written
    public int getOpacity() {
        return PixelFormat.OPAQUE;
    }
}
