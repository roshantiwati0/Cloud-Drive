package com.cloudssd.mobile;

import android.content.Context;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Comparator;
import java.util.List;

import com.cloudssd.mobile.CloudBackend.DocInfo;

/**
 * The last listing of each folder the Files app showed, per PC, kept on the phone: when the PC is away the Files app
 * shows these (with when they were saved) instead of an empty folder. At most {@link #MAX_FOLDERS} folders per PC;
 * the least recently saved go first.
 */
final class ListingCache {
    private static final int MAX_FOLDERS = 2000;
    private static int writes;

    private ListingCache() {
    }

    static final class Saved {
        final List<DocInfo> items;
        final long savedAt;

        Saved(List<DocInfo> items, long savedAt) {
            this.items = items;
            this.savedAt = savedAt;
        }
    }

    private static File dirOf(Context c, String pc) {
        return new File(new File(c.getFilesDir(), "listings"), PcProfiles.key(pc == null || pc.isEmpty() ? "none" : pc));
    }

    private static File fileOf(Context c, String pc, String folderId) {
        return new File(dirOf(c, pc), OfflinePins.sha1(folderId) + ".json");
    }

    /** A PC was forgotten on this phone: its saved lists go. */
    static void forgetPc(Context c, String pc) {
        if (pc == null || pc.isEmpty()) return;
        OfflinePins.deleteTree(dirOf(c, pc));
    }

    static void put(Context c, String pc, String folderId, List<DocInfo> list) {
        try {
            JSONArray items = new JSONArray();
            for (DocInfo d : list) {
                items.put(new JSONObject().put("id", d.id).put("name", d.name).put("dir", d.dir).put("size", d.size)
                        .put("modified", d.modified).put("canEdit", d.canEdit).put("canCreate", d.canCreate).put("exported", d.exported));
            }
            byte[] data = new JSONObject().put("folder", folderId).put("savedAt", System.currentTimeMillis()).put("items", items)
                    .toString().getBytes(StandardCharsets.UTF_8);
            File f = fileOf(c, pc, folderId);
            //noinspection ResultOfMethodCallIgnored
            f.getParentFile().mkdirs();
            File tmp = new File(f.getPath() + "." + Thread.currentThread().getId() + ".tmp");
            try (OutputStream out = new FileOutputStream(tmp)) {
                out.write(data);
            }
            if (!tmp.renameTo(f)) {
                //noinspection ResultOfMethodCallIgnored
                f.delete();
                if (!tmp.renameTo(f)) //noinspection ResultOfMethodCallIgnored
                    tmp.delete();
            }
            if (++writes % 50 == 0) trim(dirOf(c, pc));
        } catch (JSONException | IOException ignored) {
            // Only a convenience for when the PC is away.
        }
    }

    static Saved get(Context c, String pc, String folderId) {
        File f = fileOf(c, pc, folderId);
        if (!f.isFile()) return null;
        try (InputStream in = new FileInputStream(f)) {
            byte[] data = new byte[(int) f.length()];
            int off = 0, n;
            while (off < data.length && (n = in.read(data, off, data.length - off)) > 0) off += n;
            JSONObject o = new JSONObject(new String(data, 0, off, StandardCharsets.UTF_8));
            if (!folderId.equals(o.optString("folder"))) return null;
            JSONArray items = o.getJSONArray("items");
            List<DocInfo> list = new ArrayList<>();
            for (int i = 0; i < items.length(); i++) {
                JSONObject j = items.getJSONObject(i);
                DocInfo d = new DocInfo();
                d.id = j.getString("id");
                d.name = j.getString("name");
                d.dir = j.optBoolean("dir");
                d.size = j.optLong("size", -1);
                d.modified = j.optLong("modified");
                d.canEdit = j.optBoolean("canEdit");
                d.canCreate = j.optBoolean("canCreate");
                d.exported = j.optBoolean("exported");
                list.add(d);
            }
            return new Saved(list, o.optLong("savedAt"));
        } catch (JSONException | IOException e) {
            return null;
        }
    }

    /** An item from the saved listing of its folder (for when the PC cannot be asked about it). */
    static DocInfo find(Context c, String pc, String id) {
        int slash = id.lastIndexOf('/');
        String parent = slash > 0 ? id.substring(0, slash) : id.substring(0, id.indexOf(':') + 1);
        Saved s = get(c, pc, parent);
        if (s == null) return null;
        for (DocInfo d : s.items) if (d.id.equals(id)) return d;
        return null;
    }

    private static void trim(File dir) {
        File[] files = dir.listFiles();
        if (files == null || files.length <= MAX_FOLDERS) return;
        Arrays.sort(files, new Comparator<File>() {
            @Override
            public int compare(File a, File b) {
                return Long.compare(a.lastModified(), b.lastModified());
            }
        });
        for (int i = 0; i < files.length - MAX_FOLDERS; i++) //noinspection ResultOfMethodCallIgnored
            files[i].delete();
    }
}
