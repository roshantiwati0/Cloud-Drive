package com.cloudssd.mobile;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Build;
import android.os.Environment;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * The PC looking at this phone's files, only as far as this phone allows. The app keeps one long request open to the
 * PC in use (/api/peer/inbox) while it runs; the PC's questions (list a folder, send a file) come back on it and are
 * answered on /api/peer/result. Nothing is answered without this phone's own permission ({@link Grants}): a question
 * without it brings up a note here, and the PC is told to wait. Paths are checked to stay inside what was allowed
 * (the shared folders, or the whole phone storage).
 */
final class PeerLink {
    interface Listener {
        /** scope: what to offer first, "all" (the whole storage) or "shared". */
        void onAsk(String pcId, String pcName, String scope);
    }

    private static final String TAG = "CloudSSD.Peer";
    static final String CHANNEL = "peer";
    static final int NOTE_ID = 7701;
    /** The folders "shared" lets the PC see. */
    static final String[] SHARED = {"DCIM", "Pictures", "Movies", "Music", "Download", "Documents"};
    private static final int MAX_ITEMS = 2000;

    private static PeerLink instance;

    static synchronized PeerLink get(Context c) {
        if (instance == null) instance = new PeerLink(c.getApplicationContext());
        return instance;
    }

    private final Context ctx;
    private final ExecutorService jobs = Executors.newFixedThreadPool(2);
    private volatile boolean running;
    private volatile Listener listener;
    private long lastAsk;
    /** Wakes the loop from its wait (see {@link #kick}). */
    private final Object wake = new Object();
    private volatile boolean kicked;
    /** The long request open now, so a kick can end it at once (another PC chosen, just paired or unblocked). */
    private volatile HttpURLConnection waiting;

    private PeerLink(Context c) {
        ctx = c;
        appCtx = c;
    }

    /** For the kinds of files (a static walk): Android's media index is asked through it. */
    private static volatile Context appCtx;

    void setListener(Listener l) {
        listener = l;
    }

    void start() {
        synchronized (this) {
            if (running) return;
            running = true;
        }
        Thread t = new Thread(this::loop, "cloudssd-peer");
        t.setDaemon(true);
        t.start();
    }

    /**
     * Something changed (paired, another PC chosen, back online after being blocked): start asking the PC now instead
     * of after the current wait (up to a minute after a failure).
     */
    void kick() {
        kicked = true;
        HttpURLConnection c = waiting;
        if (c != null) {
            try {
                c.disconnect();
            } catch (Exception ignored) {
            }
        }
        synchronized (wake) {
            wake.notifyAll();
        }
    }

    private void sleep(long ms) {
        synchronized (wake) {
            if (kicked) return;
            try {
                wake.wait(ms);
            } catch (InterruptedException ignored) {
            }
        }
    }

    /**
     * True while this phone should keep answering the PC with the app closed: "Find my phone" is on, or the PC in use
     * may look at this phone's files right now (its grant has not run out).
     */
    static boolean neededInBackground(Context c) {
        SharedPreferences p = c.getSharedPreferences("cloudssd", Context.MODE_PRIVATE);
        if (p.getBoolean("peer_paused", false)) return false;
        if (p.getBoolean("find_my_phone", false)) return true;
        String pc = PcProfiles.activeId(c);
        return pc != null && !pc.isEmpty() && Grants.get(c, pc) != null;
    }

    private void loop() {
        long backoff = 2000;
        while (running) {
            kicked = false;
            try {
                String pc = PcProfiles.activeId(ctx);
                SharedPreferences p = ctx.getSharedPreferences("cloudssd", Context.MODE_PRIVATE);
                String token = TokenStore.pcToken(ctx);
                // A grant that ran out (or Find my phone switched off) ends the background service. (Starting it is
                // left to the app, the note's buttons and boot: Android does not let a background thread start one.)
                PeerLinkService.stopIfNotNeeded(ctx);
                if (pc == null || pc.isEmpty() || token.isEmpty() || !p.getString("features", "[]").contains("\"peer\"")) {
                    sleep(20000);
                    continue;
                }
                String server = PcLink.resolve(ctx);
                if (server.isEmpty()) {
                    backoff = Math.min(backoff * 2, 60000);
                    sleep(backoff);
                    continue;
                }
                JSONObject r = new JSONObject(waitForJob(server + "/api/peer/inbox?wait=25" + vitals(), token));
                backoff = 2000;
                final JSONObject job = r.optJSONObject("job");
                if (job != null) {
                    final String s = server, pcId = pc;
                    jobs.execute(() -> handle(s, token, pcId, job));
                }
            } catch (Exception e) {
                if (kicked) {
                    backoff = 2000;   // ended on purpose: ask again right away
                    continue;
                }
                backoff = Math.min(backoff * 2, 60000);
                sleep(backoff);
            }
        }
    }

    /** The long wait for the PC's next question; {@link #kick} can end it. */
    private String waitForJob(String url, String token) throws IOException {
        HttpURLConnection c = open(url, token, "GET", 35000);
        waiting = c;
        try {
            int code = c.getResponseCode();
            InputStream in = code >= 400 ? c.getErrorStream() : c.getInputStream();
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            if (in != null) try (InputStream i = in) {
                byte[] buf = new byte[8192];
                for (int n; (n = i.read(buf)) > 0; ) out.write(buf, 0, n);
            }
            if (code >= 400) throw new IOException("PC answered HTTP " + code);
            return out.toString("UTF-8");
        } finally {
            waiting = null;
            c.disconnect();
        }
    }

    // ---- answering -----------------------------------------------------------------------------------------------------

    private void handle(String server, String token, String pc, JSONObject job) {
        String id = job.optString("id");
        String kind = job.optString("kind");
        JSONObject args = job.optJSONObject("args");
        String path = args == null ? "" : args.optString("path", "");
        try {
            // Ringing and the clipboard need no file permission: anything the paired PC may do anyway, and seen here.
            if ("phone.ring".equals(kind)) {
                PhoneRinger.ring(ctx, args == null ? "" : args.optString("from", ""));
                answerJson(server, token, id, new JSONObject().put("ok", true));
                return;
            }
            if ("clip.set".equals(kind)) {
                final String text = args == null ? "" : args.optString("text", "");
                final String from = args == null || args.optString("from", "").isEmpty() ? Tr.t(ctx, "the PC") : args.optString("from", "");
                new android.os.Handler(android.os.Looper.getMainLooper()).post(() -> {
                    android.content.ClipboardManager cm = (android.content.ClipboardManager) ctx.getSystemService(Context.CLIPBOARD_SERVICE);
                    if (cm != null) cm.setPrimaryClip(android.content.ClipData.newPlainText("Cloud-SSD", text));
                    android.widget.Toast.makeText(ctx, Tr.t(ctx, "Clipboard from {0}", from), android.widget.Toast.LENGTH_SHORT).show();
                });
                answerJson(server, token, id, new JSONObject().put("ok", true));
                return;
            }
            if ("fs.pullEnd".equals(kind)) {
                String task = args == null ? "" : args.optString("task", "");
                // Why the PC stopped: "cancelled" (someone pressed Cancel there) or "network" (it broke off).
                if (!task.isEmpty()) {
                    endedRuns.put(task, args.optString("reason", "network"));
                    TransferHub.get(ctx).externalFinish(task, args.optString("reason", "network"));
                }
                answerJson(server, token, id, new JSONObject().put("ok", true));
                return;
            }
            Grants.Grant g = Grants.get(ctx, pc);
            if ("fs.askAll".equals(kind)) {
                // The PC wants to show this phone as the phone shows itself: the whole storage. This phone's person decides.
                if (g != null && "all".equals(g.scope)) { answerJson(server, token, id, new JSONObject().put("scope", "all")); return; }
                ask(pc, args == null ? "" : args.optString("asker", ""), g != null && g.write, "all");
                answerJson(server, token, id, new JSONObject().put("asked", true));
                return;
            }
            if (g == null) {
                // Through the PC, another of its devices (a laptop) may be the one asking: say who.
                ask(pc, args == null ? "" : args.optString("asker", ""));
                answer(server, token, id, "needs-permission", null);
                return;
            }
            switch (kind) {
                case "fs.roots":
                    answerJson(server, token, id, roots(g));
                    break;
                case "fs.list": {
                    File dir = resolve(g, path);
                    if (dir == null || !dir.isDirectory()) answer(server, token, id, "denied", "Not allowed or not there: " + path);
                    else answerJson(server, token, id, list(dir));
                    break;
                }
                case "fs.read": {
                    File f = resolve(g, path);
                    if (f == null || !f.isFile()) answer(server, token, id, "denied", "Not allowed or not there: " + path);
                    else answerFile(server, token, id, f);
                    break;
                }
                case "fs.cats": {
                    // How many photos, videos… the PC may see, and how big they are.
                    answerJson(server, token, id, cats(g, args != null && "1".equals(args.optString("refresh"))));
                    break;
                }
                case "fs.cat": {
                    answerJson(server, token, id, cat(g, args == null ? "" : args.optString("cat", "")));
                    break;
                }
                case "fs.tree": {
                    // Everything under a folder, for "Save to PC" of a whole folder.
                    File dir = resolve(g, path);
                    if (dir == null || !dir.isDirectory()) answer(server, token, id, "denied", "Not allowed or not there: " + path);
                    else answerJson(server, token, id, tree(dir));
                    break;
                }
                case "fs.mkdir":
                case "fs.pull": {
                    // Adding to this phone: only when its owner said so ("May add files").
                    if (!g.write) {
                        ask(pc, args == null ? "" : args.optString("asker", ""), true);
                        answer(server, token, id, "needs-permission", "write");
                        break;
                    }
                    if (Build.VERSION.SDK_INT >= 30 && !Environment.isExternalStorageManager()) {
                        answer(server, token, id, "error", "Turn on All files access for Cloud-SSD on this phone first.");
                        break;
                    }
                    File dir = resolve(g, path);
                    String sub = args == null ? "" : args.optString("sub", "");
                    if (dir != null && !sub.isEmpty()) dir = resolve(g, (path.isEmpty() ? "" : path + "/") + sub);
                    if (dir == null) {
                        answer(server, token, id, "denied", "Not allowed here: " + path);
                        break;
                    }
                    if (!dir.isDirectory() && !dir.mkdirs()) {
                        answer(server, token, id, "error", "Could not make the folder " + relOf(dir));
                        break;
                    }
                    if ("fs.mkdir".equals(kind)) {
                        String name = safeName(args == null ? "" : args.optString("name", ""));
                        if (name.isEmpty()) {
                            answerJson(server, token, id, new JSONObject().put("path", relOf(dir)));
                            break;
                        }
                        File made = resolve(g, relOf(new File(dir, name)));
                        if (made == null) answer(server, token, id, "denied", "Not allowed here.");
                        else if (!made.isDirectory() && !made.mkdirs()) answer(server, token, id, "error", "Could not make the folder.");
                        else answerJson(server, token, id, new JSONObject().put("path", relOf(made)).put("name", made.getName()));
                        break;
                    }
                    answerJson(server, token, id, pullAsTask(server, token, pc, g, dir, args));
                    break;
                }
                default:
                    answer(server, token, id, "error", "This phone does not know " + kind);
            }
        } catch (Exception e) {
            Log.w(TAG, "peer job failed", e);
            try {
                answer(server, token, id, "error", String.valueOf(e.getMessage()));
            } catch (Exception ignored) {
            }
        }
    }

    // ---- kinds of files (Photos, Videos, Audio, Documents, Apps, Archives) -------------------------------------------

    static final String[] CATS = {"photos", "videos", "audio", "docs", "apks", "archives"};
    private static final String[][] CAT_EXT = {
            {"jpg", "jpeg", "png", "gif", "webp", "heic", "heif", "bmp"},
            {"mp4", "mkv", "mov", "avi", "webm", "3gp", "m4v"},
            {"mp3", "m4a", "aac", "wav", "flac", "ogg", "opus", "amr"},
            {"pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "csv", "rtf", "odt", "ods", "odp", "epub"},
            {"apk", "apks", "xapk", "apkm"},
            {"zip", "rar", "7z", "tar", "gz", "tgz", "bz2", "iso"}};
    private static final java.util.HashMap<String, Integer> EXT_CAT = new java.util.HashMap<>();

    static {
        for (int i = 0; i < CAT_EXT.length; i++) for (String e : CAT_EXT[i]) EXT_CAT.put(e, i);
    }

    /** The kind of a file by its name's ending: an index into CATS, or -1. */
    static int catOf(String name) {
        int dot = name.lastIndexOf('.');
        if (dot < 0 || dot == name.length() - 1) return -1;
        Integer c = EXT_CAT.get(name.substring(dot + 1).toLowerCase(java.util.Locale.ROOT));
        return c == null ? -1 : c;
    }

    private static final class CatFile {
        final String path, name;
        final long size, modified;
        final int cat;

        CatFile(String path, String name, long size, long modified, int cat) {
            this.path = path;
            this.name = name;
            this.size = size;
            this.modified = modified;
            this.cat = cat;
        }
    }

    /** The last walk of the storage, kept a minute (per scope) so opening one kind after the counts costs nothing. */
    private static java.util.ArrayList<CatFile> catCache;
    private static String catCacheScope = "";
    private static long catCacheAt;
    private static final int MAX_CAT_WALK = 300000, MAX_CAT_LIST = 3000;

    private static synchronized java.util.ArrayList<CatFile> catFiles(Grants.Grant g, boolean refresh) {
        long now = System.currentTimeMillis();
        if (!refresh && catCache != null && catCacheScope.equals(g.scope) && now - catCacheAt < 60000) return catCache;
        java.util.ArrayList<CatFile> out = new java.util.ArrayList<>();
        // The whole storage allowed: the same count as this phone's own Phone tab (Android's media index, which also
        // has WhatsApp and other apps' shared media), so the PC shows the numbers the phone shows.
        if ("all".equals(g.scope) && appCtx != null && mediaIndex(appCtx, out)) {
            catCache = out;
            catCacheScope = g.scope;
            catCacheAt = now;
            return out;
        }
        out.clear();
        java.util.ArrayDeque<File> todo = new java.util.ArrayDeque<>();
        if ("all".equals(g.scope)) todo.add(storageRoot());
        else for (String s : SHARED) todo.add(new File(storageRoot(), s));
        int seen = 0;
        while (!todo.isEmpty() && seen < MAX_CAT_WALK) {
            File d = todo.poll();
            File[] items = d.listFiles();
            if (items == null) continue;
            for (File f : items) {
                String n = f.getName();
                if (n.startsWith(".")) continue;
                if (f.isDirectory()) {
                    // Other apps' data stays out, as everywhere.
                    if (d.equals(storageRoot()) && n.equals("Android")) continue;
                    todo.add(f);
                    continue;
                }
                seen++;
                int c = catOf(n);
                if (c >= 0) out.add(new CatFile(relOf(f), n, f.length(), f.lastModified(), c));
            }
        }
        catCache = out;
        catCacheScope = g.scope;
        catCacheAt = now;
        return out;
    }

    /**
     * The kinds from Android's media index, as MainActivity.buildStorageReport counts them: pictures, videos and audio by
     * the index's own type; the rest by name (documents, apps, archives). False when the index cannot be read.
     */
    private static boolean mediaIndex(Context c, java.util.ArrayList<CatFile> out) {
        String[] proj = {android.provider.MediaStore.MediaColumns.DATA, android.provider.MediaStore.MediaColumns.SIZE,
                android.provider.MediaStore.Files.FileColumns.MEDIA_TYPE, android.provider.MediaStore.MediaColumns.DATE_MODIFIED};
        String root = storageRoot().getAbsolutePath() + "/";
        try (android.database.Cursor cur = c.getContentResolver().query(android.provider.MediaStore.Files.getContentUri("external"), proj, null, null, null)) {
            if (cur == null) return false;
            while (cur.moveToNext()) {
                String path = cur.getString(0);
                long size = cur.getLong(1);
                if (path == null || size <= 0 || !path.startsWith(root)) continue;
                int type = cur.getInt(2);
                String name = path.substring(path.lastIndexOf('/') + 1);
                int cat;
                if (type == android.provider.MediaStore.Files.FileColumns.MEDIA_TYPE_IMAGE) cat = 0;
                else if (type == android.provider.MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO) cat = 1;
                else if (type == android.provider.MediaStore.Files.FileColumns.MEDIA_TYPE_AUDIO) cat = 2;
                else {
                    cat = catOf(name);
                    if (cat < 3) continue;                       // a picture or clip the index does not show: "other" on the phone too
                }
                out.add(new CatFile(path.substring(root.length()), name, size, cur.getLong(3) * 1000L, cat));
            }
            return true;
        } catch (Exception e) {
            Log.w(TAG, "media index failed", e);
            return false;
        }
    }

    /** Files changed (the PC added some): the kinds are counted again next time. */
    static synchronized void forgetCats() {
        catCache = null;
    }

    private static JSONObject cats(Grants.Grant g, boolean refresh) throws JSONException {
        int[] count = new int[CATS.length];
        long[] bytes = new long[CATS.length];
        for (CatFile f : catFiles(g, refresh)) {
            count[f.cat]++;
            bytes[f.cat] += f.size;
        }
        JSONArray a = new JSONArray();
        for (int i = 0; i < CATS.length; i++) a.put(new JSONObject().put("key", CATS[i]).put("count", count[i]).put("bytes", bytes[i]));
        return new JSONObject().put("cats", a);
    }

    /** The files of one kind, newest first (at most MAX_CAT_LIST). */
    private static JSONObject cat(Grants.Grant g, String key) throws JSONException {
        int want = Arrays.asList(CATS).indexOf(key);
        java.util.ArrayList<CatFile> mine = new java.util.ArrayList<>();
        for (CatFile f : catFiles(g, false)) if (f.cat == want) mine.add(f);
        java.util.Collections.sort(mine, (a, b) -> Long.compare(b.modified, a.modified));
        JSONArray files = new JSONArray();
        for (int i = 0; i < mine.size() && i < MAX_CAT_LIST; i++) {
            CatFile f = mine.get(i);
            files.put(new JSONObject().put("name", f.name).put("path", f.path).put("size", f.size).put("modified", f.modified));
        }
        return new JSONObject().put("cat", key).put("files", files).put("total", mine.size()).put("truncated", mine.size() > MAX_CAT_LIST);
    }

    /** Most entries fs.tree lists (a whole project folder fits; past this the PC says some were left out). */
    private static final int MAX_TREE = 20000;

    /** Every file under a folder (not hidden ones), with its path inside that folder, for saving the folder whole. */
    private static JSONObject tree(File top) throws JSONException {
        JSONArray files = new JSONArray(), folders = new JSONArray();
        java.util.ArrayDeque<File> todo = new java.util.ArrayDeque<>();
        todo.add(top);
        String base = top.getAbsolutePath();
        long bytes = 0;
        int n = 0;
        boolean truncated = false;
        while (!todo.isEmpty()) {
            File d = todo.poll();
            File[] items = d.listFiles();
            if (items == null) continue;
            Arrays.sort(items, (a, b) -> a.getName().compareToIgnoreCase(b.getName()));
            for (File f : items) {
                if (f.getName().startsWith(".")) continue;
                if (++n > MAX_TREE) {
                    truncated = true;
                    todo.clear();
                    break;
                }
                String rel = f.getAbsolutePath().substring(base.length() + 1).replace(File.separatorChar, '/');
                if (f.isDirectory()) {
                    folders.put(rel);
                    todo.add(f);
                } else {
                    files.put(new JSONObject().put("rel", rel).put("path", relOf(f)).put("size", f.length()).put("modified", f.lastModified()));
                    bytes += f.length();
                }
            }
        }
        return new JSONObject().put("path", relOf(top)).put("name", top.getName()).put("folders", folders).put("files", files)
                .put("bytes", bytes).put("truncated", truncated);
    }

    /** A file or folder name the PC asked for, without folders or characters a phone folder cannot hold. */
    private static String safeName(String name) {
        String n = name == null ? "" : name.replaceAll("[\\\\/:*?\"<>|\\u0000-\\u001f]", "_").trim();
        if (n.equals(".") || n.equals("..") || n.startsWith(".")) n = n.replaceFirst("^\\.+", "_");
        return n.length() > 180 ? n.substring(0, 180) : n;
    }

    /**
     * fs.pull: the PC staged a file; fetch it (one-time url on the same PC) into the folder. onClash: keep (a free name
     * "a (2).jpg"), replace, or skip when the name is taken.
     */
    private JSONObject pull(String server, String token, Grants.Grant g, File dir, JSONObject args) throws IOException, JSONException {
        String name = safeName(args.optString("name", ""));
        String url = args.optString("url", "");
        String onClash = args.optString("onClash", "keep");
        if (name.isEmpty() || !url.startsWith("/api/peer/blob")) throw new IOException("Bad file to add.");
        File target = new File(dir, name);
        if (resolve(g, relOf(target)) == null) throw new IOException("Not allowed here.");
        if (target.exists()) {
            if ("skip".equals(onClash)) return new JSONObject().put("path", relOf(target)).put("name", name).put("skipped", true);
            if (!"replace".equals(onClash) || target.isDirectory()) target = freeFile(dir, name);
        }
        File part = new File(dir, ".cssd-part-" + Long.toHexString(System.nanoTime()));
        HttpURLConnection c = open(server + url, token, "GET", 10 * 60 * 1000);
        try {
            int code = c.getResponseCode();
            if (code >= 400) throw new IOException("The PC answered HTTP " + code);
            try (InputStream in = c.getInputStream(); OutputStream out = new java.io.FileOutputStream(part)) {
                byte[] buf = new byte[64 * 1024];
                for (int k; (k = in.read(buf)) > 0; ) out.write(buf, 0, k);
            }
        } catch (IOException e) {
            //noinspection ResultOfMethodCallIgnored
            part.delete();
            throw e;
        } finally {
            c.disconnect();
        }
        long modified = args.optLong("modified", 0);
        if (target.exists() && !target.delete()) {
            //noinspection ResultOfMethodCallIgnored
            part.delete();
            throw new IOException("Could not replace " + name);
        }
        if (!part.renameTo(target)) {
            //noinspection ResultOfMethodCallIgnored
            part.delete();
            throw new IOException("Could not save " + name);
        }
        if (modified > 0) //noinspection ResultOfMethodCallIgnored
            target.setLastModified(modified);
        forgetCats();
        // The gallery and other apps see it at once.
        try {
            android.media.MediaScannerConnection.scanFile(ctx, new String[]{target.getAbsolutePath()}, null, null);
        } catch (Exception ignored) {
        }
        return new JSONObject().put("path", relOf(target)).put("name", target.getName()).put("skipped", false);
    }

    /** Runs the PC ended early (fs.pullEnd), with why: a file of such a run that fails afterwards failed for that reason. */
    private final java.util.concurrent.ConcurrentHashMap<String, String> endedRuns = new java.util.concurrent.ConcurrentHashMap<>();

    /**
     * fs.pull as part of the PC's run (args.task = its id): the phone's Transfers page shows the run as one task —
     * files and size from the PC, each file as it lands, ended with the last one (args.last) or fs.pullEnd.
     */
    private JSONObject pullAsTask(String server, String token, String pc, Grants.Grant g, File dir, JSONObject args) throws IOException, JSONException {
        String task = args.optString("task", "");
        if (task.isEmpty()) return pull(server, token, g, dir, args);
        JSONObject rec = PcProfiles.get(ctx, pc);
        String pcName = rec == null ? "" : rec.optString("label", rec.optString("name", ""));
        String top = args.optString("path", "");
        JSONObject meta = new JSONObject().put("id", task).put("dir", "toPhone")
                .put("kind", args.optString("folder", "").isEmpty() ? "files" : "folder").put("folder", args.optString("folder", ""))
                .put("device", pcName.isEmpty() ? "PC" : pcName)
                .put("files", args.optInt("files", 1)).put("bytes", args.optLong("bytes", 0))
                .put("saveTo", "Phone storage" + (top.isEmpty() ? "" : " › " + top.replace("/", " › ")))
                .put("openPath", new File(storageRoot(), top).getAbsolutePath());
        TransferHub hub = TransferHub.get(ctx);
        hub.external(meta);
        String name = args.optString("name", "");
        try {
            JSONObject r = pull(server, token, g, dir, args);
            hub.externalFile(task, name, args.optLong("size", 0), true, "");
            return r;
        } catch (IOException e) {
            String m = String.valueOf(e.getMessage());
            // The PC ended the run while this file was on its way: it failed for that reason, not the network.
            String why = endedRuns.get(task);
            hub.externalFile(task, name, 0, false, why != null ? why : m.contains("space") || m.contains("ENOSPC") ? "space" : "network");
            throw e;
        } finally {
            if ("1".equals(args.optString("last", ""))) hub.externalFinish(task);
        }
    }

    /** "a.jpg" taken: "a (2).jpg", "a (3).jpg"… */
    private static File freeFile(File dir, String name) {
        int dot = name.lastIndexOf('.');
        String stem = dot > 0 ? name.substring(0, dot) : name, ext = dot > 0 ? name.substring(dot) : "";
        for (int i = 2; ; i++) {
            File f = new File(dir, stem + " (" + i + ")" + ext);
            if (!f.exists()) return f;
        }
    }

    /** "&battery=83&charging=1&free=…&total=…": shown on the phone's card on the PC. */
    private String vitals() {
        StringBuilder sb = new StringBuilder();
        try {
            android.content.Intent b = ctx.registerReceiver(null, new android.content.IntentFilter(android.content.Intent.ACTION_BATTERY_CHANGED));
            if (b != null) {
                int level = b.getIntExtra(android.os.BatteryManager.EXTRA_LEVEL, -1), scale = b.getIntExtra(android.os.BatteryManager.EXTRA_SCALE, -1);
                int plugged = b.getIntExtra(android.os.BatteryManager.EXTRA_PLUGGED, 0);
                if (level >= 0 && scale > 0) sb.append("&battery=").append(Math.round(level * 100f / scale)).append("&charging=").append(plugged != 0 ? 1 : 0);
            }
            android.os.StatFs st = new android.os.StatFs(storageRoot().getPath());
            sb.append("&free=").append(st.getAvailableBytes()).append("&total=").append(st.getTotalBytes());
        } catch (Exception ignored) {
        }
        return sb.toString();
    }

    static File storageRoot() {
        return Environment.getExternalStorageDirectory();
    }

    /** The file or folder at a path relative to the phone storage, when the grant lets the PC see it; else null. */
    static File resolve(Grants.Grant g, String rel) {
        try {
            File root = storageRoot().getCanonicalFile();
            String clean = rel == null ? "" : rel.replace('\\', '/');
            while (clean.startsWith("/")) clean = clean.substring(1);
            File f = (clean.isEmpty() ? root : new File(root, clean)).getCanonicalFile();
            String rp = root.getPath(), fp = f.getPath();
            if (!fp.equals(rp) && !fp.startsWith(rp + File.separator)) return null;          // outside the phone storage
            String inside = fp.equals(rp) ? "" : fp.substring(rp.length() + 1);
            if (inside.startsWith("Android" + File.separator) || inside.equals("Android")) return null;   // other apps' data
            for (String part : inside.split(File.separator.equals("\\") ? "\\\\" : File.separator))
                if (part.startsWith(".")) return null;                                         // hidden files stay hidden
            if ("all".equals(g.scope)) return f;
            if (inside.isEmpty()) return null;                                                 // "shared": not the top itself
            String first = inside.split(File.separator.equals("\\") ? "\\\\" : File.separator)[0];
            for (String s : SHARED) if (s.equalsIgnoreCase(first)) return f;
            return null;
        } catch (IOException e) {
            return null;
        }
    }

    private static String relOf(File f) {
        String rp = storageRoot().getAbsolutePath();
        String fp = f.getAbsolutePath();
        return fp.length() > rp.length() ? fp.substring(rp.length() + 1).replace(File.separatorChar, '/') : "";
    }

    private static JSONObject roots(Grants.Grant g) throws JSONException {
        JSONArray roots = new JSONArray();
        if ("all".equals(g.scope)) roots.put(new JSONObject().put("name", "Phone storage").put("path", ""));
        else for (String s : SHARED) {
            File d = new File(storageRoot(), s);
            if (d.isDirectory()) roots.put(new JSONObject().put("name", s).put("path", s));
        }
        return new JSONObject().put("scope", g.scope).put("until", g.until).put("roots", roots);
    }

    private static JSONObject list(File dir) throws JSONException {
        JSONArray folders = new JSONArray(), files = new JSONArray();
        File[] items = dir.listFiles();
        boolean truncated = false;
        if (items != null) {
            Arrays.sort(items, (a, b) -> a.getName().compareToIgnoreCase(b.getName()));
            int n = 0;
            for (File f : items) {
                if (f.getName().startsWith(".")) continue;
                if (++n > MAX_ITEMS) {
                    truncated = true;
                    break;
                }
                JSONObject o = new JSONObject().put("name", f.getName()).put("path", relOf(f)).put("modified", f.lastModified());
                if (f.isDirectory()) {
                    String[] kids = f.list();
                    folders.put(o.put("count", kids == null ? 0 : kids.length));
                } else files.put(o.put("size", f.length()));
            }
        }
        return new JSONObject().put("path", relOf(dir)).put("folders", folders).put("files", files).put("truncated", truncated);
    }

    private static String enc(String s) {
        try {
            return URLEncoder.encode(s, "UTF-8").replace("+", "%20");
        } catch (Exception e) {
            return s;
        }
    }

    private void answer(String server, String token, String job, String status, String error) throws IOException {
        request(server + "/api/peer/result?job=" + enc(job) + "&status=" + enc(status) + (error == null ? "" : "&error=" + enc(error)),
                token, "POST", 30000, new byte[0], 0);
    }

    private void answerJson(String server, String token, String job, JSONObject o) throws IOException {
        byte[] body = o.toString().getBytes(StandardCharsets.UTF_8);
        request(server + "/api/peer/result?job=" + enc(job) + "&status=ok", token, "POST", 60000, body, body.length);
    }

    private void answerFile(String server, String token, String job, File f) throws IOException {
        HttpURLConnection c = open(server + "/api/peer/result?job=" + enc(job) + "&status=ok", token, "POST", 10 * 60 * 1000);
        try {
            c.setDoOutput(true);
            c.setRequestProperty("Content-Type", "application/octet-stream");
            c.setFixedLengthStreamingMode(f.length());
            try (InputStream in = new FileInputStream(f); OutputStream out = c.getOutputStream()) {
                byte[] buf = new byte[64 * 1024];
                int n;
                while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
            }
            c.getResponseCode();
        } finally {
            c.disconnect();
        }
    }

    private static HttpURLConnection open(String url, String token, String method, int readMs) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        c.setConnectTimeout(8000);
        c.setReadTimeout(readMs);
        c.setUseCaches(false);
        c.setRequestMethod(method);
        c.setRequestProperty("X-Pairing-Token", token);
        c.setRequestProperty("Connection", "close");
        return c;
    }

    private static String request(String url, String token, String method, int readMs, byte[] body, int length) throws IOException {
        HttpURLConnection c = open(url, token, method, readMs);
        try {
            if (body != null) {
                c.setDoOutput(true);
                c.setRequestProperty("Content-Type", "application/json");
                c.setFixedLengthStreamingMode(length);
                try (OutputStream out = c.getOutputStream()) {
                    out.write(body, 0, length);
                }
            }
            int code = c.getResponseCode();
            InputStream in = code >= 400 ? c.getErrorStream() : c.getInputStream();
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            if (in != null) try (InputStream i = in) {
                byte[] buf = new byte[8192];
                for (int n; (n = i.read(buf)) > 0; ) out.write(buf, 0, n);
            }
            if (code >= 400) throw new IOException("PC answered HTTP " + code);
            return out.toString("UTF-8");
        } finally {
            c.disconnect();
        }
    }

    // ---- asking this phone's owner -----------------------------------------------------------------------------------

    private void ask(String pc, String asker) {
        ask(pc, asker, false);
    }

    /** Once a minute at most: the page (when open) and a note with Allow / Deny. write: the PC wants to add files. */
    private void ask(String pc, String asker, boolean write) {
        ask(pc, asker, write, "all");
    }

    /** scope: what Allow gives (and the page offers first): "all" = the whole phone storage, as the phone shows it. */
    private void ask(String pc, String asker, boolean write, String scope) {
        long now = System.currentTimeMillis();
        if (now - lastAsk < 60000) return;
        lastAsk = now;
        JSONObject rec = PcProfiles.get(ctx, pc);
        String name = rec == null ? "" : rec.optString("label", rec.optString("name", ""));
        if (name.isEmpty()) name = "Your PC";
        // Allowing it allows the PC (and so what it lets its laptops see), for the time chosen.
        if (asker != null && !asker.trim().isEmpty() && !asker.trim().equals(name)) name = asker.trim() + " via " + name;
        Listener l = listener;
        if (l != null) l.onAsk(pc, name, scope);
        notifyAsk(pc, name, write, scope);
    }

    private void notifyAsk(String pc, String name, boolean write, String scope) {
        NotificationManager nm = (NotificationManager) ctx.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm == null) return;
        if (Build.VERSION.SDK_INT >= 26 && nm.getNotificationChannel(CHANNEL) == null)
            nm.createNotificationChannel(new NotificationChannel(CHANNEL, "Requests from your PC", NotificationManager.IMPORTANCE_HIGH));
        Intent open = new Intent(ctx, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 23 ? PendingIntent.FLAG_IMMUTABLE : 0);
        PendingIntent content = PendingIntent.getActivity(ctx, 0, open, flags);
        Grants.Grant had = Grants.get(ctx, pc);
        PendingIntent allow = PendingIntent.getBroadcast(ctx, 1, new Intent(ctx, PeerDecisionReceiver.class)
                .putExtra("pc", pc).putExtra("minutes", 60).putExtra("scope", "all".equals(scope) || had == null ? "all" : had.scope)
                .putExtra("write", write || (had != null && had.write)), flags);
        PendingIntent deny = PendingIntent.getBroadcast(ctx, 2, new Intent(ctx, PeerDecisionReceiver.class)
                .putExtra("pc", pc).putExtra("minutes", -1), flags);
        Notification.Builder b = Build.VERSION.SDK_INT >= 26 ? new Notification.Builder(ctx, CHANNEL) : new Notification.Builder(ctx);
        b.setSmallIcon(android.R.drawable.ic_menu_view)
                .setContentTitle(write ? Tr.t(ctx, "{0} wants to add files to this phone", name) : Tr.t(ctx, "{0} wants to see this phone's files", name))
                .setContentText(write ? Tr.t(ctx, "Allow only if you are at that PC. It can add files and folders for 1 hour.")
                        : Tr.t(ctx, "Allow only if you are at that PC. Its whole storage for 1 hour."))
                .setContentIntent(content)
                .setAutoCancel(true);
        Notification.Action.Builder allowAction = new Notification.Action.Builder(null, Tr.t(ctx, "Allow 1 hour"), allow);
        // Allowing needs the phone unlocked: someone holding a locked phone cannot open it up.
        if (Build.VERSION.SDK_INT >= 31) allowAction.setAuthenticationRequired(true);
        b.addAction(allowAction.build());
        b.addAction(new Notification.Action.Builder(null, Tr.t(ctx, "Deny"), deny).build());
        try {
            nm.notify(NOTE_ID, b.build());
        } catch (SecurityException ignored) {
            // Notifications not allowed: the page asks when the app is open.
        }
    }

    // ---- what this phone allows ----------------------------------------------------------------------------------------

    /** Per PC: what it may see (shared folders or the whole phone storage) and until when (0 = until taken back). */
    static final class Grants {
        static final class Grant {
            final String scope;
            final long until;
            /** The PC may add files and folders (fs.mkdir, fs.pull), not only look. */
            final boolean write;

            Grant(String scope, long until, boolean write) {
                this.scope = scope;
                this.until = until;
                this.write = write;
            }
        }

        private static String key(String pc) {
            return "peer_" + PcProfiles.key(pc);
        }

        static Grant get(Context c, String pc) {
            if (pc == null || pc.isEmpty()) return null;
            SharedPreferences p = c.getSharedPreferences("cloudssd", Context.MODE_PRIVATE);
            String raw = p.getString(key(pc), "");
            if (raw.isEmpty()) return null;
            try {
                JSONObject o = new JSONObject(raw);
                long until = o.optLong("until", 0);
                if (until > 0 && until < System.currentTimeMillis()) {
                    p.edit().remove(key(pc)).apply();
                    return null;
                }
                return new Grant("all".equals(o.optString("scope")) ? "all" : "shared", until, o.optBoolean("write", false));
            } catch (JSONException e) {
                return null;
            }
        }

        /** minutes 0 = until taken back. Keeps whether the PC may add files. */
        static void set(Context c, String pc, String scope, int minutes) {
            Grant had = get(c, pc);
            set(c, pc, scope, minutes, had != null && had.write);
        }

        static void set(Context c, String pc, String scope, int minutes, boolean write) {
            try {
                long until = minutes > 0 ? System.currentTimeMillis() + minutes * 60_000L : 0;
                c.getSharedPreferences("cloudssd", Context.MODE_PRIVATE).edit()
                        .putString(key(pc), new JSONObject().put("scope", "all".equals(scope) ? "all" : "shared").put("until", until)
                                .put("write", write).toString()).apply();
            } catch (JSONException ignored) {
            }
        }

        static void clear(Context c, String pc) {
            c.getSharedPreferences("cloudssd", Context.MODE_PRIVATE).edit().remove(key(pc)).apply();
        }

        static JSONObject forPage(Context c, String pc) {
            JSONObject o = new JSONObject();
            Grant g = get(c, pc);
            try {
                if (g != null) o.put("scope", g.scope).put("until", g.until).put("write", g.write);
            } catch (JSONException ignored) {
            }
            return o;
        }
    }
}
