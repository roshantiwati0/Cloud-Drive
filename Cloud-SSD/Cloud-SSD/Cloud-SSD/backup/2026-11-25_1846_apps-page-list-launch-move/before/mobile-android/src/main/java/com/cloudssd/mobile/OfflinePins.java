package com.cloudssd.mobile;

import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.StatFs;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import com.cloudssd.mobile.CloudBackend.BackendException;
import com.cloudssd.mobile.CloudBackend.DocInfo;

/**
 * "Offline rakho" on the phone: folders and files of a PC kept on this phone, so they open while the PC is away.
 * What is kept is in offline.db; the copies are in the app's own storage (files/offline/&lt;pc&gt;/), out of the gallery
 * and of other apps. They are fetched in the background (resuming), on Wi-Fi only unless mobile data is allowed,
 * always leaving 500 MB free; files deleted on the PC go here too. Only the PC in use is synced: the others keep
 * their copies and catch up when they are chosen again.
 */
final class OfflinePins {
    interface Listener {
        void onEvent(JSONObject ev);
    }

    private static final String TAG = "CloudSSD.Offline";
    static final long RESERVE = 500L * 1024 * 1024;
    static final String MOBILE_DATA = "offline_mobile_data";
    private static final int MAX_FILES = 5000;
    private static final int MAX_DEPTH = 24;
    private static final long EMIT_MS = 400;

    private static OfflinePins instance;

    static synchronized OfflinePins get(Context c) {
        if (instance == null) instance = new OfflinePins(c.getApplicationContext());
        return instance;
    }

    private final Context ctx;
    private final Db db;
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final Object lock = new Object();
    private boolean running, again;
    private volatile Listener listener;
    private long lastEmit;

    private OfflinePins(Context c) {
        ctx = c;
        db = new Db(c);
    }

    void setListener(Listener l) {
        listener = l;
    }

    boolean busy() {
        synchronized (lock) {
            return running;
        }
    }

    // ---- where the copies are --------------------------------------------------------------------------------------

    static File dirOf(Context c, String pc) {
        return new File(new File(c.getFilesDir(), "offline"), PcProfiles.key(pc));
    }

    private File fileOf(String pc, String id) {
        return new File(dirOf(ctx, pc), sha1(id) + extOf(id));
    }

    /** True when the pin (a folder id, a drive "x:", or a file id) includes this item. */
    static boolean covers(String pin, String id) {
        if (pin.equals(id)) return true;
        return id.startsWith(pin.endsWith(":") ? pin : pin + "/");
    }

    // ---- what is kept ------------------------------------------------------------------------------------------------

    private static final class Pin {
        String id, name;
        boolean dir;
    }

    private List<Pin> pinsOf(String pc) {
        List<Pin> out = new ArrayList<>();
        try (Cursor c = db.getReadableDatabase().query("pins", new String[]{"id", "name", "dir"}, "pc=?", new String[]{pc}, null, null, "added")) {
            while (c.moveToNext()) {
                Pin p = new Pin();
                p.id = c.getString(0);
                p.name = c.getString(1);
                p.dir = c.getInt(2) != 0;
                out.add(p);
            }
        }
        return out;
    }

    private boolean stillPinned(String pc, String id) {
        try (Cursor c = db.getReadableDatabase().query("pins", new String[]{"id"}, "pc=? AND id=?", new String[]{pc, id}, null, null, null)) {
            return c.moveToFirst();
        }
    }

    /** Keep an item (a folder with everything in it, or one file). Returns "" or why it cannot be kept. */
    String pin(String pc, String id, String name, boolean dir) {
        if (pc == null || pc.isEmpty() || id == null || id.indexOf(':') <= 0) return "This item cannot be kept offline.";
        ContentValues v = new ContentValues();
        v.put("pc", pc);
        v.put("id", id);
        v.put("name", name == null ? id : name);
        v.put("dir", dir ? 1 : 0);
        v.put("added", System.currentTimeMillis());
        v.put("state", "waiting");
        db.getWritableDatabase().insertWithOnConflict("pins", null, v, SQLiteDatabase.CONFLICT_IGNORE);
        changed(pc);
        kick();
        return "";
    }

    /** Stop keeping it: its copies go, except those another pin (a folder above or inside) still keeps. */
    void unpin(String pc, String id) {
        SQLiteDatabase w = db.getWritableDatabase();
        w.delete("pins", "pc=? AND id=?", new String[]{pc, id});
        List<Pin> rest = pinsOf(pc);
        List<String[]> rows = new ArrayList<>();
        try (Cursor c = w.query("files", new String[]{"id", "local"}, "pc=? AND pin=?", new String[]{pc, id}, null, null, null)) {
            while (c.moveToNext()) rows.add(new String[]{c.getString(0), c.getString(1)});
        }
        for (String[] r : rows) {
            String other = null;
            for (Pin p : rest) if (covers(p.id, r[0])) { other = p.id; break; }
            if (other != null) {
                ContentValues v = new ContentValues();
                v.put("pin", other);
                w.update("files", v, "pc=? AND id=?", new String[]{pc, r[0]});
            } else {
                deleteCopy(pc, r[0]);
                w.delete("files", "pc=? AND id=?", new String[]{pc, r[0]});
            }
        }
        changed(pc);
    }

    private void deleteCopy(String pc, String id) {
        File f = fileOf(pc, id);
        //noinspection ResultOfMethodCallIgnored
        f.delete();
        //noinspection ResultOfMethodCallIgnored
        new File(f.getPath() + ".part").delete();
    }

    /** A PC was forgotten on this phone: everything kept from it goes. */
    void forgetPc(String pc) {
        if (pc == null || pc.isEmpty()) return;
        SQLiteDatabase w = db.getWritableDatabase();
        w.delete("pins", "pc=?", new String[]{pc});
        w.delete("files", "pc=?", new String[]{pc});
        deleteTree(dirOf(ctx, pc));
    }

    static void deleteTree(File f) {
        File[] kids = f.listFiles();
        if (kids != null) for (File k : kids) deleteTree(k);
        //noinspection ResultOfMethodCallIgnored
        f.delete();
    }

    /** The kept copy of a file, or null. */
    File local(String pc, String id) {
        if (pc == null || id == null) return null;
        try (Cursor c = db.getReadableDatabase().query("files", new String[]{"local", "size"}, "pc=? AND id=?", new String[]{pc, id}, null, null, null)) {
            if (!c.moveToFirst() || c.isNull(0) || c.getString(0).isEmpty()) return null;
            File f = new File(dirOf(ctx, pc), c.getString(0));
            return f.isFile() && (c.getLong(1) < 0 || f.length() == c.getLong(1)) ? f : null;
        }
    }

    boolean mobileDataAllowed() {
        return ctx.getSharedPreferences("cloudssd", Context.MODE_PRIVATE).getBoolean(MOBILE_DATA, false);
    }

    void setMobileData(boolean on) {
        ctx.getSharedPreferences("cloudssd", Context.MODE_PRIVATE).edit().putBoolean(MOBILE_DATA, on).apply();
        if (on) kick();
    }

    /**
     * For the page: {pins: [{id, name, dir, state, error, files, bytes, doneFiles, doneBytes, synced}],
     * files: {id: [size, modified]} (copies that are here), busy, mobileData, used (bytes on this phone)}.
     */
    JSONObject state(String pc) throws JSONException {
        JSONObject o = new JSONObject();
        JSONArray pins = new JSONArray();
        SQLiteDatabase r = db.getReadableDatabase();
        try (Cursor c = r.query("pins", new String[]{"id", "name", "dir", "state", "error", "files", "bytes", "done_files", "done_bytes", "synced"},
                "pc=?", new String[]{pc == null ? "" : pc}, null, null, "added")) {
            while (c.moveToNext()) {
                pins.put(new JSONObject().put("id", c.getString(0)).put("name", c.getString(1)).put("dir", c.getInt(2) != 0)
                        .put("state", c.getString(3)).put("error", c.isNull(4) ? "" : c.getString(4))
                        .put("files", c.getLong(5)).put("bytes", c.getLong(6)).put("doneFiles", c.getLong(7)).put("doneBytes", c.getLong(8))
                        .put("synced", c.getLong(9)));
            }
        }
        JSONObject files = new JSONObject();
        long used = 0;
        try (Cursor c = r.query("files", new String[]{"id", "size", "modified"}, "pc=? AND local<>''", new String[]{pc == null ? "" : pc}, null, null, null)) {
            while (c.moveToNext()) {
                files.put(c.getString(0), new JSONArray().put(c.getLong(1)).put(c.getLong(2)));
                used += Math.max(0, c.getLong(1));
            }
        }
        return o.put("pins", pins).put("files", files).put("busy", busy()).put("mobileData", mobileDataAllowed()).put("used", used);
    }

    // ---- keeping them up to date ---------------------------------------------------------------------------------

    /** Starts a round in the background (or one more after the running one). */
    void kick() {
        synchronized (lock) {
            again = true;
            if (running) return;
            running = true;
        }
        TransferService.start(ctx);
        worker.execute(this::loop);
    }

    private void loop() {
        while (true) {
            synchronized (lock) {
                if (!again) {
                    running = false;
                    break;
                }
                again = false;
            }
            try {
                runAll();
            } catch (Exception e) {
                Log.w(TAG, "offline round failed", e);
            }
        }
        TransferService.stopIfIdle(ctx);
        String pc = PcProfiles.activeId(ctx);
        if (pc != null) changed(pc);
    }

    private boolean networkAllowed() {
        if (mobileDataAllowed()) return true;
        ConnectivityManager cm = (ConnectivityManager) ctx.getSystemService(Context.CONNECTIVITY_SERVICE);
        if (cm == null) return true;
        Network n = cm.getActiveNetwork();
        NetworkCapabilities nc = n == null ? null : cm.getNetworkCapabilities(n);
        // No network at all: the PC may still be there over a cable (USB tethering, adb), so try.
        return nc == null || nc.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_METERED);
    }

    private void runAll() {
        String pc = PcProfiles.activeId(ctx);
        if (pc == null || pc.isEmpty()) return;
        PcBackend b = new PcBackend(ctx);
        if (!b.isConfigured()) return;
        for (Pin p : pinsOf(pc)) {
            if (!pc.equals(PcProfiles.activeId(ctx))) return;           // another PC was chosen meanwhile
            if (!networkAllowed()) {
                setState(pc, p.id, "wifi", null);
                continue;
            }
            try {
                syncPin(b, pc, p);
            } catch (BackendException e) {
                if (e.status == BackendException.UNREACHABLE || e.status == BackendException.NOT_PAIRED) {
                    // The PC is away: everything waits for it.
                    for (Pin q : pinsOf(pc)) setStateIfNotDone(pc, q.id, "waiting");
                    return;
                }
                setState(pc, p.id, "error", e.getMessage());
            } catch (IOException e) {
                setState(pc, p.id, "error", e.getMessage() == null ? "Could not keep it offline." : e.getMessage());
            }
        }
    }

    private boolean current(String pc, DocInfo d) {
        try (Cursor c = db.getReadableDatabase().query("files", new String[]{"local", "size", "modified"}, "pc=? AND id=?", new String[]{pc, d.id}, null, null, null)) {
            if (!c.moveToFirst() || c.getString(0) == null || c.getString(0).isEmpty()) return false;
            if (c.getLong(1) != d.size || c.getLong(2) != d.modified) return false;
            File f = new File(dirOf(ctx, pc), c.getString(0));
            return f.isFile() && f.length() == d.size;
        }
    }

    private void syncPin(PcBackend b, String pc, Pin p) throws IOException {
        setState(pc, p.id, "syncing", null);
        // 1. What is in it now.
        List<DocInfo> files = new ArrayList<>();
        boolean skipped = false;
        if (p.dir) {
            ArrayDeque<Object[]> queue = new ArrayDeque<>();
            queue.add(new Object[]{p.id, 0});
            while (!queue.isEmpty()) {
                Object[] next = queue.poll();
                int depth = (Integer) next[1];
                List<DocInfo> kids;
                try {
                    kids = b.list((String) next[0]);
                } catch (BackendException e) {
                    // A folder inside it that Windows lets nobody read (on the PC's own disk), or that went away
                    // meanwhile: it is left out and the rest is kept. The kept folder itself must answer.
                    if (depth == 0 || (e.status != 403 && e.status != 404)) throw e;
                    skipped = true;
                    continue;
                }
                for (DocInfo d : kids) {
                    if (d.dir) {
                        if (depth + 1 < MAX_DEPTH) queue.add(new Object[]{d.id, depth + 1});
                    } else if (!d.exported && d.size >= 0) {
                        files.add(d);   // Google Docs exports have no size and change on every read: not kept
                    }
                }
                if (files.size() > MAX_FILES) throw new IOException("More than " + MAX_FILES + " files: keep a smaller folder offline.");
                if (!stillPinned(pc, p.id)) return;
            }
        } else {
            DocInfo d = b.stat(p.id);
            if (!d.dir) files.add(d);
        }

        // 2. Room for what is missing, always leaving some free.
        long total = 0, missing = 0, doneBytes = 0;
        int doneFiles = 0;
        for (DocInfo d : files) {
            total += d.size;
            if (current(pc, d)) {
                doneFiles++;
                doneBytes += d.size;
            } else missing += d.size;
        }
        progress(pc, p.id, files.size(), total, doneFiles, doneBytes, true);
        long free = new StatFs(ctx.getFilesDir().getPath()).getAvailableBytes();
        if (missing > 0 && free - missing < RESERVE) {
            throw new IOException("Not enough room on this phone: " + mb(missing) + " more is needed, and 500 MB is always left free.");
        }

        // 3. Fetch what is new or changed.
        for (DocInfo d : files) {
            if (!stillPinned(pc, p.id) || !pc.equals(PcProfiles.activeId(ctx))) return;
            if (!networkAllowed()) {
                setState(pc, p.id, "wifi", null);
                return;
            }
            if (current(pc, d)) {
                setPinOf(pc, d.id, p.id);
                continue;
            }
            download(b, pc, p.id, d, files.size(), total, doneFiles, doneBytes);
            doneFiles++;
            doneBytes += d.size;
            progress(pc, p.id, files.size(), total, doneFiles, doneBytes, false);
        }

        // 4. Files gone from the PC go here too (not after a folder inside could not be read: its copies stay).
        if (p.dir && !skipped) {
            Set<String> keep = new HashSet<>();
            for (DocInfo d : files) keep.add(d.id);
            List<String> gone = new ArrayList<>();
            try (Cursor c = db.getReadableDatabase().query("files", new String[]{"id"}, "pc=? AND pin=?", new String[]{pc, p.id}, null, null, null)) {
                while (c.moveToNext()) if (!keep.contains(c.getString(0))) gone.add(c.getString(0));
            }
            for (String id : gone) {
                deleteCopy(pc, id);
                db.getWritableDatabase().delete("files", "pc=? AND id=?", new String[]{pc, id});
            }
        }
        ContentValues v = new ContentValues();
        v.put("state", "done");
        v.putNull("error");
        v.put("files", files.size());
        v.put("bytes", total);
        v.put("done_files", files.size());
        v.put("done_bytes", total);
        v.put("synced", System.currentTimeMillis());
        db.getWritableDatabase().update("pins", v, "pc=? AND id=?", new String[]{pc, p.id});
        changed(pc);
    }

    private void setPinOf(String pc, String id, String pin) {
        ContentValues v = new ContentValues();
        v.put("pin", pin);
        db.getWritableDatabase().update("files", v, "pc=? AND id=? AND pin<>?", new String[]{pc, id, pin});
    }

    /** One file, into a ".part" that is continued next time when the same version of the file is fetched again. */
    private void download(PcBackend b, String pc, String pin, DocInfo d, int files, long total, int doneFiles, long doneBytes) throws IOException {
        File f = fileOf(pc, d.id);
        File part = new File(f.getPath() + ".part");
        //noinspection ResultOfMethodCallIgnored
        f.getParentFile().mkdirs();
        long offset = 0;
        try (Cursor c = db.getReadableDatabase().query("files", new String[]{"psize", "pmod"}, "pc=? AND id=?", new String[]{pc, d.id}, null, null, null)) {
            if (part.isFile() && c.moveToFirst() && c.getLong(0) == d.size && c.getLong(1) == d.modified && part.length() < d.size) offset = part.length();
        }
        if (offset == 0) //noinspection ResultOfMethodCallIgnored
            part.delete();
        SQLiteDatabase w = db.getWritableDatabase();
        ContentValues v = new ContentValues();
        v.put("pc", pc);
        v.put("id", d.id);
        v.put("pin", pin);
        v.put("psize", d.size);
        v.put("pmod", d.modified);
        if (w.update("files", v, "pc=? AND id=?", new String[]{pc, d.id}) == 0) {
            v.put("local", "");
            v.put("size", -1);
            v.put("modified", 0);
            w.insert("files", null, v);
        }
        long have = offset;
        try (InputStream in = b.openRead(d.id, offset); OutputStream out = new FileOutputStream(part, offset > 0)) {
            byte[] buf = new byte[64 * 1024];
            int n;
            while ((n = in.read(buf)) > 0) {
                out.write(buf, 0, n);
                have += n;
                progress(pc, pin, files, total, doneFiles, doneBytes + have, false);
            }
        }
        if (part.length() != d.size) {
            if (part.length() > d.size) //noinspection ResultOfMethodCallIgnored
                part.delete();
            throw new BackendException(BackendException.UNREACHABLE, d.name + " was cut off; it continues next time.");
        }
        //noinspection ResultOfMethodCallIgnored
        f.delete();
        if (!part.renameTo(f)) throw new IOException("Could not save " + d.name + " on this phone.");
        ContentValues done = new ContentValues();
        done.put("local", f.getName());
        done.put("size", d.size);
        done.put("modified", d.modified);
        w.update("files", done, "pc=? AND id=?", new String[]{pc, d.id});
    }

    // ---- telling the page ------------------------------------------------------------------------------------------

    private void setState(String pc, String id, String state, String error) {
        ContentValues v = new ContentValues();
        v.put("state", state);
        if (error == null) v.putNull("error");
        else v.put("error", error);
        db.getWritableDatabase().update("pins", v, "pc=? AND id=?", new String[]{pc, id});
        changed(pc);
    }

    private void setStateIfNotDone(String pc, String id, String state) {
        ContentValues v = new ContentValues();
        v.put("state", state);
        db.getWritableDatabase().update("pins", v, "pc=? AND id=? AND state<>'done'", new String[]{pc, id});
        changed(pc);
    }

    private void progress(String pc, String id, int files, long total, int doneFiles, long doneBytes, boolean save) {
        if (save) {
            ContentValues v = new ContentValues();
            v.put("files", files);
            v.put("bytes", total);
            v.put("done_files", doneFiles);
            v.put("done_bytes", doneBytes);
            db.getWritableDatabase().update("pins", v, "pc=? AND id=?", new String[]{pc, id});
        }
        long now = System.currentTimeMillis();
        if (!save && now - lastEmit < EMIT_MS) return;
        lastEmit = now;
        try {
            send(new JSONObject().put("type", "progress").put("pc", pc).put("id", id).put("files", files).put("bytes", total)
                    .put("doneFiles", doneFiles).put("doneBytes", doneBytes));
        } catch (JSONException ignored) {
        }
    }

    private void changed(String pc) {
        try {
            send(new JSONObject().put("type", "pins").put("pc", pc).put("busy", busy()));
        } catch (JSONException ignored) {
        }
    }

    private void send(JSONObject ev) {
        Listener l = listener;
        if (l != null) l.onEvent(ev);
    }

    // ---- small helpers -------------------------------------------------------------------------------------------------

    private static String mb(long bytes) {
        return String.format(Locale.ROOT, "%.0f MB", Math.max(1, bytes / (1024.0 * 1024.0)));
    }

    static String sha1(String s) {
        try {
            byte[] h = MessageDigest.getInstance("SHA-1").digest(s.getBytes(StandardCharsets.UTF_8));
            StringBuilder sb = new StringBuilder();
            for (byte x : h) sb.append(String.format(Locale.ROOT, "%02x", x));
            return sb.toString();
        } catch (Exception e) {
            return Integer.toHexString(s.hashCode());
        }
    }

    /** ".jpg" for "cloudssd:Photos/a.JPG" (so other apps see the type), "" when it is odd. */
    static String extOf(String id) {
        int slash = Math.max(id.lastIndexOf('/'), id.indexOf(':'));
        String name = id.substring(slash + 1);
        int dot = name.lastIndexOf('.');
        if (dot <= 0) return "";
        String ext = name.substring(dot + 1).toLowerCase(Locale.ROOT);
        return ext.matches("[a-z0-9]{1,8}") ? "." + ext : "";
    }

    private static final class Db extends SQLiteOpenHelper {
        Db(Context c) {
            super(c, "offline.db", null, 1);
        }

        @Override
        public void onCreate(SQLiteDatabase db) {
            db.execSQL("CREATE TABLE pins (pc TEXT NOT NULL, id TEXT NOT NULL, name TEXT, dir INTEGER NOT NULL DEFAULT 1, added INTEGER, "
                    + "state TEXT, error TEXT, files INTEGER NOT NULL DEFAULT 0, bytes INTEGER NOT NULL DEFAULT 0, "
                    + "done_files INTEGER NOT NULL DEFAULT 0, done_bytes INTEGER NOT NULL DEFAULT 0, synced INTEGER NOT NULL DEFAULT 0, "
                    + "PRIMARY KEY (pc, id))");
            db.execSQL("CREATE TABLE files (pc TEXT NOT NULL, id TEXT NOT NULL, pin TEXT NOT NULL, size INTEGER NOT NULL DEFAULT -1, "
                    + "modified INTEGER NOT NULL DEFAULT 0, local TEXT NOT NULL DEFAULT '', psize INTEGER NOT NULL DEFAULT -1, "
                    + "pmod INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (pc, id))");
            db.execSQL("CREATE INDEX files_pin ON files (pc, pin)");
        }

        @Override
        public void onUpgrade(SQLiteDatabase db, int oldVersion, int newVersion) {
        }
    }
}
