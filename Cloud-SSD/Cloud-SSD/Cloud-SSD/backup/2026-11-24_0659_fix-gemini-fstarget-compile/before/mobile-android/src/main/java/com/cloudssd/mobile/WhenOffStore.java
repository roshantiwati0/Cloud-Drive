package com.cloudssd.mobile;

import android.content.Context;
import android.content.SharedPreferences;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;

/**
 * "Files when it is off": the PCs whose copy in the cloud this phone may open while the PC is switched off. A PC is in
 * here only while this phone and that PC both said yes; the page writes it whenever a PC tells how it stands, and
 * takes it out when either side says no or the PC is forgotten. Kept on the phone because it is needed exactly when
 * the PC cannot be asked.
 */
final class WhenOffStore {
    private static final String PREFS = "cloudssd_whenoff";

    private WhenOffStore() {
    }

    private static SharedPreferences prefs(Context c) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    private static JSONObject all(Context c) {
        try {
            return new JSONObject(prefs(c).getString("pcs", "{}"));
        } catch (JSONException e) {
            return new JSONObject();
        }
    }

    /** Remembers (or, with active = false, forgets) a PC. True when something changed. */
    static synchronized boolean set(Context c, String pcId, String name, boolean active) {
        if (pcId == null || pcId.isEmpty()) return false;
        JSONObject all = all(c);
        JSONObject old = all.optJSONObject(pcId);
        String shown = name == null ? "" : name.trim();
        if (!active) {
            if (old == null) return false;
            all.remove(pcId);
        } else {
            if (old != null && (shown.isEmpty() || shown.equals(old.optString("name")))) return false;
            try {
                all.put(pcId, new JSONObject().put("name", shown.isEmpty() && old != null ? old.optString("name") : shown));
            } catch (JSONException e) {
                return false;
            }
        }
        prefs(c).edit().putString("pcs", all.toString()).commit();
        return true;
    }

    static boolean has(Context c, String pcId) {
        return all(c).has(pcId);
    }

    /** The PC's name as the page last showed it ("PC" when it never said one). */
    static String name(Context c, String pcId) {
        JSONObject o = all(c).optJSONObject(pcId);
        String n = o == null ? "" : o.optString("name");
        return n.isEmpty() ? "PC" : n;
    }

    /** Every PC in here: {id, name}. */
    static List<String[]> pcs(Context c) {
        List<String[]> out = new ArrayList<>();
        JSONObject all = all(c);
        for (Iterator<String> it = all.keys(); it.hasNext(); ) {
            String id = it.next();
            out.add(new String[]{id, name(c, id)});
        }
        return out;
    }
}
