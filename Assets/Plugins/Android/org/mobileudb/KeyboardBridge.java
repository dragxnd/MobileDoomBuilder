package org.mobileudb;

import android.app.Activity;
import android.graphics.Rect;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.view.View;
import android.view.WindowInsets;
import java.lang.ref.WeakReference;
import java.util.concurrent.atomic.AtomicInteger;

// UI-thread samples; no replacement of Unity's inset or animation listeners.
public final class KeyboardBridge {
    private static final Handler UI = new Handler(Looper.getMainLooper());
    private static final AtomicInteger generation = new AtomicInteger();
    private static volatile float bottom;
    private KeyboardBridge() { }

    public static int start(Activity activity) {
        final int token = generation.incrementAndGet();
        bottom = 0;
        final WeakReference<Activity> owner = new WeakReference<>(activity);
        UI.post(new Runnable() {
            final Rect visible = new Rect();
            final int[] decorLocation = new int[2], contentLocation = new int[2];
            @Override public void run() {
                if (generation.get() != token) return;
                Activity current = owner.get();
                if (current == null || current.isFinishing() || current.isDestroyed()) { bottom = 0; return; }
                View decor = current.getWindow().getDecorView();
                View content = current.findViewById(android.R.id.content);
                float fraction = 0;
                if (content != null && content.getHeight() > 0) {
                    decor.getLocationOnScreen(decorLocation);
                    content.getLocationOnScreen(contentLocation);
                    int keyboardTop = decorLocation[1] + decor.getHeight();
                    boolean shown;
                    if (Build.VERSION.SDK_INT >= 30) {
                        WindowInsets insets = decor.getRootWindowInsets();
                        shown = insets != null && insets.isVisible(WindowInsets.Type.ime());
                        // Decor may already be resized by adjustResize. Window
                        // metrics retain the window bounds including inset areas.
                        if (shown) keyboardTop = current.getWindowManager().getCurrentWindowMetrics().getBounds().bottom - insets.getInsets(WindowInsets.Type.ime()).bottom;
                    } else {
                        decor.getWindowVisibleDisplayFrame(visible);
                        int missing = keyboardTop - visible.bottom;
                        shown = missing > Math.max(80 * decor.getResources().getDisplayMetrics().density, decor.getHeight() * .15f);
                        if (shown) keyboardTop = visible.bottom;
                    }
                    if (shown) fraction = overlap(contentLocation[1], content.getHeight(), keyboardTop);
                }
                if (generation.get() == token) bottom = fraction;
                UI.postDelayed(this, 100);
            }
        });
        return token;
    }
    // Already-resized content, hidden or floating IME all yield zero overlap.
    static float overlap(int contentTop, int contentHeight, int keyboardTop) {
        if (contentHeight <= 0) return 0;
        return Math.max(0, Math.min(1, (contentTop + (float)contentHeight - keyboardTop) / contentHeight));
    }
    public static float bottomFraction(int token) { return generation.get() == token ? bottom : 0; }
    public static void stop(int token) { if (generation.compareAndSet(token, token + 1)) bottom = 0; }
}
