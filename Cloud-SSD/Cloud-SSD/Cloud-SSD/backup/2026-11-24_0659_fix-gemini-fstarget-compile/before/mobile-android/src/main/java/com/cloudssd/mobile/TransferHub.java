package com.cloudssd.mobile;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.ContentValues;
import android.content.Context;
import android.content.Intent;
import android.content.res.AssetFileDescriptor;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.os.ParcelFileDescriptor;
import android.os.SystemClock;
import android.provider.MediaStore;
import android.webkit.MimeTypeMap;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Every transfer between this phone and the PC (or Google Drive): a queue that keeps going when the app is
 * closed (it runs in {@link TransferService}), two files at a time, retries by itself after a broken connection, and
 * continues from where it stopped instead of starting over. What is queued is kept in a small database, so a
 * transfer that was cut off can still be retried after the app was closed.
 */
final class TransferHub {
    interface Listener {
        void onTransfer(JSONObject t);

        /** A task changed (see {@link #taskJson}). */
        default void onTask(JSONObject task) { }
    }

    static final String CHANNEL = "cloudssd_transfers";
    static final int SUMMARY_ID = 4999;
    private static final long[] RETRY_DELAYS_MS = {5_000, 20_000, 60_000};

    private static TransferHub instance;

    static synchronized TransferHub get(Context c) {
        if (instance == null) instance = new TransferHub(c.getApplicationContext());
        return instance;
    }

    private final Context ctx;
    private final Store store;
    private final ExecutorService pool;
    private final ScheduledExecutorService timer = Executors.newSingleThreadScheduledExecutor();
    private final List<Listener> listeners = new CopyOnWriteArrayList<>();
    private final Map<Long, AtomicBoolean> cancel = new ConcurrentHashMap<>();
    private final Map<Long, double[]> speed = new ConcurrentHashMap<>();   // {bytesPerSec, lastBytes, lastAt}
    private final Map<Long, Long> retryAt = new ConcurrentHashMap<>();
    /** Uploads the PC is asking its person about: since when (so they give up after a while). */
    private final Map<Long, Long> askedSince = new ConcurrentHashMap<>();

    private TransferHub(Context c) {
        ctx = c;
        store = new Store(c);
        int parallel = Math.max(1, Math.min(3, c.getSharedPreferences("cloudssd", Context.MODE_PRIVATE).getInt("parallel", 2)));
        pool = Executors.newFixedThreadPool(parallel);
        createChannel();
        // Transfers that were running when the app was killed: nothing is running them now. They can be retried
        // (and continue where they stopped).
        store.interrupted();
        watchNetwork();
    }

    // ---- tasks: one offer, one folder or one batch of files, as people think of a transfer --------------------------

    /** Files queued on this thread between beginTask and endTask belong to that task; others get a task of their own. */
    private final ThreadLocal<String> taskScope = new ThreadLocal<>();
    /** How the phone reaches the PC now (wifi, usb, hotspot, tailscale), told by the page. */
    volatile String linkKind = "wifi";
    /** The PC in use, by name (told by the page), for tasks made here. */
    volatile String pcName = "PC";
    private final Map<String, Long> taskRaised = new ConcurrentHashMap<>();
    /** How often a transfer that waits for the PC ("held") has looked again by itself: each time it waits longer. */
    private final Map<Long, Integer> heldTries = new ConcurrentHashMap<>();
    private final Map<String, String> taskLastState = new ConcurrentHashMap<>();

    /**
     * Starts a task: meta has dir (toPc, toPhone, toDevice, fromDevice), device, link, kind (files, folder), folder,
     * saveTo and openPath; id may be given (an offer's) or is made. Files queued on this thread until endTask join it.
     */
    String beginTask(JSONObject meta) {
        String id = meta.optString("id", "");
        if (id.isEmpty()) id = "ph-" + Uploader.randomId().substring(0, 12);
        store.putTask(id, meta);
        taskScope.set(id);
        return id;
    }

    void endTask() {
        taskScope.remove();
    }

    /** Puts the row into the current task, or into a task of its own (a single file, a backup…). */
    private void tag(ContentValues v) {
        String t = taskScope.get();
        if (t == null) {
            t = "ph-" + Uploader.randomId().substring(0, 12);
            boolean down = "download".equals(v.getAsString("type"));
            String target = v.getAsString("target") == null ? "" : v.getAsString("target");
            boolean relay = target.startsWith("share:");
            JSONObject meta = new JSONObject();
            try {
                meta.put("dir", down ? "toPhone" : relay ? "toDevice" : "toPc");
                meta.put("device", pcName);
                meta.put("link", relay ? "relay" : linkKind);
                meta.put("kind", "files");
                meta.put("saveTo", down ? "Downloads" + (target.isEmpty() ? "" : " › " + target.replace("/", " › ")) : relay ? pcName : pcName + (target.isEmpty() ? " › Uploads" : " › " + target));
                meta.put("openPath", down ? "Download" + (target.isEmpty() ? "" : "/" + target) : "");
            } catch (Exception ignored) { }
            store.putTask(t, meta);
        }
        v.put("task", t);
    }

    /** The task in the shared model (the PC's TransferTask has the same fields). */
    JSONObject taskJson(String id) {
        JSONObject meta = store.taskMeta(id);
        if (meta == null) return null;
        List<JSONObject> rows = store.rowsOf(id);
        int files = rows.size(), done = 0, failed = 0, cancelled = 0, active = 0, held = 0, waiting = 0;
        long bytes = 0, doneBytes = 0, speedSum = 0, ended = 0;
        String current = "";
        JSONArray fails = new JSONArray();
        for (JSONObject r : rows) {
            String st = r.optString("state");
            long size = Math.max(0, r.optLong("size", 0));
            bytes += size;
            switch (st) {
                case "done": done++; doneBytes += size; break;
                case "failed": failed++; fails.put(failure(r)); break;
                case "cancelled": cancelled++; fails.put(failure(r)); break;
                case "active":
                    active++;
                    doneBytes += r.optLong("bytes");
                    current = r.optString("name");
                    double[] s = speed.get(r.optLong("id"));
                    if (s != null) speedSum += Math.round(s[0]);
                    break;
                case "held": held++; doneBytes += r.optLong("bytes"); break;
                default: waiting++; doneBytes += r.optLong("bytes"); break;
            }
            ended = Math.max(ended, r.optLong("finished", 0));
        }
        boolean open = active + held + waiting > 0;
        // Work done outside the hub: open until it is finished; then what never came counts as failed.
        int expected = meta.optInt("files", 0);
        boolean outside = meta.has("open");
        // Whoever ran it went away without saying so (the PC crashed, the app was killed): no file for five minutes
        // ends it, so it does not stay "running" for ever.
        if (outside && meta.optBoolean("open", false)
                && System.currentTimeMillis() - Math.max(ended, Math.max(meta.optLong("touched", 0), meta.optLong("started", 0))) > EXTERNAL_IDLE_MS) {
            try { meta.put("open", false).put("endReason", "network"); } catch (Exception ignored) { }
            store.putTask(id, meta);
        }
        if (outside && meta.optBoolean("open", false)) open = true;
        else if (outside && expected > done + failed + cancelled) {
            // What never came: stopped on purpose ("cancelled") or lost on the way ("network").
            String why = "cancelled".equals(meta.optString("endReason")) ? "cancelled" : "network";
            int missing = expected - (done + failed + cancelled);
            for (int i = 0; i < missing; i++) {
                try { fails.put(new JSONObject().put("name", "").put("reason", why)); } catch (Exception ignored) { }
            }
            if ("cancelled".equals(why)) cancelled += missing; else failed += missing;
        }
        String state;
        if (outside && open) state = done + failed > 0 ? "running" : "waiting";
        else if (active > 0) state = "running";
        else if (held > 0) state = "paused";
        else if (waiting > 0) state = done + failed + cancelled > 0 ? "running" : "waiting";
        else if (files == 0) state = "waiting";
        else if (failed + cancelled == 0) state = "done";
        else if (done == 0) state = failed == 0 ? "cancelled" : "failed";
        else state = "partial";
        JSONObject o = new JSONObject();
        try {
            o.put("id", id);
            o.put("dir", meta.optString("dir", "toPc"));
            o.put("kind", meta.optString("kind", "files"));
            o.put("folder", meta.optString("folder", ""));
            o.put("device", meta.optString("device", pcName));
            o.put("link", meta.optString("link", linkKind));
            o.put("state", state);
            o.put("name", rows.isEmpty() ? meta.optString("name", "") : rows.get(0).optString("name"));
            o.put("files", Math.max(files, meta.optInt("files", 0)));
            o.put("doneFiles", done);
            o.put("bytes", Math.max(bytes, meta.optLong("bytes", 0)));
            o.put("doneBytes", doneBytes);
            o.put("speed", open ? speedSum : 0);
            o.put("current", open ? current : "");
            o.put("failed", fails);
            o.put("saveTo", meta.optString("saveTo", ""));
            o.put("openPath", meta.optString("openPath", ""));
            o.put("started", iso(meta.optLong("started", System.currentTimeMillis())));
            o.put("ended", open || ended == 0 ? JSONObject.NULL : iso(ended));
            o.put("canRetry", !outside && !open && failed + cancelled > 0);   // outside work is retried where it runs
            o.put("canCancel", open && !outside);
        } catch (Exception ignored) { }
        return o;
    }

    private static String iso(long ms) {
        java.text.SimpleDateFormat f = new java.text.SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ssXXX", java.util.Locale.US);
        return f.format(new java.util.Date(ms));
    }

    /** Why a file did not arrive, in the model's words, from the error the page showed. */
    static String reasonOf(String state, String error) {
        if ("cancelled".equals(state)) return "cancelled";
        String e = error == null ? "" : error.toLowerCase(java.util.Locale.US);
        if (e.contains("space") || e.contains("full")) return "space";
        if (e.contains("not reachable") || e.contains("connection") || e.contains("network") || e.contains("timed out") || e.contains("stopped when")) return "network";
        if (e.contains("no longer") || e.contains("not found") || e.contains("missing") || e.contains("gone")) return "missing";
        if (e.contains("declined") || e.contains("nobody answered") || e.contains("denied") || e.contains("permission") || e.contains("not allowed") || e.contains("403")) return "denied";
        if (e.contains("cancel")) return "cancelled";
        return "other";
    }

    private static JSONObject failure(JSONObject r) {
        JSONObject f = new JSONObject();
        try {
            f.put("name", r.optString("name"));
            f.put("reason", reasonOf(r.optString("state"), r.optString("error")));
        } catch (Exception ignored) { }
        return f;
    }

    // ---- work done outside the hub (PeerLink's fs.pull…): the same tasks and events ---------------------------------

    /**
     * A task for work this hub does not run itself. meta as for beginTask (dir, kind, folder, device, link, files,
     * bytes, saveTo, openPath; id optional, e.g. the PC's task id so several calls join one task). Returns its id ("pl-…").
     */
    String external(JSONObject meta) {
        String id = meta.optString("id", "");
        if (id.isEmpty()) id = "pl-" + Uploader.randomId().substring(0, 12);
        JSONObject old = store.taskMeta(id);
        try {
            if (old != null) { for (java.util.Iterator<String> k = old.keys(); k.hasNext(); ) { String key = k.next(); if (!meta.has(key)) meta.put(key, old.get(key)); } }
            meta.put("open", true);
            meta.put("touched", System.currentTimeMillis());
            meta.remove("endReason");
        } catch (Exception ignored) { }
        store.putTask(id, meta);
        raiseTask(id);
        return id;
    }

    /** An outside task with no file for this long is taken as ended (see taskJson). */
    private static final long EXTERNAL_IDLE_MS = 5 * 60_000L;

    /** One file of an outside task arrived (ok) or did not (reason: network, space, missing, cancelled, denied, other). */
    void externalFile(String id, String name, long size, boolean ok, String reason) {
        ContentValues v = new ContentValues();
        v.put("type", "external");
        v.put("name", name);
        v.put("size", size);
        v.put("bytes", ok ? size : 0);
        v.put("state", ok ? "done" : "failed");
        v.put("error", ok ? "" : reason == null ? "other" : reason);
        v.put("finished", System.currentTimeMillis());
        v.put("task", id);
        store.insert(v);
        raiseTask(id);
    }

    /** An outside task ends: what did not arrive by now counts as failed (lost on the way). */
    void externalFinish(String id) {
        externalFinish(id, "network");
    }

    /** The same, saying why the rest did not come: "cancelled" (stopped on purpose) or "network". */
    void externalFinish(String id, String reason) {
        JSONObject meta = store.taskMeta(id);
        if (meta == null || !meta.has("open")) return;
        try {
            meta.put("open", false);
            meta.put("endReason", "cancelled".equals(reason) ? "cancelled" : "network");
        } catch (Exception ignored) { }
        store.putTask(id, meta);
        raiseTask(id);
    }

    private void raiseTask(String id) {
        JSONObject t = taskJson(id);
        if (t == null) return;
        taskLastState.put(id, t.optString("state") + "/" + t.optInt("doneFiles"));
        taskRaised.put(id, SystemClock.elapsedRealtime());
        for (Listener l : listeners) l.onTask(t);
    }

    /** The newest tasks, for a page that just opened. */
    JSONArray tasks() {
        JSONArray out = new JSONArray();
        for (String id : store.recentTasks(40)) {
            JSONObject t = taskJson(id);
            if (t != null && t.optInt("files") > 0) out.put(t);
        }
        return out;
    }

    void taskCancel(String id) {
        // Work run from elsewhere (the PC adding files): this phone can only stop listing it as running.
        JSONObject meta = store.taskMeta(id);
        if (meta != null && meta.optBoolean("open", false)) {
            externalFinish(id, "cancelled");
            return;
        }
        for (JSONObject r : store.rowsOf(id)) {
            String st = r.optString("state");
            if (!"done".equals(st) && !"failed".equals(st) && !"cancelled".equals(st)) cancel(r.optLong("id"));
        }
    }

    void taskRetry(String id) {
        for (JSONObject r : store.rowsOf(id)) {
            String st = r.optString("state");
            if (("failed".equals(st) || "cancelled".equals(st)) && !"external".equals(r.optString("type"))) retry(r.optLong("id"));
        }
    }

    /** Tells the page about the row's task: at most 4 times a second, always when its state changes. */
    private void publishTask(JSONObject row) {
        String task = row.optString("task", "");
        if (task.isEmpty() || listeners.isEmpty()) return;
        long now = SystemClock.elapsedRealtime();
        JSONObject t = taskJson(task);
        if (t == null) return;
        String state = t.optString("state") + "/" + t.optInt("doneFiles") + "/" + t.optJSONArray("failed").length();
        boolean changed = !state.equals(taskLastState.put(task, state));
        Long last = taskRaised.get(task);
        if (!changed && last != null && now - last < 250) return;
        taskRaised.put(task, now);
        for (Listener l : listeners) l.onTask(t);
    }

    void addListener(Listener l) {
        listeners.add(l);
    }

    void removeListener(Listener l) {
        listeners.remove(l);
    }

    // ---- starting -------------------------------------------------------------------------------------------------

    /**
     * @param srcKind "file" (a path), "uri" (content the app may keep reading) or "uri-temp" (shared by another app:
     *                readable only while this process lives)
     * @param target  "" = the PC's upload folder, else a cloud folder id ("cloudssd:Photos")
     */
    long enqueueUpload(String name, long size, String srcKind, String src, String target) {
        ContentValues v = new ContentValues();
        v.put("type", "upload");
        v.put("name", name);
        v.put("srcKind", srcKind);
        v.put("src", src);
        v.put("target", target == null ? "" : target);
        v.put("size", size);
        v.put("uploadId", Uploader.randomId());
        v.put("state", "queued");
        v.put("pcId", PcProfiles.activeId(ctx));
        tag(v);
        long id = store.insert(v);
        submit(id);
        return id;
    }

    /**
     * Moves a phone file to the cloud: uploaded like any other, and the file on the phone is deleted only after the
     * upload was confirmed (Space &amp; RAM, "Move to the cloud").
     */
    long enqueueMove(String name, long size, String path, String target) {
        ContentValues v = new ContentValues();
        v.put("type", "upload");
        v.put("name", name);
        v.put("srcKind", "file");
        v.put("src", path);
        v.put("target", target);
        v.put("size", size);
        v.put("uploadId", Uploader.randomId());
        v.put("state", "queued");
        v.put("deleteSrc", 1);
        v.put("pcId", PcProfiles.activeId(ctx));
        tag(v);
        long id = store.insert(v);
        submit(id);
        return id;
    }

    /** @param route a PC route with its parameters and no key ("/api/download?path=…"), or "cloud:" + an item id */
    long enqueueDownload(String route, String name, long size) {
        return enqueueDownload(route, name, size, "");
    }

    /** folder: a folder inside Downloads ("Cloud-SSD/Received/DESKTOP"), or "" for Downloads itself. */
    long enqueueDownload(String route, String name, long size, String folder) {
        ContentValues v = new ContentValues();
        v.put("type", "download");
        v.put("target", cleanFolder(folder));
        v.put("name", name);
        v.put("srcKind", route.startsWith("cloud:") ? "cloud" : "pc");
        v.put("src", route.startsWith("cloud:") ? route.substring(6) : route);
        v.put("size", size);
        v.put("state", "queued");
        v.put("pcId", route.startsWith("cloud:") ? "" : PcProfiles.activeId(ctx));
        tag(v);
        long id = store.insert(v);
        submit(id);
        return id;
    }

    void retry(long id) {
        ContentValues v = new ContentValues();
        v.put("state", "queued");
        v.put("error", "");
        v.put("attempts", 0);
        store.update(id, v);
        submit(id);
    }

    /**
     * The PC is back (or another PC was chosen): transfers that were waiting for the PC now in use start again by
     * themselves. Those for another saved PC keep waiting for it.
     */
    void resumeHeld() {
        String active = PcProfiles.activeId(ctx);
        for (long id : store.ids("state = 'held'")) {
            JSONObject row = store.row(id);
            if (row == null) continue;
            String pc = row.optString("pcId", "");
            if (!pc.isEmpty() && !pc.equals(active)) continue;
            retry(id);
        }
    }

    /** Watches the network: when a network comes up, transfers waiting for the PC are tried again a moment later. */
    private void watchNetwork() {
        if (Build.VERSION.SDK_INT < 24) return;
        try {
            android.net.ConnectivityManager cm = (android.net.ConnectivityManager) ctx.getSystemService(Context.CONNECTIVITY_SERVICE);
            if (cm == null) return;
            cm.registerDefaultNetworkCallback(new android.net.ConnectivityManager.NetworkCallback() {
                @Override
                public void onAvailable(android.net.Network network) {
                    if (store.count("state = 'held'") > 0) timer.schedule(TransferHub.this::resumeHeld, 3, TimeUnit.SECONDS);
                }
            });
        } catch (Exception ignored) {
        }
    }

    void cancel(long id) {
        AtomicBoolean f = cancel.get(id);
        if (f != null) f.set(true);
        JSONObject row = store.row(id);
        if (row != null && ("queued".equals(row.optString("state")) || "waiting".equals(row.optString("state")) || "held".equals(row.optString("state")))) {
            retryAt.remove(id);
            setState(id, "cancelled", "Cancelled");
            dropPartial(row);
        }
    }

    /** Removes a failed or finished transfer from the list (and the half file it left, if any). */
    void dismiss(long id) {
        JSONObject row = store.row(id);
        if (row == null) return;
        String state = row.optString("state");
        if ("active".equals(state) || "queued".equals(state)) {
            cancel(id);
            return;
        }
        dropPartial(row);
        releaseUri(row);
        store.delete(id);
    }

    /** Android ended the background time for today: stop cleanly; each can be retried later. */
    void pauseAll(String why) {
        for (Map.Entry<Long, AtomicBoolean> e : cancel.entrySet()) {
            e.getValue().set(true);
            ContentValues v = new ContentValues();
            v.put("state", "failed");
            v.put("error", why);
            store.update(e.getKey(), v);
        }
    }

    private void submit(final long id) {
        cancel.put(id, new AtomicBoolean(false));
        publish(id);
        TransferService.start(ctx);
        pool.execute(() -> run(id));
    }

    boolean busy() {
        return store.count("state IN ('queued','active','waiting')") > 0;
    }

    JSONArray snapshot() {
        return store.visible();
    }

    // ---- running --------------------------------------------------------------------------------------------------

    private void run(long id) {
        JSONObject row = store.row(id);
        AtomicBoolean stop = cancel.get(id);
        if (row == null || stop == null || stop.get() || !"queued".equals(row.optString("state"))) {
            TransferService.stopIfIdle(ctx);
            return;
        }
        // Meant for a saved PC that is not the one in use now: it waits for that PC instead of going to another one.
        String forPc = row.optString("pcId", "");
        if (!forPc.isEmpty() && !"cloud".equals(row.optString("srcKind")) && !forPc.equals(PcProfiles.activeId(ctx))) {
            setState(id, "held", "Waiting for " + pcName(forPc) + " — sends when it is the PC in use");
            cancel.remove(id);
            TransferService.stopIfIdle(ctx);
            return;
        }
        setState(id, "active", "");
        speed.put(id, new double[]{0, row.optLong("bytes", 0), SystemClock.elapsedRealtime()});
        try {
            if ("upload".equals(row.optString("type"))) upload(id, row, stop);
            else download(id, row, stop);
            ContentValues v = new ContentValues();
            v.put("state", "done");
            v.put("error", "");
            v.put("finished", System.currentTimeMillis());
            if (row.optInt("deleteSrc") == 1 && "file".equals(row.optString("srcKind"))) {
                File src = new File(row.optString("src"));
                if (src.delete()) {
                    v.put("note", "Moved to the cloud");
                    android.media.MediaScannerConnection.scanFile(ctx, new String[]{src.getAbsolutePath()}, null, null);
                }
            }
            store.update(id, v);
            heldTries.remove(id);
            releaseUri(store.row(id));
            notifyDone(id);
        } catch (Exception e) {
            boolean cancelled = stop.get();
            JSONObject now = store.row(id);
            if (cancelled && now != null && "failed".equals(now.optString("state"))) {
                // paused by pauseAll: keep its message
            } else if (cancelled) {
                setState(id, "cancelled", "Cancelled");
                dropPartial(now);
            } else {
                failOrRetry(id, e);
            }
        } finally {
            cancel.remove(id);
            speed.remove(id);
            publish(id);
            TransferService.stopIfIdle(ctx);
        }
    }

    /** A dropped connection is tried again by itself (after 5 s, 20 s, 60 s); a refusal by the PC is not. */
    private void failOrRetry(final long id, Exception e) {
        JSONObject row = store.row(id);
        int attempts = row == null ? 0 : row.optInt("attempts", 0);
        boolean refused = e instanceof Uploader.Refused && ((Uploader.Refused) e).status > 0
                || e instanceof CloudBackend.BackendException && ((CloudBackend.BackendException) e).status > 0;
        String msg = friendly(e);
        // The PC is asking its person first (423: a guest's files, or "always ask"): wait and look again every 5 s, for
        // up to 15 minutes; an OK there lets the next try through, a No comes back as a refusal.
        if (e instanceof Uploader.Refused && ((Uploader.Refused) e).status == 423 && row != null) {
            Long since = askedSince.get(id);
            if (since == null) askedSince.put(id, since = System.currentTimeMillis());
            if (System.currentTimeMillis() - since > 15 * 60_000L) {
                askedSince.remove(id);
                setState(id, "failed", "Nobody answered on the PC");
                notifyFailed(id, "Nobody answered on the PC");
                return;
            }
            ContentValues v = new ContentValues();
            v.put("state", "waiting");
            v.put("error", "Waiting for an OK on the PC");
            store.update(id, v);
            publish(id);
            retryAt.put(id, System.currentTimeMillis() + 5_000);
            timer.schedule(() -> {
                retryAt.remove(id);
                JSONObject r = store.row(id);
                if (r == null || !"waiting".equals(r.optString("state"))) return;
                ContentValues q = new ContentValues();
                q.put("state", "queued");
                store.update(id, q);
                submit(id);
            }, 5_000, TimeUnit.MILLISECONDS);
            return;
        }
        askedSince.remove(id);
        // The PC is away (not refusing): after one quick retry the transfer waits for it and goes on by itself when it
        // is reachable again, instead of failing. A file shared by another app cannot wait (it may not be readable later).
        boolean pcAway = !refused && unreachable(e) && row != null && !"uri-temp".equals(row.optString("srcKind"))
                && !"cloud".equals(row.optString("srcKind"));
        if (pcAway && attempts >= 1) {
            String pc = row.optString("pcId", "");
            setState(id, "held", "Waiting for " + pcName(pc) + " — sends when it is reachable");
            // The page says when the PC is back, and a new network does too. A PC that was only slow for a moment (it
            // was starting) gives neither sign: the transfer looks again by itself, after 30 s, 1 min, 2 min… up to 15.
            int tries = heldTries.merge(id, 1, Integer::sum);
            long delay = Math.min(15 * 60_000L, 30_000L << Math.min(tries - 1, 5));
            timer.schedule(() -> {
                JSONObject r = store.row(id);
                if (r == null || !"held".equals(r.optString("state"))) return;
                String forPc = r.optString("pcId", "");
                if (forPc.isEmpty() || forPc.equals(PcProfiles.activeId(ctx))) retry(id);
            }, delay, TimeUnit.MILLISECONDS);
            return;
        }
        if (!refused && attempts < RETRY_DELAYS_MS.length && row != null && !"uri-temp-gone".equals(msg)) {
            long delay = RETRY_DELAYS_MS[attempts];
            ContentValues v = new ContentValues();
            v.put("state", "waiting");
            v.put("error", msg);
            v.put("attempts", attempts + 1);
            store.update(id, v);
            retryAt.put(id, System.currentTimeMillis() + delay);
            timer.schedule(() -> {
                retryAt.remove(id);
                JSONObject r = store.row(id);
                if (r == null || !"waiting".equals(r.optString("state"))) return;
                ContentValues q = new ContentValues();
                q.put("state", "queued");
                store.update(id, q);
                submit(id);
            }, delay, TimeUnit.MILLISECONDS);
        } else {
            setState(id, "failed", msg);
            notifyFailed(id, msg);
        }
    }

    /** The PC did not answer at all (switched off, another network, Wi-Fi gone), as opposed to answering with an error. */
    static boolean unreachable(Exception e) {
        if (e instanceof CloudBackend.BackendException) return ((CloudBackend.BackendException) e).status == CloudBackend.BackendException.UNREACHABLE;
        if (e instanceof Uploader.Refused) return ((Uploader.Refused) e).status <= 0;
        return e instanceof java.net.SocketTimeoutException || e instanceof java.net.ConnectException || e instanceof java.net.NoRouteToHostException
                || e instanceof java.net.UnknownHostException || e instanceof java.net.SocketException;
    }

    /** The name the page gave a saved PC (or "the PC"). */
    private String pcName(String pcId) {
        JSONObject rec = pcId == null || pcId.isEmpty() ? null : PcProfiles.get(ctx, pcId);
        String n = rec == null ? "" : rec.optString("label", rec.optString("name", ""));
        return n.isEmpty() ? "the PC" : n;
    }

    static String friendly(Exception e) {
        String m = e.getMessage();
        if (e instanceof java.net.SocketTimeoutException || e instanceof java.net.ConnectException || e instanceof java.net.NoRouteToHostException
                || e instanceof java.net.UnknownHostException) {
            return "PC not reachable. At home: same Wi-Fi. Away: Tailscale on.";
        }
        if (e instanceof java.net.SocketException) return "The connection to the PC was lost.";
        return m == null || m.isEmpty() ? e.getClass().getSimpleName() : m;
    }

    private void progress(long id, long bytes) {
        double[] s = speed.get(id);
        long now = SystemClock.elapsedRealtime();
        if (s != null && now - s[2] >= 300) {
            s[0] = (bytes - s[1]) * 1000.0 / (now - s[2]);
            s[1] = bytes;
            s[2] = now;
        }
        ContentValues v = new ContentValues();
        v.put("bytes", bytes);
        store.updateQuiet(id, v);
        publish(id);
    }

    // ---- uploads --------------------------------------------------------------------------------------------------

    private Uploader.Opener opener(final JSONObject row) {
        final String kind = row.optString("srcKind");
        final String src = row.optString("src");
        return offset -> {
            InputStream in;
            if ("file".equals(kind)) {
                FileInputStream f = new FileInputStream(new File(src));
                if (offset > 0) f.getChannel().position(offset);
                return f;
            }
            try {
                AssetFileDescriptor fd = ctx.getContentResolver().openAssetFileDescriptor(Uri.parse(src), "r");
                if (fd == null) throw new IOException("Cannot read " + row.optString("name"));
                in = fd.createInputStream();
            } catch (SecurityException e) {
                throw new IOException("uri-temp".equals(kind) ? "Share the file again: Android no longer lets the app read it." : "Android no longer lets the app read this file.");
            }
            if (offset > 0) {
                if (in instanceof FileInputStream) ((FileInputStream) in).getChannel().position(offset);
                else for (long left = offset; left > 0; ) {
                    long n = in.skip(left);
                    if (n <= 0) throw new IOException("Cannot continue this file from the middle.");
                    left -= n;
                }
            }
            return in;
        };
    }

    private static String cleanName(String name) {
        String clean = name.replaceAll("[\\\\/:*?\"<>|\\p{Cntrl}]", "_").replaceAll("[. ]+$", "");
        return clean.isEmpty() ? "upload_" + System.currentTimeMillis() : clean;
    }

    private void upload(final long id, JSONObject row, AtomicBoolean stop) throws IOException {
        String name = row.optString("name");
        String target = row.optString("target", "");
        long size = row.optLong("size", -1);
        String route;
        if (target.startsWith("share:")) {
            // A file for another device, passed on through the PC: "share:<offer>:<item>".
            String[] p = target.split(":", 3);
            route = "/api/share/upload?id=" + Uploader.enc(p.length > 1 ? p[1] : "") + "&item=" + Uploader.enc(p.length > 2 ? p[2] : "")
                    + "&filename=" + Uploader.enc(name);
        } else if (target.startsWith("pcfolder:")) {
            // A file of a folder sent to the PC: its place there ("MyApp (2)/src"), named by the PC beforehand.
            route = "/api/upload?filename=" + Uploader.enc(name) + "&folder=" + Uploader.enc(target.substring(9));
        } else route = target.isEmpty()
                ? "/api/upload?filename=" + Uploader.enc(name)
                : "/api/fs/write?overwrite=0&mkdirs=1&path=" + Uploader.enc(target + (target.endsWith(":") ? "" : "/") + cleanName(name));
        // The PC shows one send (a folder, a few files) as one task, and asks once when it has to: tell it which send
        // this file belongs to and how big that is.
        String task = row.optString("task", "");
        if (route.startsWith("/api/upload?") && !task.isEmpty()) {
            List<JSONObject> all = store.rowsOf(task);
            long bytes = 0;
            for (JSONObject r : all) bytes += Math.max(0, r.optLong("size", 0));
            JSONObject meta = store.taskMeta(task);
            route += "&task=" + Uploader.enc(task) + "&files=" + all.size() + "&bytes=" + bytes
                    + "&kind=" + Uploader.enc(meta == null ? "files" : meta.optString("kind", "files"));
            // "Replace" was picked for names the PC already has: its file gives way to this one.
            if (meta != null && "replace".equals(meta.optString("onClash"))) route += "&onClash=replace";
        }
        try {
            Uploader.send(ctx, route, opener(row), size, row.optString("uploadId"), (done, total) -> progress(id, done), stop);
        } catch (Uploader.Refused r) {
            // PC off, but Google Drive is signed in on this phone: the file goes straight to the cloud instead.
            if (r.status != -1 || !Backends.driveReady(ctx) || target.startsWith("pcfolder:")) throw r;
            String folder = target.isEmpty() ? "cloudssd:From phone" : target;
            CloudBackend drive = Backends.drive(ctx);
            Backends.ensureFolder(drive, folder);
            try (InputStream in = opener(row).open(0)) {
                final long[] sent = {0};
                InputStream counted = new java.io.FilterInputStream(in) {
                    @Override
                    public int read(byte[] b, int off, int len) throws IOException {
                        if (stop.get()) throw new IOException("cancelled");
                        int n = super.read(b, off, len);
                        if (n > 0) progress(id, sent[0] += n);
                        return n;
                    }
                };
                drive.write(folder + (folder.endsWith(":") ? "" : "/") + cleanName(name), counted);
            }
            ContentValues v = new ContentValues();
            v.put("note", "Saved to Google Drive (" + folder.substring(folder.indexOf(':') + 1) + ") — the PC was off");
            store.updateQuiet(id, v);
        }
    }

    // ---- downloads ------------------------------------------------------------------------------------------------

    private static String mimeOf(String name) {
        int dot = name.lastIndexOf('.');
        String m = dot < 0 ? null : MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substring(dot + 1).toLowerCase(Locale.ROOT));
        return m != null ? m : "application/octet-stream";
    }

    private static String readAll(InputStream in) throws IOException {
        if (in == null) return "";
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
        return out.toString("UTF-8");
    }

    /**
     * Is something already at Downloads/folder ("Cloud-SSD/Received/PC/MyApp")? On the disk, in MediaStore (files still
     * being written count too) or among the downloads waiting in this list.
     */
    boolean folderTaken(String folder) {
        String f = cleanFolder(folder);
        if (f.isEmpty()) return false;
        try {
            if (new File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), f).exists()) return true;
        } catch (Exception ignored) { }
        String like = f.replace("'", "''");
        if (!store.ids("type='download' AND state IN ('queued','active','waiting','held') AND (target='" + like + "' OR target LIKE '" + like.replace("%", "") + "/%')").isEmpty())
            return true;
        if (Build.VERSION.SDK_INT >= 29) {
            String rel = Environment.DIRECTORY_DOWNLOADS + "/" + f + "/";
            String[] cols = {MediaStore.MediaColumns._ID};
            try {
                Cursor c;
                if (Build.VERSION.SDK_INT >= 30) {
                    android.os.Bundle q = new android.os.Bundle();
                    q.putString(android.content.ContentResolver.QUERY_ARG_SQL_SELECTION, MediaStore.MediaColumns.RELATIVE_PATH + " LIKE ?");
                    q.putStringArray(android.content.ContentResolver.QUERY_ARG_SQL_SELECTION_ARGS, new String[]{rel + "%"});
                    q.putInt(MediaStore.QUERY_ARG_MATCH_PENDING, MediaStore.MATCH_INCLUDE);
                    q.putInt(android.content.ContentResolver.QUERY_ARG_LIMIT, 1);
                    c = ctx.getContentResolver().query(MediaStore.Downloads.EXTERNAL_CONTENT_URI, cols, q, null);
                } else {
                    c = ctx.getContentResolver().query(MediaStore.Downloads.EXTERNAL_CONTENT_URI, cols,
                            MediaStore.MediaColumns.RELATIVE_PATH + " LIKE ?", new String[]{rel + "%"}, null);
                }
                if (c != null) {
                    try { if (c.moveToFirst()) return true; } finally { c.close(); }
                }
            } catch (Exception ignored) { }
        }
        return false;
    }

    /** Where the file is being written: a pending Downloads item (Android 10+) or a hidden part file (older). */
    /** "Cloud-SSD/Received/My PC" with every part cleaned; "" when nothing usable is left. */
    static String cleanFolder(String folder) {
        if (folder == null) return "";
        StringBuilder out = new StringBuilder();
        for (String part : folder.split("/")) {
            String p = part.replaceAll("[\\\\:*?\"<>|\\p{Cntrl}]", "_").replaceAll("^[. ]+|[. ]+$", "");
            if (p.isEmpty() || p.equals("..")) continue;
            if (p.length() > 60) p = p.substring(0, 60);
            if (out.length() > 0) out.append('/');
            out.append(p);
        }
        return out.toString();
    }

    private OutputStream openDest(long id, JSONObject row, long[] have) throws IOException {
        String name = row.optString("name");
        String dest = row.optString("dest", "");
        String folder = cleanFolder(row.optString("target", ""));
        if (Build.VERSION.SDK_INT >= 29) {
            Uri uri = dest.isEmpty() ? null : Uri.parse(dest);
            if (uri != null) {
                try (ParcelFileDescriptor pfd = ctx.getContentResolver().openFileDescriptor(uri, "r")) {
                    have[0] = pfd == null ? 0 : pfd.getStatSize();
                } catch (Exception e) {
                    uri = null; // the half file was removed meanwhile
                }
            }
            if (uri == null) {
                ContentValues values = new ContentValues();
                values.put(MediaStore.MediaColumns.DISPLAY_NAME, name);
                values.put(MediaStore.MediaColumns.MIME_TYPE, mimeOf(name));
                values.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + (folder.isEmpty() ? "" : "/" + folder));
                values.put(MediaStore.MediaColumns.IS_PENDING, 1);
                uri = ctx.getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values);
                if (uri == null) throw new IOException("Cannot create the file in Downloads");
                ContentValues v = new ContentValues();
                v.put("dest", uri.toString());
                store.updateQuiet(id, v);
                have[0] = 0;
            }
            return ctx.getContentResolver().openOutputStream(uri, have[0] > 0 ? "wa" : "w");
        }
        File dir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS);
        if (!folder.isEmpty()) dir = new File(dir, folder);
        if (!dir.exists()) dir.mkdirs();
        File part = dest.isEmpty() ? new File(dir, "." + name + ".cssd-part") : new File(dest);
        if (dest.isEmpty()) {
            ContentValues v = new ContentValues();
            v.put("dest", part.getAbsolutePath());
            store.updateQuiet(id, v);
        }
        have[0] = part.exists() ? part.length() : 0;
        return new FileOutputStream(part, have[0] > 0);
    }

    private void truncateDest(JSONObject row) throws IOException {
        String dest = store.row(row.optLong("id")).optString("dest", "");
        if (dest.isEmpty()) return;
        if (Build.VERSION.SDK_INT >= 29) {
            try (OutputStream o = ctx.getContentResolver().openOutputStream(Uri.parse(dest), "wt")) {
                if (o != null) o.flush();
            }
        } else {
            new FileOutputStream(dest, false).close();
        }
    }

    private void download(long id, JSONObject row, AtomicBoolean stop) throws IOException {
        long[] have = {0};
        OutputStream out = openDest(id, row, have);
        if (out == null) throw new IOException("Cannot write to Downloads");
        long total = row.optLong("size", -1);
        InputStream in;
        HttpURLConnection conn = null;
        if ("cloud".equals(row.optString("srcKind"))) {
            in = Backends.files(ctx).openRead(row.optString("src"), have[0]);
        } else {
            String server = PcLink.resolve(ctx);
            if (server.isEmpty()) {
                out.close();
                throw new Uploader.Refused(-1, "PC not reachable. At home: same Wi-Fi. Away: Tailscale on.");
            }
            conn = (HttpURLConnection) new URL(server + row.optString("src")).openConnection();
            conn.setRequestProperty("X-Pairing-Token", TokenStore.pcToken(ctx));
            conn.setConnectTimeout(10000);
            conn.setReadTimeout(60000);
            conn.setRequestProperty("Connection", "close");
            String etag = row.optString("etag", "");
            if (have[0] > 0) {
                conn.setRequestProperty("Range", "bytes=" + have[0] + "-");
                if (!etag.isEmpty()) conn.setRequestProperty("If-Range", etag);
            }
            int code = conn.getResponseCode();
            if (code == 416 && have[0] > 0 && (total <= 0 || have[0] >= total)) {
                out.close();
                finishDownload(id, row);
                conn.disconnect();
                return;
            }
            if (code != 200 && code != 206) {
                out.close();
                String body = readAll(conn.getErrorStream());
                String msg = "PC answered HTTP " + code;
                try {
                    msg = new JSONObject(body).optString("error", msg);
                } catch (Exception ignored) {
                }
                conn.disconnect();
                throw new Uploader.Refused(code, msg);
            }
            if (code == 200 && have[0] > 0) {
                // Older PC, or the file changed: start over.
                out.close();
                truncateDest(row);
                have[0] = 0;
                out = openDest(id, store.row(id), new long[1]);
            }
            String newTag = conn.getHeaderField("ETag");
            long len = conn.getContentLengthLong();
            ContentValues v = new ContentValues();
            if (newTag != null) v.put("etag", newTag);
            if (len > 0) v.put("size", total = have[0] + len);
            store.updateQuiet(id, v);
            in = conn.getInputStream();
        }
        long got = have[0];
        try (InputStream i = in; OutputStream o = out) {
            byte[] buf = new byte[128 * 1024];
            for (int n; (n = i.read(buf)) != -1; ) {
                if (stop.get()) throw new IOException("cancelled");
                o.write(buf, 0, n);
                got += n;
                progress(id, got);
            }
            o.flush();
        } finally {
            if (conn != null) conn.disconnect();
        }
        if (total > 0 && got < total) throw new IOException("The connection dropped before the file was complete");
        finishDownload(id, store.row(id));
    }

    private void finishDownload(long id, JSONObject row) {
        String dest = row.optString("dest", "");
        if (Build.VERSION.SDK_INT >= 29) {
            ContentValues done = new ContentValues();
            done.put(MediaStore.MediaColumns.IS_PENDING, 0);
            ctx.getContentResolver().update(Uri.parse(dest), done, null, null);
        } else {
            File part = new File(dest);
            String name = row.optString("name");
            File target = new File(part.getParentFile(), name);
            for (int n = 1; target.exists(); n++) {
                int dot = name.lastIndexOf('.');
                target = new File(part.getParentFile(), dot > 0 ? name.substring(0, dot) + " (" + n + ")" + name.substring(dot) : name + " (" + n + ")");
            }
            if (part.renameTo(target)) {
                ContentValues v = new ContentValues();
                v.put("dest", target.getAbsolutePath());
                store.updateQuiet(id, v);
                Intent scan = new Intent(Intent.ACTION_MEDIA_SCANNER_SCAN_FILE);
                scan.setData(Uri.fromFile(target));
                ctx.sendBroadcast(scan);
            }
        }
    }

    /** A cancelled or dismissed download: never leave half a file looking like a finished one. */
    private void dropPartial(JSONObject row) {
        if (row == null || !"download".equals(row.optString("type")) || "done".equals(row.optString("state"))) return;
        String dest = row.optString("dest", "");
        if (dest.isEmpty()) return;
        try {
            if (Build.VERSION.SDK_INT >= 29) ctx.getContentResolver().delete(Uri.parse(dest), null, null);
            else new File(dest).delete();
        } catch (Exception ignored) {
        }
    }

    private void releaseUri(JSONObject row) {
        if (row == null || !"uri".equals(row.optString("srcKind"))) return;
        try {
            ctx.getContentResolver().releasePersistableUriPermission(Uri.parse(row.optString("src")), Intent.FLAG_GRANT_READ_URI_PERMISSION);
        } catch (Exception ignored) {
        }
    }

    // ---- telling the page and the notification shade -------------------------------------------------------------

    private void setState(long id, String state, String error) {
        ContentValues v = new ContentValues();
        v.put("state", state);
        v.put("error", error == null ? "" : error);
        store.update(id, v);
        publish(id);
    }

    private JSONObject event(JSONObject row) {
        long id = row.optLong("id");
        JSONObject o = new JSONObject();
        try {
            String state = row.optString("state");
            o.put("id", id);
            o.put("type", row.optString("type"));
            o.put("name", row.optString("name"));
            o.put("state", state);
            o.put("bytes", row.optLong("bytes"));
            o.put("total", row.optLong("size", -1));
            double[] s = speed.get(id);
            o.put("speed", s == null ? 0 : Math.round(s[0]));
            String err = row.optString("error", "");
            if (!err.isEmpty() && !"active".equals(state)) o.put("error", err);
            if (!row.optString("note", "").isEmpty()) o.put("note", row.optString("note"));
            o.put("canRetry", "failed".equals(state) || "cancelled".equals(state) || "held".equals(state));
            o.put("resumable", row.optLong("bytes") > 0 && row.optLong("size", -1) > 0);
            o.put("attempt", row.optInt("attempts", 0));
            Long at = retryAt.get(id);
            if (at != null) o.put("retryIn", Math.max(0, (at - System.currentTimeMillis()) / 1000));
        } catch (Exception ignored) {
        }
        return o;
    }

    private void publish(long id) {
        JSONObject row = store.row(id);
        if (row == null) return;
        JSONObject e = event(row);
        if (!row.optString("task", "").isEmpty()) try { e.put("task", row.optString("task")); } catch (Exception ignored) { }
        for (Listener l : listeners) l.onTransfer(e);
        notifyProgress(row, e);
        publishTask(row);
    }

    private void createChannel() {
        if (Build.VERSION.SDK_INT < 26) return;
        NotificationChannel ch = new NotificationChannel(CHANNEL, "File Transfers", NotificationManager.IMPORTANCE_LOW);
        ch.setDescription("Shows file transfers between this phone and the PC");
        ch.enableVibration(false);
        NotificationManager nm = (NotificationManager) ctx.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm != null) nm.createNotificationChannel(ch);
    }

    Notification.Builder builder(String title, String text, int icon) {
        Notification.Builder b = Build.VERSION.SDK_INT >= 26 ? new Notification.Builder(ctx, CHANNEL) : new Notification.Builder(ctx);
        Intent open = new Intent(ctx, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP);
        int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 23 ? PendingIntent.FLAG_IMMUTABLE : 0);
        return b.setSmallIcon(icon).setContentTitle(title).setContentText(text).setOnlyAlertOnce(true)
                .setContentIntent(PendingIntent.getActivity(ctx, 0, open, flags));
    }

    private void post(int id, Notification.Builder b) {
        NotificationManager nm = (NotificationManager) ctx.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm == null) return;
        try {
            nm.notify(id, b.build());
        } catch (Exception ignored) {
        }
    }

    private final Map<Long, Long> lastNotified = new ConcurrentHashMap<>();

    private void notifyProgress(JSONObject row, JSONObject e) {
        long id = row.optLong("id");
        if (!"active".equals(e.optString("state"))) return;
        long now = SystemClock.elapsedRealtime();
        Long last = lastNotified.get(id);
        if (last != null && now - last < 700) return;
        lastNotified.put(id, now);
        boolean up = "upload".equals(e.optString("type"));
        long total = e.optLong("total", -1), done = e.optLong("bytes");
        post((int) (5000 + id), builder(up ? Tr.t(ctx, "Sending {0}", e.optString("name")) : Tr.t(ctx, "Downloading {0}", e.optString("name")),
                fmt(e.optLong("speed")) + "/s • " + fmt(done) + (total > 0 ? " / " + fmt(total) : ""),
                up ? android.R.drawable.stat_sys_upload : android.R.drawable.stat_sys_download)
                .setOngoing(true).setProgress(100, total > 0 ? (int) (done * 100 / total) : 0, total <= 0));
    }

    private void notifyDone(long id) {
        JSONObject row = store.row(id);
        if (row == null) return;
        boolean up = "upload".equals(row.optString("type"));
        String note = row.optString("note", "");
        String title = up ? (note.isEmpty() ? Tr.t(ctx, "Sent to PC") : Tr.t(ctx, "Saved to Google Drive")) : Tr.t(ctx, "Saved to Downloads");
        Notification.Builder b = builder(title,
                row.optString("name") + " (" + fmt(row.optLong("bytes")) + ")",
                up ? android.R.drawable.stat_sys_upload_done : android.R.drawable.stat_sys_download_done).setAutoCancel(true);
        if (!up) {
            try {
                String dest = row.optString("dest");
                Uri uri = Build.VERSION.SDK_INT >= 29 ? Uri.parse(dest) : Uri.fromFile(new File(dest));
                Intent open = new Intent(Intent.ACTION_VIEW).setDataAndType(uri, mimeOf(row.optString("name")))
                        .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_GRANT_READ_URI_PERMISSION);
                int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 23 ? PendingIntent.FLAG_IMMUTABLE : 0);
                b.setContentIntent(PendingIntent.getActivity(ctx, (int) (5000 + id), open, flags));
            } catch (Exception ignored) {
            }
        }
        post((int) (5000 + id), b);
    }

    private void notifyFailed(long id, String msg) {
        JSONObject row = store.row(id);
        if (row == null) return;
        boolean up = "upload".equals(row.optString("type"));
        post((int) (5000 + id), builder(up ? Tr.t(ctx, "Upload stopped") : Tr.t(ctx, "Download stopped"),
                Tr.t(ctx, "{0}: {1} — open the app to retry", row.optString("name"), msg), android.R.drawable.stat_notify_error).setAutoCancel(true));
    }

    static String fmt(long bytes) {
        if (bytes <= 0) return "0 B";
        final String[] units = {"B", "KB", "MB", "GB", "TB"};
        int g = Math.min(units.length - 1, (int) (Math.log10(bytes) / Math.log10(1024)));
        return String.format(Locale.US, "%.1f %s", bytes / Math.pow(1024, g), units[g]);
    }

    // ---- storage ------------------------------------------------------------------------------------------------

    private static final class Store extends SQLiteOpenHelper {
        Store(Context c) {
            super(c, "transfers.db", null, 4);
        }

        @Override
        public void onCreate(SQLiteDatabase db) {
            db.execSQL("CREATE TABLE t (id INTEGER PRIMARY KEY AUTOINCREMENT, type TEXT, name TEXT, srcKind TEXT, src TEXT, target TEXT, "
                    + "size INTEGER DEFAULT -1, uploadId TEXT, dest TEXT, etag TEXT, bytes INTEGER DEFAULT 0, state TEXT, error TEXT, "
                    + "note TEXT, attempts INTEGER DEFAULT 0, created INTEGER, finished INTEGER DEFAULT 0, deleteSrc INTEGER DEFAULT 0, pcId TEXT DEFAULT '', "
                    + "task TEXT DEFAULT '')");
            db.execSQL("CREATE TABLE tk (id TEXT PRIMARY KEY, meta TEXT, started INTEGER)");
        }

        @Override
        public void onUpgrade(SQLiteDatabase db, int from, int to) {
            if (from < 2) db.execSQL("ALTER TABLE t ADD COLUMN deleteSrc INTEGER DEFAULT 0");
            // Which saved PC a transfer is for: it waits for that PC instead of going to another one.
            if (from < 3) db.execSQL("ALTER TABLE t ADD COLUMN pcId TEXT DEFAULT ''");
            // Transfers as tasks: each row belongs to one, and a task's own details are kept beside.
            if (from < 4) {
                db.execSQL("ALTER TABLE t ADD COLUMN task TEXT DEFAULT ''");
                db.execSQL("CREATE TABLE tk (id TEXT PRIMARY KEY, meta TEXT, started INTEGER)");
            }
        }

        synchronized List<Long> ids(String where) {
            List<Long> out = new ArrayList<>();
            try (Cursor c = getReadableDatabase().rawQuery("SELECT id FROM t WHERE " + where, null)) {
                while (c.moveToNext()) out.add(c.getLong(0));
            }
            return out;
        }

        synchronized long insert(ContentValues v) {
            v.put("created", System.currentTimeMillis());
            return getWritableDatabase().insert("t", null, v);
        }

        synchronized void update(long id, ContentValues v) {
            getWritableDatabase().update("t", v, "id=?", new String[]{String.valueOf(id)});
        }

        void updateQuiet(long id, ContentValues v) {
            update(id, v);
        }

        synchronized void delete(long id) {
            getWritableDatabase().delete("t", "id=?", new String[]{String.valueOf(id)});
        }

        synchronized int count(String where) {
            try (Cursor c = getReadableDatabase().rawQuery("SELECT COUNT(*) FROM t WHERE " + where, null)) {
                return c.moveToFirst() ? c.getInt(0) : 0;
            }
        }

        synchronized void interrupted() {
            ContentValues v = new ContentValues();
            v.put("state", "failed");
            v.put("error", "Stopped when the app was closed — tap Retry to continue");
            getWritableDatabase().update("t", v, "state IN ('queued','active','waiting')", null);
            // Keep the list short: finished ones older than a day go.
            getWritableDatabase().delete("t", "state IN ('done','cancelled') AND created < ?",
                    new String[]{String.valueOf(System.currentTimeMillis() - 86400_000L)});
        }

        private static JSONObject toJson(Cursor c) {
            JSONObject o = new JSONObject();
            try {
                for (int i = 0; i < c.getColumnCount(); i++) {
                    if (c.isNull(i)) continue;
                    if (c.getType(i) == Cursor.FIELD_TYPE_INTEGER) o.put(c.getColumnName(i), c.getLong(i));
                    else o.put(c.getColumnName(i), c.getString(i));
                }
            } catch (Exception ignored) {
            }
            return o;
        }

        synchronized JSONObject row(long id) {
            try (Cursor c = getReadableDatabase().rawQuery("SELECT * FROM t WHERE id=?", new String[]{String.valueOf(id)})) {
                return c.moveToFirst() ? toJson(c) : null;
            }
        }

        synchronized void putTask(String id, JSONObject meta) {
            ContentValues v = new ContentValues();
            long started = System.currentTimeMillis();
            JSONObject old = taskMeta(id);
            if (old != null) started = old.optLong("started", started);
            try { meta.put("started", started); } catch (Exception ignored) { }
            v.put("id", id);
            v.put("meta", meta.toString());
            v.put("started", started);
            getWritableDatabase().insertWithOnConflict("tk", null, v, SQLiteDatabase.CONFLICT_REPLACE);
            // Keep the newest 80.
            getWritableDatabase().execSQL("DELETE FROM tk WHERE id NOT IN (SELECT id FROM tk ORDER BY started DESC LIMIT 80)");
        }

        synchronized JSONObject taskMeta(String id) {
            try (Cursor c = getReadableDatabase().rawQuery("SELECT meta FROM tk WHERE id=?", new String[]{id})) {
                return c.moveToFirst() ? new JSONObject(c.getString(0)) : null;
            } catch (Exception e) {
                return null;
            }
        }

        synchronized List<JSONObject> rowsOf(String task) {
            List<JSONObject> out = new ArrayList<>();
            try (Cursor c = getReadableDatabase().rawQuery("SELECT * FROM t WHERE task=? ORDER BY id", new String[]{task})) {
                while (c.moveToNext()) out.add(toJson(c));
            }
            return out;
        }

        synchronized List<String> recentTasks(int max) {
            List<String> out = new ArrayList<>();
            try (Cursor c = getReadableDatabase().rawQuery("SELECT id FROM tk ORDER BY started DESC LIMIT " + max, null)) {
                while (c.moveToNext()) out.add(c.getString(0));
            }
            return out;
        }

        /** What the page lists: everything not finished, plus what finished in the last two minutes. */
        synchronized JSONArray visible() {
            JSONArray a = new JSONArray();
            try (Cursor c = getReadableDatabase().rawQuery("SELECT * FROM t WHERE state NOT IN ('done','cancelled') OR finished > ? ORDER BY id",
                    new String[]{String.valueOf(System.currentTimeMillis() - 120_000L)})) {
                while (c.moveToNext()) a.put(toJson(c));
            }
            return a;
        }
    }

    /** The rows as the page wants them (same shape as live events). */
    JSONArray events() {
        JSONArray rows = store.visible();
        JSONArray out = new JSONArray();
        for (int i = 0; i < rows.length(); i++) out.put(event(rows.optJSONObject(i)));
        return out;
    }

    List<Long> ids() {
        JSONArray rows = store.visible();
        List<Long> out = new ArrayList<>();
        for (int i = 0; i < rows.length(); i++) out.add(rows.optJSONObject(i).optLong("id"));
        return out;
    }
}
