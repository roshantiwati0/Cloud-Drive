package com.cloudssd.mobile;

import android.content.Context;
import android.content.SharedPreferences;

import org.json.JSONArray;
import org.json.JSONObject;

import java.security.MessageDigest;

/**
 * Every PC (or laptop) this phone is paired with, each with its own key. One of them is "active": its address, id and
 * key are also written to the single values the rest of the app has always read ("server", "pc_id", "token"…), so
 * camera backup, the Files app and transfers keep working unchanged and an older app version still finds its PC.
 *
 * <p>Saved in the "cloudssd" preferences: "devices" (a JSON list, no keys), "active_device", and one encrypted key per
 * device ("pct_&lt;hash&gt;", see {@link TokenStore}).
 */
final class PcProfiles {
    private static final String PREFS = "cloudssd";
    private static final Object LOCK = new Object();

    private PcProfiles() {
    }

    private static SharedPreferences prefs(Context c) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    /** A short, file-name-safe hash of a device id (for its key's preference name and its folders). */
    static String key(String id) {
        try {
            byte[] h = MessageDigest.getInstance("SHA-1").digest(id.getBytes("UTF-8"));
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 8; i++) sb.append(String.format(java.util.Locale.US, "%02x", h[i] & 0xFF));
            return sb.toString();
        } catch (Exception e) {
            return Integer.toHexString(id.hashCode());
        }
    }

    private static String tokenName(String id) {
        return "pct_" + key(id);
    }

    private static String strip(String url) {
        String u = url == null ? "" : url.trim();
        while (u.endsWith("/")) u = u.substring(0, u.length() - 1);
        return u;
    }

    // ---- reading -------------------------------------------------------------------------------------------------

    /** All saved devices (migrating the single PC of older versions the first time). Never contains keys. */
    static JSONArray list(Context c) {
        synchronized (LOCK) {
            migrate(c);
            try {
                return new JSONArray(prefs(c).getString("devices", "[]"));
            } catch (Exception e) {
                return new JSONArray();
            }
        }
    }

    static String activeId(Context c) {
        synchronized (LOCK) {
            migrate(c);
            return prefs(c).getString("active_device", "");
        }
    }

    static JSONObject get(Context c, String id) {
        if (id == null || id.isEmpty()) return null;
        JSONArray all = list(c);
        for (int i = 0; i < all.length(); i++) {
            JSONObject o = all.optJSONObject(i);
            if (o != null && id.equals(o.optString("id"))) return o;
        }
        return null;
    }

    /** The device's own key ("" when none is saved). */
    static String token(Context c, String id) {
        return TokenStore.get(prefs(c), tokenName(id));
    }

    // ---- changing ------------------------------------------------------------------------------------------------

    private static void save(Context c, JSONArray all) {
        prefs(c).edit().putString("devices", all.toString()).commit();
    }

    /** The PC older versions knew (the single values) becomes the first saved device, once. */
    private static void migrate(Context c) {
        SharedPreferences p = prefs(c);
        if (p.contains("devices")) return;
        JSONArray all = new JSONArray();
        String lan = strip(p.getString("server_lan", p.getString("server", "")));
        if (!lan.isEmpty()) {
            String pcId = p.getString("pc_id", "");
            String id = pcId.isEmpty() ? "url:" + lan : pcId;
            try {
                JSONObject o = new JSONObject().put("id", id).put("name", "").put("kind", "pc").put("lan", lan)
                        .put("last", strip(p.getString("server", lan))).put("deviceId", p.getString("device_id", ""))
                        .put("addedAt", System.currentTimeMillis());
                try { o.put("away", new JSONArray(p.getString("server_away", "[]"))); } catch (Exception ignored) { o.put("away", new JSONArray()); }
                try { o.put("features", new JSONArray(p.getString("features", "[]"))); } catch (Exception ignored) { }
                all.put(o);
                String t = TokenStore.pcToken(c);
                if (!t.isEmpty()) TokenStore.put(p, tokenName(id), t);
                p.edit().putString("active_device", id).commit();
            } catch (Exception ignored) {
            }
        }
        save(c, all);
    }

    /**
     * Adds or updates a device with the fields given in {@code patch} (id required). A non-null {@code token} replaces
     * its key. With {@code activate} it becomes the active device.
     */
    static boolean upsert(Context c, JSONObject patch, String token, boolean activate) {
        String id = patch.optString("id", "");
        if (id.isEmpty()) return false;
        synchronized (LOCK) {
            JSONArray all = list(c);
            JSONObject rec = null;
            for (int i = 0; i < all.length(); i++) {
                JSONObject o = all.optJSONObject(i);
                if (o != null && id.equals(o.optString("id"))) { rec = o; break; }
            }
            try {
                if (rec == null) {
                    rec = new JSONObject().put("id", id).put("addedAt", System.currentTimeMillis()).put("kind", "pc");
                    all.put(rec);
                }
                java.util.Iterator<String> keys = patch.keys();
                while (keys.hasNext()) {
                    String k = keys.next();
                    if (k.equals("id") || k.equals("token") || k.equals("activate") || k.equals("replaces")) continue;
                    Object v = patch.get(k);
                    if (k.equals("lan") || k.equals("last")) v = strip(String.valueOf(v));
                    rec.put(k, v);
                }
            } catch (Exception e) {
                return false;
            }
            save(c, all);
            if (token != null) TokenStore.put(prefs(c), tokenName(id), token.trim());
        }
        if (activate) activate(c, id);
        return true;
    }

    /**
     * Makes a device the active one: its address, id and key go into the single values everything else reads.
     * Returns {server, token} or null when there is no such device.
     */
    static JSONObject activate(Context c, String id) {
        JSONObject rec = get(c, id);
        if (rec == null) return null;
        String lan = strip(rec.optString("lan", ""));
        String server = strip(rec.optString("last", lan));
        if (server.isEmpty()) server = lan;
        String t = token(c, id);
        synchronized (LOCK) {
            SharedPreferences.Editor e = prefs(c).edit()
                    .putString("server", server)
                    .putString("server_lan", lan)
                    .putString("server_away", rec.optJSONArray("away") != null ? rec.optJSONArray("away").toString() : "[]")
                    .remove("server_direct")   // the new PC tells its own on its first answer
                    .putString("pc_id", id.startsWith("url:") ? "" : id)
                    .putString("device_id", rec.optString("deviceId", ""))
                    .putString("active_device", id);
            if (rec.optJSONArray("features") != null) e.putString("features", rec.optJSONArray("features").toString());
            else e.remove("features");
            e.commit();
            TokenStore.setPcToken(c, t);
        }
        PcLink.reset();
        PcBackend.reset();
        CloudDocumentsProvider.notifyRootsChanged(c);
        try {
            return new JSONObject().put("ok", true).put("server", server).put("token", t).put("id", id);
        } catch (Exception e) {
            return null;
        }
    }

    /**
     * The page changed the single values (paired again, a new address, a new key): the active device's record follows.
     * When those values now name a different PC than the active one, that PC's record is used (made when missing), so
     * one PC's key is never filed under another PC.
     */
    static void syncActiveFromFlat(Context c) {
        synchronized (LOCK) {
            SharedPreferences p = prefs(c);
            String lan = strip(p.getString("server_lan", p.getString("server", "")));
            if (lan.isEmpty()) return;
            String flatPc = p.getString("pc_id", "");
            String id = p.getString("active_device", "");
            if (!flatPc.isEmpty() && !flatPc.equals(id)) id = flatPc;          // a different (or first) PC
            if (id.isEmpty()) id = "url:" + lan;
            try {
                JSONObject patch = new JSONObject().put("id", id).put("lan", lan).put("last", strip(p.getString("server", lan)));
                try { patch.put("away", new JSONArray(p.getString("server_away", "[]"))); } catch (Exception ignored) { }
                String dev = p.getString("device_id", "");
                if (!dev.isEmpty()) patch.put("deviceId", dev);
                try { patch.put("features", new JSONArray(p.getString("features", "[]"))); } catch (Exception ignored) { }
                upsert(c, patch, TokenStore.pcToken(c), false);
                p.edit().putString("active_device", id).commit();
            } catch (Exception ignored) {
            }
        }
    }

    /** Gives a device saved under its address ("url:…") its real id once the PC reports one. */
    static void rekey(Context c, String oldId, String newId) {
        if (oldId == null || newId == null || oldId.equals(newId) || newId.isEmpty()) return;
        synchronized (LOCK) {
            JSONArray all = list(c);
            for (int i = 0; i < all.length(); i++) {
                JSONObject o = all.optJSONObject(i);
                if (o != null && oldId.equals(o.optString("id"))) {
                    try { o.put("id", newId); } catch (Exception ignored) { }
                }
            }
            save(c, all);
            String t = token(c, oldId);
            if (!t.isEmpty()) TokenStore.put(prefs(c), tokenName(newId), t);
            TokenStore.put(prefs(c), tokenName(oldId), "");
            if (oldId.equals(prefs(c).getString("active_device", ""))) prefs(c).edit().putString("active_device", newId).commit();
        }
    }

    /**
     * Forgets a device and its key on this phone. When it was the active one, another saved device becomes active, or
     * (none left) the phone is simply not paired any more.
     */
    static boolean remove(Context c, String id) {
        String next = "";
        boolean wasActive;
        synchronized (LOCK) {
            JSONArray all = list(c), kept = new JSONArray();
            boolean found = false;
            for (int i = 0; i < all.length(); i++) {
                JSONObject o = all.optJSONObject(i);
                if (o == null) continue;
                if (id.equals(o.optString("id"))) { found = true; continue; }
                kept.put(o);
                if (next.isEmpty()) next = o.optString("id");
            }
            if (!found) return false;
            save(c, kept);
            TokenStore.put(prefs(c), tokenName(id), "");
            wasActive = id.equals(prefs(c).getString("active_device", ""));
            if (wasActive && next.isEmpty()) {
                prefs(c).edit().remove("server").remove("server_lan").remove("server_away").remove("server_direct").remove("pc_id").remove("device_id")
                        .remove("features").remove("active_device").commit();
                TokenStore.setPcToken(c, "");
            }
        }
        if (wasActive && !next.isEmpty()) activate(c, next);
        else if (wasActive) {
            PcLink.reset();
            PcBackend.reset();
            CloudDocumentsProvider.notifyRootsChanged(c);
        }
        return true;
    }

    /** The list for the page: every device with "active" and "hasKey", never the keys themselves. */
    static JSONArray forPage(Context c) {
        JSONArray all = list(c), out = new JSONArray();
        String active = activeId(c);
        for (int i = 0; i < all.length(); i++) {
            JSONObject o = all.optJSONObject(i);
            if (o == null) continue;
            try {
                JSONObject copy = new JSONObject(o.toString());
                String id = copy.optString("id");
                copy.put("active", id.equals(active));
                copy.put("hasKey", !token(c, id).isEmpty());
                out.put(copy);
            } catch (Exception ignored) {
            }
        }
        return out;
    }
}
