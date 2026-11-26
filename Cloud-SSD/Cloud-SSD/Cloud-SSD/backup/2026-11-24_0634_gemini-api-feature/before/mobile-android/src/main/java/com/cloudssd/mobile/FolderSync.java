package com.cloudssd.mobile;

import android.app.job.JobInfo;
import android.app.job.JobScheduler;
import android.content.ComponentName;
import android.content.ContentResolver;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.database.Cursor;
import android.net.Uri;
import android.provider.DocumentsContract;
import android.webkit.MimeTypeMap;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;
import java.util.TreeSet;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Keeps a folder on this phone and a folder on the PC the same, both ways: a new file on one side is copied to the
 * other, a changed one replaces the other side's copy. When both sides changed the same file, the newer one keeps the
 * name and the older one is kept next to it as "name (conflict).ext" on both sides. Deleting is not passed on (v1): a
 * file deleted on one side stays on the other and is not copied back either.
 * <p>
 * Each pair remembers what both sides looked like after the last sync (size and time per file), so a change is told
 * apart from the other side being different: the PC sets its own time when a file is written to it.
 * Runs every hour on Wi-Fi in the background, or now from Settings; only while the pair's PC is the one in use.
 */
final class FolderSync {
    static final int JOB_PERIODIC = 7201;
    private static final String PREFS = "cloudssd";
    private static final String KEY = "folder_syncs";
    private static final int MAX_FILES = 3000;
    private static final int MAX_DEPTH = 8;
    private static final Object RUN_LOCK = new Object();

    /** Progress and results for the page (set by the activity while it is open). */
    interface Listener {
        void onSync(JSONObject event);
    }

    static volatile Listener listener;

    private FolderSync() {
    }

    // ---- the pairs ---------------------------------------------------------------------------------------------------

    private static SharedPreferences prefs(Context c) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    static JSONArray pairs(Context c) {
        try {
            return new JSONArray(prefs(c).getString(KEY, "[]"));
        } catch (Exception e) {
            return new JSONArray();
        }
    }

    private static void save(Context c, JSONArray all) {
        prefs(c).edit().putString(KEY, all.toString()).apply();
    }

    private static JSONObject find(JSONArray all, String id) {
        for (int i = 0; i < all.length(); i++) {
            JSONObject o = all.optJSONObject(i);
            if (o != null && id.equals(o.optString("id"))) return o;
        }
        return null;
    }

    /** A new pair: the phone folder (a tree the person picked; its permission is kept) and a folder on the PC in use. */
    static JSONObject add(Context c, String pcId, String pcFolder, String pcLabel, Uri tree, String phoneLabel) throws Exception {
        c.getContentResolver().takePersistableUriPermission(tree, Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
        JSONObject p = new JSONObject()
                .put("id", UUID.randomUUID().toString().substring(0, 12))
                .put("pcId", pcId).put("pcFolder", pcFolder).put("pcLabel", pcLabel)
                .put("tree", tree.toString()).put("phoneLabel", phoneLabel)
                .put("addedAt", System.currentTimeMillis());
        JSONArray all = pairs(c);
        all.put(p);
        save(c, all);
        // Scheduled after its first run (see MainActivity): Android may start a new periodic job at once, and two runs
        // back to back would show the second one's "nothing to do".
        return p;
    }

    static void remove(Context c, String id) {
        JSONArray all = pairs(c), keep = new JSONArray();
        String tree = null;
        for (int i = 0; i < all.length(); i++) {
            JSONObject o = all.optJSONObject(i);
            if (o == null) continue;
            if (id.equals(o.optString("id"))) tree = o.optString("tree");
            else keep.put(o);
        }
        save(c, keep);
        //noinspection ResultOfMethodCallIgnored
        stateFile(c, id).delete();
        // The folder's permission goes back when no other pair uses it.
        boolean used = false;
        for (int i = 0; i < keep.length(); i++) if (tree != null && tree.equals(keep.optJSONObject(i).optString("tree"))) used = true;
        if (tree != null && !used) {
            try {
                c.getContentResolver().releasePersistableUriPermission(Uri.parse(tree), Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
            } catch (Exception ignored) {
                // Already gone.
            }
        }
        schedule(c);
    }

    /** A PC forgotten on this phone: its synced folders stop too (the files stay where they are). */
    static void forgetPc(Context c, String pcId) {
        JSONArray all = pairs(c);
        for (int i = 0; i < all.length(); i++) {
            JSONObject p = all.optJSONObject(i);
            if (p != null && pcId.equals(p.optString("pcId"))) remove(c, p.optString("id"));
        }
    }

    /** Every hour on a network without data charges while there are pairs; nothing when there are none. */
    static void schedule(Context c) {
        JobScheduler jobs = (JobScheduler) c.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        if (jobs == null) return;
        if (pairs(c).length() == 0) {
            jobs.cancel(JOB_PERIODIC);
            return;
        }
        jobs.schedule(new JobInfo.Builder(JOB_PERIODIC, new ComponentName(c, FolderSyncService.class))
                .setRequiredNetworkType(JobInfo.NETWORK_TYPE_UNMETERED)
                .setPeriodic(60 * 60 * 1000L)
                .setPersisted(true)
                .build());
    }

    // ---- running -------------------------------------------------------------------------------------------------------

    /** A pair synced this recently (now, from Settings) is left for the next hourly run. */
    private static final long RECENT_MS = 15 * 60 * 1000L;

    /** The hourly run: every pair of the PC in use that was not synced in the last 15 minutes (one run at a time). */
    static void runAll(Context c, AtomicBoolean stop) {
        String active = PcProfiles.activeId(c);
        JSONArray all = pairs(c);
        long now = System.currentTimeMillis();
        for (int i = 0; i < all.length() && !stop.get(); i++) {
            JSONObject p = all.optJSONObject(i);
            if (p == null || !p.optString("pcId").equals(active)) continue;
            JSONObject last = p.optJSONObject("last");
            if (last != null && last.optBoolean("ok") && now - last.optLong("at") < RECENT_MS) continue;
            run(c, p.optString("id"), stop);
        }
    }

    /** Syncs one pair now and returns its result (also saved with the pair and sent to the page). */
    static JSONObject run(Context c, String id, AtomicBoolean stop) {
        synchronized (RUN_LOCK) {
            JSONObject result = new JSONObject();
            int[] n = new int[4]; // up, down, conflicts, failed
            String message = null;
            JSONObject pair = find(pairs(c), id);
            try {
                if (pair == null) throw new IOException("No such synced folder.");
                if (!pair.optString("pcId").equals(PcProfiles.activeId(c))) throw new IOException("Its PC is not the one in use.");
                emit(id, "running", null);
                new Run(c, pair, stop, n).go();
            } catch (Exception e) {
                message = e.getMessage() == null ? e.toString() : e.getMessage();
            }
            try {
                result.put("id", id).put("at", System.currentTimeMillis()).put("up", n[0]).put("down", n[1])
                        .put("conflicts", n[2]).put("failed", n[3]).put("ok", message == null);
                if (message != null) result.put("error", message);
                JSONArray all = pairs(c);
                JSONObject p = find(all, id);
                if (p != null) {
                    p.put("last", result);
                    save(c, all);
                }
            } catch (Exception ignored) {
                // Only the note of the result is missing.
            }
            emit(id, "done", result);
            return result;
        }
    }

    private static void emit(String id, String state, JSONObject result) {
        Listener l = listener;
        if (l == null) return;
        try {
            l.onSync(new JSONObject().put("id", id).put("state", state).put("result", result == null ? JSONObject.NULL : result));
        } catch (Exception ignored) {
            // The page is gone.
        }
    }

    private static File stateFile(Context c, String id) {
        return new File(c.getFilesDir(), "sync-" + id + ".json");
    }

    /** "photo.jpg" → "photo (conflict).jpg" (then "(conflict 2)" …) — a name not taken in <paramref name="taken"/>. */
    static String conflictName(String name, java.util.Set<String> taken) {
        int dot = name.lastIndexOf('.');
        String stem = dot > 0 ? name.substring(0, dot) : name, ext = dot > 0 ? name.substring(dot) : "";
        for (int i = 1; ; i++) {
            String n = stem + " (conflict" + (i > 1 ? " " + i : "") + ")" + ext;
            if (!taken.contains(n)) return n;
        }
    }

    /** One run of one pair. */
    private static final class Run {
        private final Context c;
        private final ContentResolver r;
        private final JSONObject pair;
        private final AtomicBoolean stop;
        private final int[] n;
        private final Uri tree;
        private final PcBackend pc;
        private final Map<String, Phone> phone = new HashMap<>();
        private final Map<String, String> phoneDirs = new HashMap<>(); // relative folder → its document id
        private final Map<String, CloudBackend.DocInfo> pcFiles = new HashMap<>();
        private JSONObject state;

        private static final class Phone {
            String docId;
            long size, modified;
        }

        Run(Context c, JSONObject pair, AtomicBoolean stop, int[] n) {
            this.c = c;
            this.r = c.getContentResolver();
            this.pair = pair;
            this.stop = stop;
            this.n = n;
            this.tree = Uri.parse(pair.optString("tree"));
            this.pc = new PcBackend(c);
        }

        void go() throws Exception {
            if (!pc.isConfigured()) throw new IOException("Not paired with a PC.");
            state = readState();
            String rootDoc = DocumentsContract.getTreeDocumentId(tree);
            phoneDirs.put("", rootDoc);
            listPhone(rootDoc, "", 0);
            listPc(pair.optString("pcFolder"), "", 0);
            TreeSet<String> all = new TreeSet<>(phone.keySet());
            all.addAll(pcFiles.keySet());
            for (String rel : all) {
                if (stop.get()) break;
                try {
                    one(rel);
                } catch (CloudBackend.BackendException e) {
                    // The PC went away: stop here, the next run continues. Refused or missing: that file only.
                    if (e.status == CloudBackend.BackendException.UNREACHABLE || e.status == CloudBackend.BackendException.NOT_PAIRED) throw e;
                    n[3]++;
                } catch (Exception e) {
                    n[3]++;
                }
            }
            writeState();
        }

        private void one(String rel) throws Exception {
            Phone f = phone.get(rel);
            CloudBackend.DocInfo p = pcFiles.get(rel);
            JSONObject st = state.optJSONObject(rel);
            boolean phoneChanged = f != null && (st == null || f.size != st.optLong("ps", -1) || f.modified != st.optLong("pm", -1));
            boolean pcChanged = p != null && (st == null || p.size != st.optLong("cs", -1) || p.modified != st.optLong("cm", -1));
            if (f != null && p != null) {
                if (st == null && f.size == p.size) { remember(rel, f, p); return; }   // already the same (first run)
                if (!phoneChanged && !pcChanged) return;
                if (phoneChanged && !pcChanged) { remember(rel, f, upload(rel, f)); n[0]++; return; }
                if (!phoneChanged) { remember(rel, download(rel, p, f.docId), p); n[1]++; return; }
                conflict(rel, f, p);
                n[2]++;
                return;
            }
            // On one side only. Deleted on the other since the last sync (and not changed since): left alone.
            if (f != null) {
                if (st != null && !phoneChanged) return;
                remember(rel, f, upload(rel, f));
                n[0]++;
            } else if (p != null) {
                if (st != null && !pcChanged) return;
                remember(rel, download(rel, p, null), p);
                n[1]++;
            }
        }

        /** Both changed: the newer keeps the name; the older is kept beside it as "(conflict)" on both sides. */
        private void conflict(String rel, Phone f, CloudBackend.DocInfo p) throws Exception {
            String dir = rel.contains("/") ? rel.substring(0, rel.lastIndexOf('/') + 1) : "";
            String name = rel.substring(dir.length());
            java.util.Set<String> taken = new java.util.HashSet<>();
            for (String k : phone.keySet()) if (k.startsWith(dir)) taken.add(k.substring(dir.length()));
            for (String k : pcFiles.keySet()) if (k.startsWith(dir)) taken.add(k.substring(dir.length()));
            String other = conflictName(name, taken);
            String otherRel = dir + other;
            if (f.modified >= p.modified) {
                // The PC's copy moves aside, the phone's takes the name there; the phone gets the PC's copy as "(conflict)".
                CloudBackend.DocInfo aside = pc.rename(p.id, other);
                remember(rel, f, upload(rel, f));
                Phone got = download(otherRel, aside, null);
                remember(otherRel, got, aside);
            } else {
                // The phone's copy moves aside, the PC's takes the name here; the PC gets the phone's copy as "(conflict)".
                Uri moved = DocumentsContract.renameDocument(r, DocumentsContract.buildDocumentUriUsingTree(tree, f.docId), other);
                Phone aside = new Phone();
                aside.docId = moved == null ? f.docId : DocumentsContract.getDocumentId(moved);
                readPhone(aside);
                remember(otherRel, aside, upload(otherRel, aside));
                remember(rel, download(rel, p, null), p);
            }
        }

        private CloudBackend.DocInfo upload(String rel, Phone f) throws Exception {
            try (InputStream in = r.openInputStream(DocumentsContract.buildDocumentUriUsingTree(tree, f.docId))) {
                if (in == null) throw new IOException("Cannot read " + rel);
                CloudBackend.DocInfo d = pc.write(pcId(rel), in, true);
                // The answer may lack the time: ask for it, so the next run sees no change.
                return d.modified > 0 ? d : pc.stat(pcId(rel));
            }
        }

        /** The PC's file into the phone folder (a new file, or over <paramref name="docId"/>). */
        private Phone download(String rel, CloudBackend.DocInfo p, String docId) throws Exception {
            Uri target = docId != null ? DocumentsContract.buildDocumentUriUsingTree(tree, docId) : create(rel);
            try (InputStream in = pc.openRead(p.id, 0); OutputStream out = r.openOutputStream(target, "wt")) {
                if (out == null) throw new IOException("Cannot write " + rel);
                byte[] buf = new byte[64 * 1024];
                for (int k; (k = in.read(buf)) >= 0; ) {
                    if (stop.get()) throw new IOException("Stopped");
                    out.write(buf, 0, k);
                }
            }
            Phone got = new Phone();
            got.docId = DocumentsContract.getDocumentId(target);
            readPhone(got);
            phone.put(rel, got);
            return got;
        }

        /** A new file in the phone folder, with the folders on the way made as needed. */
        private Uri create(String rel) throws Exception {
            String[] parts = rel.split("/");
            String path = "", parent = phoneDirs.get("");
            for (int i = 0; i < parts.length - 1; i++) {
                path = path.isEmpty() ? parts[i] : path + "/" + parts[i];
                String known = phoneDirs.get(path);
                if (known == null) {
                    Uri made = DocumentsContract.createDocument(r, DocumentsContract.buildDocumentUriUsingTree(tree, parent),
                            DocumentsContract.Document.MIME_TYPE_DIR, parts[i]);
                    if (made == null) throw new IOException("Cannot make the folder " + path);
                    known = DocumentsContract.getDocumentId(made);
                    phoneDirs.put(path, known);
                }
                parent = known;
            }
            String name = parts[parts.length - 1];
            int dot = name.lastIndexOf('.');
            String mime = dot > 0 ? MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substring(dot + 1).toLowerCase(java.util.Locale.ROOT)) : null;
            Uri made = DocumentsContract.createDocument(r, DocumentsContract.buildDocumentUriUsingTree(tree, parent),
                    mime == null ? "application/octet-stream" : mime, name);
            if (made == null) throw new IOException("Cannot make " + rel);
            return made;
        }

        private void readPhone(Phone f) {
            try (Cursor cur = r.query(DocumentsContract.buildDocumentUriUsingTree(tree, f.docId),
                    new String[]{DocumentsContract.Document.COLUMN_SIZE, DocumentsContract.Document.COLUMN_LAST_MODIFIED}, null, null, null)) {
                if (cur != null && cur.moveToFirst()) {
                    f.size = cur.getLong(0);
                    f.modified = cur.getLong(1);
                }
            }
        }

        private String pcId(String rel) {
            String base = pair.optString("pcFolder");
            return base.endsWith(":") || base.endsWith("/") ? base + rel : base + "/" + rel;
        }

        private void listPhone(String docId, String prefix, int depth) {
            if (depth > MAX_DEPTH || phone.size() > MAX_FILES) return;
            Uri kids = DocumentsContract.buildChildDocumentsUriUsingTree(tree, docId);
            try (Cursor cur = r.query(kids, new String[]{DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                    DocumentsContract.Document.COLUMN_MIME_TYPE, DocumentsContract.Document.COLUMN_SIZE, DocumentsContract.Document.COLUMN_LAST_MODIFIED}, null, null, null)) {
                while (cur != null && cur.moveToNext()) {
                    String name = cur.getString(1);
                    if (name == null || name.startsWith(".")) continue;   // hidden: thumbnails, pending downloads
                    String rel = prefix + name;
                    if (DocumentsContract.Document.MIME_TYPE_DIR.equals(cur.getString(2))) {
                        phoneDirs.put(rel, cur.getString(0));
                        listPhone(cur.getString(0), rel + "/", depth + 1);
                    } else {
                        Phone f = new Phone();
                        f.docId = cur.getString(0);
                        f.size = cur.getLong(3);
                        f.modified = cur.getLong(4);
                        phone.put(rel, f);
                    }
                }
            }
        }

        private void listPc(String folderId, String prefix, int depth) throws IOException {
            if (depth > MAX_DEPTH || pcFiles.size() > MAX_FILES) return;
            for (CloudBackend.DocInfo d : pc.list(folderId)) {
                if (d.name == null || d.name.startsWith(".")) continue;
                if (d.dir) listPc(d.id, prefix + d.name + "/", depth + 1);
                else pcFiles.put(prefix + d.name, d);
            }
        }

        private void remember(String rel, Phone f, CloudBackend.DocInfo p) throws Exception {
            state.put(rel, new JSONObject().put("ps", f.size).put("pm", f.modified).put("cs", p.size).put("cm", p.modified));
        }

        private JSONObject readState() {
            File file = stateFile(c, pair.optString("id"));
            try {
                if (!file.exists()) return new JSONObject();
                byte[] b = new byte[(int) file.length()];
                try (InputStream in = new java.io.FileInputStream(file)) {
                    int off = 0;
                    for (int k; off < b.length && (k = in.read(b, off, b.length - off)) > 0; ) off += k;
                }
                return new JSONObject(new String(b, StandardCharsets.UTF_8));
            } catch (Exception e) {
                return new JSONObject();
            }
        }

        private void writeState() throws IOException {
            File file = stateFile(c, pair.optString("id"));
            File tmp = new File(file.getPath() + ".tmp");
            try (FileOutputStream out = new FileOutputStream(tmp)) {
                out.write(state.toString().getBytes(StandardCharsets.UTF_8));
            }
            if (!tmp.renameTo(file)) throw new IOException("Could not save the sync state");
        }
    }
}
