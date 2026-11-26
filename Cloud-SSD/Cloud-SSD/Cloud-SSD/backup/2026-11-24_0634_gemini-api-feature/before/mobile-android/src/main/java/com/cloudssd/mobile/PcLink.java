package com.cloudssd.mobile;

import android.content.Context;
import android.content.SharedPreferences;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.SystemClock;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.NetworkInterface;
import java.net.URL;
import java.net.URLEncoder;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.ArrayList;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;

/**
 * Which address reaches the paired PC right now. At home that is its Wi-Fi address; away from home it is its
 * Tailscale address (when the PC allows that). Before the pairing key is sent anywhere, the address must answer with
 * this PC's public id: on a café's Wi-Fi "192.168.1.5" is someone else's device and must never get the key.
 * Used by everything that talks to the PC without the page: the Files app, camera backup, transfers.
 */
final class PcLink {
    private static final String PREFS = "cloudssd";
    private static final long VERIFIED_MS = 60_000;
    private static final long DOWN_MS = 5_000;

    private static final Object lock = new Object();
    private static String verifiedUrl = "";
    private static String verifiedNet = "";
    private static long verifiedAt;
    private static final Map<String, Long> downUntil = new HashMap<>();

    private PcLink() {
    }

    private static SharedPreferences prefs(Context c) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    /** A random id for this install of the app; the PC uses it to keep one entry per phone. */
    static String installId(Context c) {
        SharedPreferences p = prefs(c);
        String id = p.getString("install_id", "");
        if (id.isEmpty()) {
            id = UUID.randomUUID().toString().replace("-", "");
            p.edit().putString("install_id", id).apply();
        }
        return id;
    }

    static boolean isPaired(Context c) {
        return !prefs(c).getString("server", "").isEmpty() && !TokenStore.pcToken(c).isEmpty();
    }

    /** The page tells us the PC's addresses: the one it paired on (home) and the ones that work away (Tailscale). */
    static void setCandidates(Context c, String lan, JSONArray away, String pcId) {
        setCandidates(c, lan, away, pcId, null);
    }

    /** Also this phone's id on the PC (or "legacy"), which the PC's /api/hello proof is made for. */
    static void setCandidates(Context c, String lan, JSONArray away, String pcId, String deviceId) {
        setCandidates(c, lan, away, pcId, deviceId, null);
    }

    /** direct: the PC's other addresses without the internet (its hotspot, a USB cable, …), from its /api/status. */
    static void setCandidates(Context c, String lan, JSONArray away, String pcId, String deviceId, JSONArray direct) {
        SharedPreferences.Editor e = prefs(c).edit();
        if (lan != null && !lan.isEmpty()) e.putString("server_lan", strip(lan));
        if (away != null) e.putString("server_away", away.toString());
        if (direct != null) e.putString("server_direct", direct.toString());
        if (pcId != null && !pcId.isEmpty()) e.putString("pc_id", pcId);
        if (deviceId != null && !deviceId.isEmpty()) e.putString("device_id", deviceId);
        e.apply();
        synchronized (lock) {
            verifiedAt = 0;
        }
    }

    /** A new key (paired again, or with another PC): the old device id no longer goes with it. */
    static void forgetDeviceId(Context c) {
        prefs(c).edit().remove("device_id").apply();
    }

    /** Another PC became the active one: nothing proven about the previous one's addresses carries over. */
    static void reset() {
        synchronized (lock) {
            verifiedUrl = "";
            verifiedNet = "";
            verifiedAt = 0;
            downUntil.clear();
        }
    }

    private static String strip(String url) {
        String u = url.trim();
        while (u.endsWith("/")) u = u.substring(0, u.length() - 1);
        return u;
    }

    // ---- the network the phone is on ----------------------------------------------------------------------------

    /** {wifi, metered, vpn, tailnetIp, key}: what the phone is connected through right now. */
    static JSONObject netInfo(Context c) {
        JSONObject o = new JSONObject();
        try {
            ConnectivityManager cm = (ConnectivityManager) c.getSystemService(Context.CONNECTIVITY_SERVICE);
            Network n = cm == null ? null : cm.getActiveNetwork();
            NetworkCapabilities caps = n == null ? null : cm.getNetworkCapabilities(n);
            boolean wifi = caps != null && (caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) || caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET));
            boolean vpn = caps != null && caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN);
            // With Tailscale on, the active network is the VPN; whether the phone is really on Wi-Fi underneath is
            // told by a Wi-Fi network existing at all.
            if (vpn && cm != null) {
                for (Network other : cm.getAllNetworks()) {
                    NetworkCapabilities oc = cm.getNetworkCapabilities(other);
                    if (oc != null && !oc.hasTransport(NetworkCapabilities.TRANSPORT_VPN)
                            && (oc.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) || oc.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET))) wifi = true;
                }
            }
            boolean metered = cm != null && cm.isActiveNetworkMetered();
            o.put("wifi", wifi);
            o.put("metered", metered);
            o.put("vpn", vpn);
            o.put("tailnetIp", tailnetIp());
            // Any way to reach a PC without the internet: Wi-Fi, this phone's hotspot, USB tethering, Ethernet.
            String links = LinkScanner.signature();
            o.put("direct", !links.isEmpty());
            o.put("key", (n == null ? "none" : n.toString()) + (wifi ? ":w" : ":m") + "|" + links);
        } catch (Exception ignored) {
        }
        return o;
    }

    /** This phone's Tailscale address (100.64.0.0/10) when the Tailscale app is connected, else "". */
    static String tailnetIp() {
        try {
            for (NetworkInterface ni : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!ni.isUp()) continue;
                for (InetAddress a : Collections.list(ni.getInetAddresses())) {
                    if (!(a instanceof Inet4Address)) continue;
                    byte[] b = a.getAddress();
                    if ((b[0] & 0xFF) == 100 && (b[1] & 0xC0) == 64) return a.getHostAddress();
                }
            }
        } catch (Exception ignored) {
        }
        return "";
    }

    // ---- choosing the address -----------------------------------------------------------------------------------

    /**
     * The addresses to try, best first: the PC on this phone's own localhost (USB debugging), addresses on a link this
     * phone has now (Wi-Fi, its hotspot, a USB cable), the home address, the PC's other direct addresses, then
     * Tailscale. Without any direct link only localhost and Tailscale are tried.
     */
    static List<String> candidates(Context c, boolean direct) {
        SharedPreferences p = prefs(c);
        List<String> out = new ArrayList<>();
        String lan = p.getString("server_lan", p.getString("server", ""));
        List<String> others = new ArrayList<>();
        try {
            JSONArray d = new JSONArray(p.getString("server_direct", "[]"));
            for (int i = 0; i < d.length(); i++) {
                String u = strip(d.optString(i, ""));
                if (!u.isEmpty()) others.add(u);
            }
        } catch (JSONException ignored) {
        }
        if (lan.contains("127.0.0.1")) out.add(strip(lan));
        if (direct) {
            for (String u : others) if (!out.contains(u) && LinkScanner.sameSubnet(hostOf(u))) out.add(u);
        }
        if (!lan.isEmpty() && !out.contains(strip(lan)) && (direct || lan.contains("://100."))) out.add(strip(lan));
        if (direct) {
            for (String u : others) if (!out.contains(u)) out.add(u);
        }
        try {
            JSONArray away = new JSONArray(p.getString("server_away", "[]"));
            for (int i = 0; i < away.length(); i++) {
                String u = strip(away.optString(i, ""));
                if (!u.isEmpty() && !out.contains(u)) out.add(u);
            }
        } catch (JSONException ignored) {
        }
        String last = p.getString("server", "");
        if (!last.isEmpty() && !out.contains(strip(last)) && (direct || last.contains("127.0.0.1"))) out.add(strip(last));
        return out;
    }

    /** "192.168.42.10" for "http://192.168.42.10:8090". */
    static String hostOf(String url) {
        String u = url.replaceFirst("^https?://", "");
        int slash = u.indexOf('/');
        if (slash >= 0) u = u.substring(0, slash);
        int colon = u.lastIndexOf(':');
        return colon > 0 ? u.substring(0, colon) : u;
    }

    /**
     * An address of the paired PC that answers now, or "" when none does. Checked with the PC's public id before
     * the key is ever sent there; the answer is reused for a minute on the same network.
     */
    static String resolve(Context c) {
        JSONObject net = netInfo(c);
        String key = net.optString("key", "");
        synchronized (lock) {
            if (!verifiedUrl.isEmpty() && key.equals(verifiedNet) && SystemClock.elapsedRealtime() - verifiedAt < VERIFIED_MS && !isDown(verifiedUrl))
                return verifiedUrl;
        }
        final String pcId = prefs(c).getString("pc_id", "");
        final String deviceId = prefs(c).getString("device_id", "");
        final String token = TokenStore.pcToken(c);
        final List<String> urls = new ArrayList<>();
        for (String url : candidates(c, net.optBoolean("direct", true))) if (!isDown(url)) urls.add(url);
        String found = firstThatIsOurs(urls, pcId, deviceId, token);
        if (found.isEmpty()) return "";
        synchronized (lock) {
            verifiedUrl = found;
            verifiedNet = key;
            verifiedAt = SystemClock.elapsedRealtime();
        }
        prefs(c).edit().putString("server", found).apply();
        return found;
    }

    private static final java.util.concurrent.ExecutorService PROBES = java.util.concurrent.Executors.newCachedThreadPool(r -> {
        Thread t = new Thread(r, "cloudssd-probe");
        t.setDaemon(true);
        return t;
    });
    /** How long the addresses are given together; one that does not answer by then is left for next time. */
    private static final long PROBE_ALL_MS = 4000;
    /** A better (earlier) address still being asked gets this long once a later one has answered. */
    private static final long PREFER_EARLIER_MS = 400;

    /**
     * Asks every address at once (it used to be one after another: an address that does not answer made the Files
     * app wait ~10 s before saying the PC is away). The first in the usual order that proves to be our PC wins; a
     * later address that answers first waits a moment for the earlier, better ones (USB, home Wi-Fi before Tailscale).
     */
    static String firstThatIsOurs(final List<String> urls, final String pcId, final String deviceId, final String token) {
        if (urls.isEmpty()) return "";
        final int n = urls.size();
        final Boolean[] result = new Boolean[n];
        final Object done = new Object();
        for (int i = 0; i < n; i++) {
            final int k = i;
            PROBES.execute(() -> {
                boolean ok = false;
                try {
                    ok = identify(urls.get(k), pcId, deviceId, token);
                } catch (Exception ignored) {
                }
                if (!ok) markDown(urls.get(k));
                synchronized (done) {
                    result[k] = ok;
                    done.notifyAll();
                }
            });
        }
        long deadline = SystemClock.elapsedRealtime() + PROBE_ALL_MS;
        long preferUntil = Long.MAX_VALUE;
        synchronized (done) {
            while (true) {
                // The best answer so far: the earliest address that proved itself, if every earlier one has failed.
                for (int i = 0; i < n; i++) {
                    if (result[i] == null) break;
                    if (result[i]) return urls.get(i);
                }
                int firstOk = -1;
                boolean allDone = true;
                for (int i = 0; i < n; i++) {
                    if (result[i] == null) allDone = false;
                    else if (result[i] && firstOk < 0) firstOk = i;
                }
                long now = SystemClock.elapsedRealtime();
                if (firstOk >= 0 && preferUntil == Long.MAX_VALUE) preferUntil = now + PREFER_EARLIER_MS;
                if (firstOk >= 0 && now >= preferUntil) return urls.get(firstOk);
                if (allDone) return firstOk >= 0 ? urls.get(firstOk) : "";
                if (now >= deadline) return firstOk >= 0 ? urls.get(firstOk) : "";
                try {
                    done.wait(Math.max(1, Math.min(deadline, preferUntil) - now));
                } catch (InterruptedException e) {
                    return firstOk >= 0 ? urls.get(firstOk) : "";
                }
            }
        }
    }

    /**
     * True when the address is our PC. A PC with /api/hello must answer a fresh challenge with the hash of this
     * phone's key (a device that only copied the PC's public id cannot); an older PC, or a phone that does not know
     * its id on the PC yet, is checked by the public id (accepted on the address it was paired on when it has none).
     */
    static boolean identify(String url, String pcId, String deviceId, String token) {
        if (!deviceId.isEmpty() && !token.isEmpty()) {
            String nonce = randomHex(16);
            JSONObject h;
            try {
                h = getJsonOrThrow(url + "/api/hello?nonce=" + nonce + "&d=" + encode(deviceId));
            } catch (IOException e) {
                return false;   // nothing answers there: no point asking the same address for its status too
            }
            if (h != null && h.has("proof")) {
                String theirs = h.optString("pcId", "");
                if (!pcId.isEmpty() && !pcId.equals(theirs)) return false;
                return MessageDigest.isEqual(helloProof(token, nonce, theirs).getBytes(UTF8), h.optString("proof", "").getBytes(UTF8));
            }
        }
        JSONObject s = getJson(url + "/api/status");
        if (s == null || !s.has("pcName")) return false;
        // A PC that has /api/hello but did not prove itself above is not trusted by its public id alone.
        if (!deviceId.isEmpty() && hasFeature(s, "hello-proof")) return false;
        String theirs = s.optString("pcId", "");
        return pcId.isEmpty() || theirs.isEmpty() || pcId.equals(theirs);
    }

    /** HMAC-SHA256(SHA-256(key), nonce + "\n" + pcId) as lowercase hex — what the real PC answers on /api/hello. */
    static String helloProof(String token, String nonce, String pcId) {
        try {
            byte[] key = MessageDigest.getInstance("SHA-256").digest(token.getBytes(UTF8));
            Mac mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(key, "HmacSHA256"));
            return hex(mac.doFinal((nonce + "\n" + pcId).getBytes(UTF8)));
        } catch (Exception e) {
            return "";
        }
    }

    private static final java.nio.charset.Charset UTF8 = java.nio.charset.Charset.forName("UTF-8");
    private static final SecureRandom RANDOM = new SecureRandom();

    private static String randomHex(int bytes) {
        byte[] b = new byte[bytes];
        RANDOM.nextBytes(b);
        return hex(b);
    }

    private static String hex(byte[] b) {
        StringBuilder sb = new StringBuilder(b.length * 2);
        for (byte x : b) sb.append(String.format(java.util.Locale.US, "%02x", x & 0xFF));
        return sb.toString();
    }

    private static String encode(String s) {
        try {
            return URLEncoder.encode(s, "UTF-8");
        } catch (Exception e) {
            return s;
        }
    }

    private static boolean hasFeature(JSONObject status, String feature) {
        JSONArray f = status.optJSONArray("features");
        if (f == null) return false;
        for (int i = 0; i < f.length(); i++) if (feature.equals(f.optString(i))) return true;
        return false;
    }

    /** A JSON answer from a GET without the key, or null (not reachable, not 200, not JSON). */
    private static JSONObject getJson(String url) {
        try {
            return getJsonOrThrow(url);
        } catch (IOException e) {
            return null;
        }
    }

    /**
     * A JSON answer, or null when something answered but not with JSON (an older PC without that route); throws when
     * nothing answers. A PC answers these in milliseconds, so 2.5 s is plenty even through Tailscale.
     */
    private static JSONObject getJsonOrThrow(String url) throws IOException {
        HttpURLConnection conn = null;
        try {
            conn = (HttpURLConnection) new URL(url).openConnection();
            conn.setConnectTimeout(2500);
            conn.setReadTimeout(2500);
            conn.setUseCaches(false);
            conn.setRequestProperty("Connection", "close");
            if (conn.getResponseCode() != 200) return null;
            return new JSONObject(readAll(conn.getInputStream()));
        } catch (JSONException e) {
            return null;
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    static void markDown(String url) {
        synchronized (lock) {
            downUntil.put(url, SystemClock.elapsedRealtime() + DOWN_MS);
            if (url.equals(verifiedUrl)) verifiedAt = 0;
        }
    }

    private static boolean isDown(String url) {
        synchronized (lock) {
            Long until = downUntil.get(url);
            return until != null && SystemClock.elapsedRealtime() < until;
        }
    }

    /** "lan" or "tailnet" for the address in use. */
    static String routeOf(String url) {
        return url != null && url.contains("://100.") || (url != null && url.contains(".ts.net")) ? "tailnet" : "lan";
    }

    private static String readAll(InputStream in) throws java.io.IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
        return out.toString("UTF-8");
    }
}
