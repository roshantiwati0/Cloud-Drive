package com.cloudssd.mobile;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.ActivityManager;
import android.app.usage.StorageStatsManager;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.ContentUris;
import android.content.ContentValues;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.content.res.Configuration;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.media.ThumbnailUtils;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Debug;
import android.os.Environment;
import android.os.StatFs;
import android.os.storage.StorageManager;
import android.provider.DocumentsContract;
import android.provider.MediaStore;
import android.provider.OpenableColumns;
import android.provider.Settings;
import android.util.Log;
import android.util.LruCache;
import android.util.Size;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.webkit.ConsoleMessage;
import android.webkit.JavascriptInterface;
import android.webkit.URLUtil;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.FileReader;
import java.io.OutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.NetworkInterface;
import java.net.Socket;
import java.net.URL;
import java.net.URLConnection;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

import com.cloudssd.mobile.CloudBackend.BackendException;
import com.cloudssd.mobile.CloudBackend.DocInfo;
import com.cloudssd.mobile.CloudBackend.RootInfo;

/**
 * Cloud-SSD phone app: a WebView over the bundled page (assets/index.html) plus the native pieces a page cannot do:
 * finding the PC on the Wi-Fi, reading phone storage and memory, transfers that keep going in the background,
 * thumbnails, PDFs, Google Drive without the PC. The page calls {@link AndroidBridge}; results that take time come
 * back through window.onNative(event, data).
 */
public class MainActivity extends Activity {
    private static final String TAG = "CloudSSD";
    private static final int REQ_WEB_CHOOSER = 1001;
    private static final int REQ_PICK_UPLOAD = 2001;
    private static final int REQ_PERMS = 101;
    private static final String THUMB_HOST = "cloudssd.thumb";
    private static final String PDF_HOST = "cloudssd.pdf";
    private static final String CLOUD_HOST = "cloudssd.cloud";
    /** Pictures of photos and videos on the PC, kept on the phone so they also show while the PC is away. */
    private static final String PC_THUMB_HOST = "cloudssd.pcthumb";
    /** cloudssd.offline/f?pc=…&id=…: a file kept on this phone ("Offline rakho"), with Range support. */
    private static final String OFFLINE_HOST = "cloudssd.offline";
    private static final String PREFS = "cloudssd";
    private static final int MAX_LIST_ITEMS = 3000;

    private WebView webView;
    /** Behind the status bar and the navigation bar: colours the page's look chose (setBars). */
    private BarsDrawable bars;
    /** Asks for the fingerprint or screen lock when the app opens or comes back (when switched on). */
    private AppLock appLock;
    private ValueCallback<Uri[]> webChooserCallback;
    private volatile String serverBaseUrl = "";
    private volatile String uploadTarget = "";
    /** A cloudssd://pair link that arrived before the page could take it (JSON, or ""). */
    private volatile String pendingPairLink = "";
    /** Opened from the "listening for your PC" note: the page shows Settings → Find my phone (to switch it off). */
    static final String ACTION_FIND_MY_PHONE = "com.cloudssd.mobile.FIND_MY_PHONE";
    private volatile boolean pendingFindMyPhone;
    /** Files shared to "Send to a device…", until the page says where they go. */
    private final List<Uri> shareIn = new ArrayList<>();

    private List<Uri> takeShareIn() {
        synchronized (shareIn) {
            List<Uri> u = new ArrayList<>(shareIn);
            shareIn.clear();
            return u;
        }
    }

    private final ExecutorService worker = Executors.newFixedThreadPool(3);
    private final AtomicBoolean discovering = new AtomicBoolean(false);
    private final LruCache<String, byte[]> thumbCache = new LruCache<String, byte[]>(16 * 1024 * 1024) {
        @Override
        protected int sizeOf(String key, byte[] value) {
            return value.length;
        }
    };
    private final TransferHub.Listener transferListener = new TransferHub.Listener() {
        @Override
        public void onTransfer(JSONObject t) { emit("transfer", t); }

        @Override
        public void onTask(JSONObject task) { emit("task", task); }
    };
    /** What the page said about the next batch (device, saveTo…), taken by the next one that starts. */
    private volatile JSONObject nextTaskMeta;

    /** Starts a task for a batch: the defaults here, with what the page told before (native.nextTask) on top. */
    private String startTask(JSONObject defaults) {
        TransferHub hub = TransferHub.get(this);
        JSONObject meta = defaults;
        JSONObject told = nextTaskMeta;
        nextTaskMeta = null;
        try {
            if (!meta.has("device")) meta.put("device", hub.pcName);
            if (!meta.has("link")) meta.put("link", hub.linkKind);
            if (told != null) for (java.util.Iterator<String> k = told.keys(); k.hasNext(); ) { String key = k.next(); meta.put(key, told.get(key)); }
        } catch (Exception ignored) { }
        return hub.beginTask(meta);
    }

    private static JSONObject meta(String... kv) {
        JSONObject o = new JSONObject();
        try { for (int i = 0; i + 1 < kv.length; i += 2) o.put(kv[i], kv[i + 1]); } catch (Exception ignored) { }
        return o;
    }
    private final OfflinePins.Listener offlineListener = ev -> emit("offline", ev);
    private final PeerLink.Listener peerListener = (pc, name, scope) -> {
        try {
            emit("peer", new JSONObject().put("type", "ask").put("pc", pc).put("name", name).put("scope", scope));
        } catch (JSONException ignored) {
        }
    };

    // ------------------------------------------------------------------ small helpers

    private static String extOf(String name) {
        int dot = name == null ? -1 : name.lastIndexOf('.');
        return dot < 0 ? "" : name.substring(dot + 1).toLowerCase(Locale.US);
    }

    private static final List<String> IMAGE_EXT = Arrays.asList("jpg", "jpeg", "png", "gif", "webp", "bmp", "heic", "heif", "dng", "svg");
    private static final List<String> VIDEO_EXT = Arrays.asList("mp4", "mkv", "webm", "avi", "mov", "m4v", "3gp", "ts", "flv", "wmv");
    private static final List<String> AUDIO_EXT = Arrays.asList("mp3", "wav", "flac", "aac", "ogg", "m4a", "opus", "amr", "wma", "mid");
    // Real documents only: .xml/.json/.html here would fill "Documents" with files apps write for themselves.
    private static final List<String> DOC_EXT = Arrays.asList("pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "csv", "rtf", "odt", "ods", "odp", "epub");
    private static final List<String> APP_EXT = Arrays.asList("apk", "apks", "xapk", "apkm", "zip", "rar", "7z", "tar", "gz", "iso");

    /** images | videos | audio | documents | apps | other — the same buckets the phone's own Files app uses. */
    private static String kindOf(String name) {
        String e = extOf(name);
        if (IMAGE_EXT.contains(e)) return "images";
        if (VIDEO_EXT.contains(e)) return "videos";
        if (AUDIO_EXT.contains(e)) return "audio";
        if (DOC_EXT.contains(e)) return "documents";
        if (APP_EXT.contains(e)) return "apps";
        return "other";
    }

    private static String mimeOf(String name) {
        String mime = URLConnection.guessContentTypeFromName(name);
        if (mime != null) return mime;
        switch (extOf(name)) {
            case "mkv": return "video/x-matroska";
            case "apk": return "application/vnd.android.package-archive";
            case "flac": return "audio/flac";
            case "opus": return "audio/ogg";
            case "heic": return "image/heic";
            case "7z": return "application/x-7z-compressed";
            case "rar": return "application/vnd.rar";
            default: return "application/octet-stream";
        }
    }

    /** Sends a result to the page: window.onNative(event, payload). The payload is JSON, which is also valid JavaScript. */
    private void emit(final String event, final Object payload) {
        final String js = "window.onNative&&window.onNative('" + event + "'," + payload + ");";
        runOnUiThread(() -> {
            if (webView != null) webView.evaluateJavascript(js, null);
        });
    }

    /** A new file in Downloads/Cloud-SSD; when the name is taken, "name (1).ext" (Android picks it on 10+). */
    private OutputStream openInDownloads(String name) throws IOException {
        if (Build.VERSION.SDK_INT >= 29) {
            ContentValues v = new ContentValues();
            v.put(MediaStore.MediaColumns.DISPLAY_NAME, name);
            v.put(MediaStore.MediaColumns.MIME_TYPE, mimeOf(name));
            v.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/Cloud-SSD");
            Uri uri = getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, v);
            OutputStream o = uri == null ? null : getContentResolver().openOutputStream(uri, "w");
            if (o == null) throw new IOException("Cannot create the file in Downloads");
            return o;
        }
        File dir = new File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), "Cloud-SSD");
        if (!dir.exists() && !dir.mkdirs()) throw new IOException("Cannot make Downloads/Cloud-SSD");
        int dot = name.lastIndexOf('.');
        String base = dot > 0 ? name.substring(0, dot) : name, ext = dot > 0 ? name.substring(dot) : "";
        File f = new File(dir, name);
        for (int i = 1; f.exists(); i++) f = new File(dir, base + " (" + i + ")" + ext);
        return new FileOutputStream(f);
    }

    /** True when Android may not stop this app in the background to save battery. */
    private boolean batteryUnrestricted() {
        if (Build.VERSION.SDK_INT < 23) return true;
        android.os.PowerManager pm = (android.os.PowerManager) getSystemService(POWER_SERVICE);
        return pm != null && pm.isIgnoringBatteryOptimizations(getPackageName());
    }

    /** One system dialog: "Let Cloud-SSD always run in the background?" (else the app's battery settings). */
    private void askBatteryUnrestricted() {
        if (Build.VERSION.SDK_INT < 23 || batteryUnrestricted()) return;
        runOnUiThread(() -> {
            try {
                startActivity(new Intent(android.provider.Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:" + getPackageName())));
            } catch (Exception e) {
                try {
                    startActivity(new Intent(android.provider.Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS));
                } catch (Exception ignored) {
                    toast("Settings → Apps → Cloud-SSD → Battery → Unrestricted");
                }
            }
        });
    }

    private void toast(final String text) {
        // Fixed messages are translated here; ones with values come translated (Tr.t with {0}).
        final String shown = Tr.t(MainActivity.this, text);
        runOnUiThread(() -> Toast.makeText(MainActivity.this, shown, Toast.LENGTH_SHORT).show());
    }

    private boolean hasPermission(String permission) {
        return Build.VERSION.SDK_INT < 23 || checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED;
    }

    private boolean debuggable() {
        return (getApplicationInfo().flags & ApplicationInfo.FLAG_DEBUGGABLE) != 0;
    }

    private boolean nightMode() {
        return (getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES;
    }

    /** "all" = every file (All files access), "media" = photos/videos/audio only, "none" = nothing yet. */
    private String storageAccess() {
        if (Build.VERSION.SDK_INT >= 30) {
            if (Environment.isExternalStorageManager()) return "all";
            if (Build.VERSION.SDK_INT >= 33) {
                boolean any = hasPermission("android.permission.READ_MEDIA_IMAGES") || hasPermission("android.permission.READ_MEDIA_VIDEO")
                        || hasPermission("android.permission.READ_MEDIA_AUDIO");
                return any ? "media" : "none";
            }
            return hasPermission("android.permission.READ_EXTERNAL_STORAGE") ? "media" : "none";
        }
        return hasPermission("android.permission.READ_EXTERNAL_STORAGE") ? "all" : "none";
    }

    private static String phoneWifiIp() {
        try {
            for (NetworkInterface ni : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!ni.isUp() || ni.isLoopback() || !isLanInterface(ni.getName())) continue;
                for (InetAddress a : Collections.list(ni.getInetAddresses())) {
                    if (a instanceof Inet4Address && a.isSiteLocalAddress()) return a.getHostAddress();
                }
            }
        } catch (Exception ignored) { }
        return "";
    }

    /** Wi-Fi, hotspot, USB tethering and Ethernet — never the mobile-data interface (its "local" range is the carrier's). */
    private static boolean isLanInterface(String name) {
        String n = name == null ? "" : name.toLowerCase(Locale.US);
        return n.startsWith("wlan") || n.startsWith("ap") || n.startsWith("swlan") || n.startsWith("softap")
                || n.startsWith("eth") || n.startsWith("usb") || n.startsWith("rndis") || n.startsWith("wifi");
    }

    /** True for an address on the paired PC (the only place the pairing key may be sent). */
    private boolean isOurServer(String url) {
        if (url == null) return false;
        String server = getSharedPreferences(PREFS, MODE_PRIVATE).getString("server", "");
        if (!server.isEmpty() && url.startsWith(server + "/")) return true;
        for (String c : PcLink.candidates(this, true)) if (url.startsWith(c + "/")) return true;
        return false;
    }

    /** "/api/download?path=…" from a full PC address, without the server part and without the key. */
    private static String routeOf(String url) {
        String r = url;
        int scheme = r.indexOf("://");
        if (scheme > 0) {
            int slash = r.indexOf('/', scheme + 3);
            r = slash < 0 ? "/" : r.substring(slash);
        }
        return r.replaceAll("([?&])token=[^&]*&?", "$1").replaceAll("[?&]$", "");
    }

    private static JSONObject docJson(DocInfo d) {
        JSONObject o = new JSONObject();
        try {
            o.put("id", d.id).put("name", d.name).put("isDirectory", d.dir).put("size", d.size).put("modified", d.modified)
                    .put("canEdit", d.canEdit).put("canCreate", d.canCreate).put("exported", d.exported);
        } catch (Exception ignored) { }
        return o;
    }

    /**
     * The icons the person can give the app on the home screen: {key the page uses, the activity-alias in the manifest
     * that carries the icon}. The first one is the alias that is on in a new install. "classic" is not offered any
     * more (the page has no card for it); it stays here so that picking another icon switches its alias off.
     */
    private static final String[][] APP_ICONS = {
        { "ocean", "IconOcean" }, { "kit", "IconKit" }, { "paper", "IconPaper" }, { "glow", "IconGlow" },
        { "midnight", "IconMidnight" }, { "sunset", "IconSunset" }, { "classic", "IconClassic" }
    };

    private android.content.ComponentName iconAlias(String alias) {
        return new android.content.ComponentName(getPackageName(), "com.cloudssd.mobile." + alias);
    }

    // ------------------------------------------------------------------ the page's native interface

    public class AndroidBridge {
        @JavascriptInterface
        public String getAppInfo() {
            JSONObject o = new JSONObject();
            try {
                o.put("version", getPackageManager().getPackageInfo(getPackageName(), 0).versionName);
                o.put("model", Build.MODEL);
                o.put("manufacturer", Build.MANUFACTURER);
                o.put("android", Build.VERSION.RELEASE);
                o.put("sdk", Build.VERSION.SDK_INT);
                o.put("phoneIp", phoneWifiIp());
                o.put("storageAccess", storageAccess());
                o.put("network", PcLink.netInfo(MainActivity.this));
                o.put("installId", PcLink.installId(MainActivity.this));
                o.put("meName", getSharedPreferences(PREFS, MODE_PRIVATE).getString("me_name", ""));
                o.put("tablet", getResources().getConfiguration().smallestScreenWidthDp >= 600);
                o.put("googleAvailable", GoogleAuth.available(MainActivity.this));
                o.put("night", nightMode());
                o.put("usageAccess", SpaceScanner.hasUsageAccess(MainActivity.this));
                o.put("tailscale", new JSONObject().put("installed", tailscaleInstalled()).put("vpn", vpnActive()));
            } catch (Exception ignored) { }
            return o.toString();
        }

        /** The paired PC and this phone's key, for the page (the key lives encrypted on the phone, not in the page). */
        @JavascriptInterface
        public String getPairing() {
            try {
                return new JSONObject().put("server", getSharedPreferences(PREFS, MODE_PRIVATE).getString("server_lan",
                        getSharedPreferences(PREFS, MODE_PRIVATE).getString("server", ""))).put("token", TokenStore.pcToken(MainActivity.this)).toString();
            } catch (Exception e) {
                return "{}";
            }
        }

        /** The page tells us which PC it is paired with, so uploads and downloads go to the same place. */
        @JavascriptInterface
        public void setServerBase(String url, String token) {
            String clean = url == null ? "" : url.trim();
            while (clean.endsWith("/")) clean = clean.substring(0, clean.length() - 1);
            SharedPreferences p = getSharedPreferences(PREFS, MODE_PRIVATE);
            boolean changed = !p.getString("server_lan", p.getString("server", "")).equals(clean);
            serverBaseUrl = clean;
            p.edit().putString("server", clean).putString("server_lan", clean).commit();
            String newToken = token == null ? "" : token.trim();
            // A new key belongs to a new pairing: the page sends the matching device id with the next status.
            if (!newToken.equals(TokenStore.pcToken(MainActivity.this))) PcLink.forgetDeviceId(MainActivity.this);
            TokenStore.setPcToken(MainActivity.this, newToken);
            PcLink.setCandidates(MainActivity.this, clean, null, null);
            if (!clean.isEmpty()) PcProfiles.syncActiveFromFlat(MainActivity.this);
            // The phone's Files app lists the cloud drives of the paired PC.
            if (changed) CloudDocumentsProvider.notifyRootsChanged(MainActivity.this);
            // Just paired (or another key): the PC can ring and ask this phone right away, not after the next wait.
            PeerLink.get(MainActivity.this).kick();
        }

        /** Away addresses (Tailscale) and the PC's id, from the PC's status: used when the home address does not answer. */
        @JavascriptInterface
        public void setServerCandidates(String json) {
            try {
                JSONObject o = new JSONObject(json);
                PcLink.setCandidates(MainActivity.this, o.optString("lan", ""), o.optJSONArray("away"), o.optString("pcId", ""), o.optString("deviceId", ""),
                        o.optJSONArray("direct"));
                PcProfiles.syncActiveFromFlat(MainActivity.this);
            } catch (Exception ignored) { }
        }

        // ---- several PCs: the page's list of saved devices (keys stay on this side) ----

        // ---- the PC looking at this phone (only as far as this phone allows) -------------------------------------

        /** {scope: shared|all, until (ms, 0 = until taken back)} or {} when that PC may not see this phone. */
        @JavascriptInterface
        public String peerState(String pcId) {
            return PeerLink.Grants.forPage(MainActivity.this, pcId).toString();
        }

        /** Lets that PC see this phone's files: scope shared|all, for minutes (0 = until taken back). */
        @JavascriptInterface
        public void peerGrant(String pcId, String scope, int minutes) {
            peerGrantWrite(pcId, scope, minutes, PeerLink.Grants.get(MainActivity.this, pcId) != null
                    && PeerLink.Grants.get(MainActivity.this, pcId).write);
        }

        /** The same, saying whether that PC may also add files and folders to this phone. */
        @JavascriptInterface
        public void peerGrantWrite(String pcId, String scope, int minutes, boolean write) {
            PeerLink.Grants.set(MainActivity.this, pcId, scope, minutes, write);
            android.app.NotificationManager nm = (android.app.NotificationManager) getSystemService(NOTIFICATION_SERVICE);
            if (nm != null) nm.cancel(PeerLink.NOTE_ID);
            // Keeps answering with the app closed for as long as it is allowed.
            PeerLinkService.ensure(MainActivity.this);
        }

        @JavascriptInterface
        public void peerRevoke(String pcId) {
            PeerLink.Grants.clear(MainActivity.this, pcId);
            PeerLinkService.stopIfNotNeeded(MainActivity.this);
        }

        /** The page is back online with its PC (paired, unblocked, another PC): ask it for questions right away. */
        @JavascriptInterface
        public void peerKick() {
            PeerLink.get(MainActivity.this).kick();
        }

        /** {on, listening (the background service runs), batteryOk (Android may not stop it to save battery)}. */
        @JavascriptInterface
        public String findMyPhone() {
            JSONObject o = new JSONObject();
            try {
                o.put("on", getSharedPreferences(PREFS, MODE_PRIVATE).getBoolean("find_my_phone", false));
                o.put("listening", PeerLinkService.isRunning());
                o.put("batteryOk", batteryUnrestricted());
                o.put("lockScreen", fullScreenAllowed());
            } catch (JSONException ignored) {
            }
            return o.toString();
        }

        /** "Find my phone": keep listening for the PC with the app closed (a quiet note shows while it does). */
        @JavascriptInterface
        public void setFindMyPhone(boolean on) {
            getSharedPreferences(PREFS, MODE_PRIVATE).edit().putBoolean("find_my_phone", on).remove("peer_paused").apply();
            if (on) {
                PeerLinkService.ensure(MainActivity.this);
                if (!batteryUnrestricted()) askBatteryUnrestricted();
            } else {
                PeerLinkService.stopIfNotNeeded(MainActivity.this);
            }
        }

        /** Android 14+: opens the setting that lets the ring show its "Found it" screen over the lock screen. */
        @JavascriptInterface
        public void askLockScreenRing() {
            if (Build.VERSION.SDK_INT < 34) return;
            runOnUiThread(() -> {
                try {
                    startActivity(new Intent("android.settings.MANAGE_APP_USE_FULL_SCREEN_INTENT", Uri.parse("package:" + getPackageName())));
                } catch (Exception e) {
                    toast("Settings → Apps → Cloud-SSD → Full screen notifications");
                }
            });
        }

        /** Asks Android (one system dialog) to let the app run in the background without being stopped. */
        @JavascriptInterface
        public void askBatteryExemption() {
            askBatteryUnrestricted();
        }

        /** Wakes a sleeping PC on this Wi-Fi (Wake-on-LAN) with the card and broadcast address it gave while it was on. */
        @JavascriptInterface
        public boolean wakePc(String mac, String broadcast) {
            // Called on the page's bridge thread, not the screen's, so the few small packets may go out right here.
            return WakeOnLan.send(mac, broadcast);
        }

        /** App lock: {on, after (seconds, 0 = right away), available (the phone has a screen lock)}. */
        @JavascriptInterface
        public String appLock() {
            JSONObject o = new JSONObject();
            try {
                o.put("on", AppLock.enabled(MainActivity.this)).put("after", AppLock.afterSeconds(MainActivity.this))
                        .put("available", AppLock.available(MainActivity.this));
            } catch (Exception ignored) {
            }
            return o.toString();
        }

        /** Switches App lock on (only with a screen lock on the phone) or off. False when it cannot be on. */
        @JavascriptInterface
        public boolean setAppLock(boolean on, int afterSeconds) {
            if (on && !AppLock.available(MainActivity.this)) return false;
            AppLock.set(MainActivity.this, on, afterSeconds);
            if (!on) runOnUiThread(() -> { if (appLock != null) appLock.disabled(); });
            return true;
        }

        /** Synced folders: [{id, pcId, pcFolder, pcLabel, phoneLabel, last: {at, up, down, conflicts, failed, ok, error}}]. */
        @JavascriptInterface
        public String folderSyncs() {
            return FolderSync.pairs(MainActivity.this).toString();
        }

        /** "Keep in sync with a phone folder": Android's folder picker opens; the pair then syncs once right away. */
        @JavascriptInterface
        public void folderSyncAdd(String pcFolder, String pcLabel) {
            pendingSyncFolder = pcFolder;
            pendingSyncLabel = pcLabel;
            runOnUiThread(() -> {
                Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE).addFlags(Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION
                        | Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
                try {
                    startActivityForResult(i, REQ_SYNC_TREE);
                } catch (Exception e) {
                    toast(Tr.t(MainActivity.this, "Could not sync that folder: {0}", String.valueOf(e.getMessage())));
                }
            });
        }

        /** The transfers as tasks, newest first (the page asks when it opens; then 'task' events follow). */
        @JavascriptInterface
        public String tasks() {
            return TransferHub.get(MainActivity.this).tasks().toString();
        }

        @JavascriptInterface
        public void taskCancel(String id) {
            TransferHub.get(MainActivity.this).taskCancel(id);
        }

        @JavascriptInterface
        public void taskRetry(String id) {
            TransferHub.get(MainActivity.this).taskRetry(id);
        }

        /** What the page knows about the next batch it starts (device, saveTo, kind…); taken by that batch. */
        @JavascriptInterface
        public void nextTask(String json) {
            try { nextTaskMeta = new JSONObject(json); } catch (Exception e) { nextTaskMeta = null; }
        }

        /** How the phone reaches the PC now (wifi, usb, hotspot, tailscale) and the PC's name, for the tasks made here. */
        @JavascriptInterface
        public void setLinkInfo(String kind, String pcName) {
            TransferHub hub = TransferHub.get(MainActivity.this);
            if (kind != null && !kind.isEmpty()) hub.linkKind = kind;
            if (pcName != null && !pcName.isEmpty()) hub.pcName = pcName;
        }

        /** Opens a folder inside the phone's storage ("Download/Cloud-SSD/Received/PC") in the Files app. */
        @JavascriptInterface
        public void openFolder(String path) {
            runOnUiThread(() -> {
                String p = path == null ? "" : path.replaceAll("^/+|/+$", "");
                try {
                    Uri u = DocumentsContract.buildDocumentUri("com.android.externalstorage.documents", "primary:" + p);
                    Intent i = new Intent(Intent.ACTION_VIEW).setDataAndType(u, DocumentsContract.Document.MIME_TYPE_DIR)
                            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                    startActivity(i);
                } catch (Exception e) {
                    try {
                        startActivity(new Intent(android.app.DownloadManager.ACTION_VIEW_DOWNLOADS));
                    } catch (Exception e2) {
                        toast(Tr.t(MainActivity.this, "Cannot share: {0}", String.valueOf(e2.getMessage())));
                    }
                }
            });
        }

        /** Picks a folder to send (a project, photos…); the page hears "folderPicked" with what is in it. */
        @JavascriptInterface
        public void pickFolderToSend(boolean everything) {
            sendFolderEverything = everything;
            runOnUiThread(() -> {
                Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE).addFlags(Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION
                        | Intent.FLAG_GRANT_READ_URI_PERMISSION);
                try {
                    startActivityForResult(i, REQ_SEND_TREE);
                } catch (Exception e) {
                    toast(Tr.t(MainActivity.this, "Cannot share: {0}", String.valueOf(e.getMessage())));
                }
            });
        }

        /**
         * Folders and files chosen in the Phone tab: {paths: [...], everything?}. Walked here (no picker); returns what
         * goes (count, bytes, folders at the top, items {name, size, folder} for an offer, what was left out).
         */
        @JavascriptInterface
        public String preparePaths(String json) {
            try {
                JSONObject o = new JSONObject(json);
                return walkPaths(o.getJSONArray("paths"), o.optBoolean("everything", false)).toString();
            } catch (Exception e) {
                try { return new JSONObject().put("error", String.valueOf(e.getMessage())).put("count", 0).toString(); } catch (Exception e2) { return "{}"; }
            }
        }

        /**
         * What preparePaths found goes to the PC as one task: each folder into the name the PC gave it ({"MyApp": "MyApp (2)"}),
         * loose files into its upload folder.
         */
        @JavascriptInterface
        public void sendPreparedToPc(String renamesJson) {
            sendPreparedToPc2(renamesJson, "[]", "keep");
        }

        /**
         * The same, after the page asked about names the PC already has: skip lists the folders (at the top) and loose
         * files to leave out; onClash "replace" has the PC's files of the same name give way to these.
         */
        @JavascriptInterface
        public void sendPreparedToPc2(String renamesJson, String skipJson, String onClash) {
            java.util.List<String[]> all = sendFolderFiles;
            if (all == null || all.isEmpty()) return;
            java.util.HashSet<String> skip = new java.util.HashSet<>();
            try { JSONArray a = new JSONArray(skipJson == null ? "[]" : skipJson); for (int i = 0; i < a.length(); i++) skip.add(a.optString(i)); } catch (Exception ignored) { }
            java.util.List<String[]> files = new ArrayList<>();
            for (String[] f : all) if (!skip.contains(f[3].isEmpty() ? f[1] : f[3].split("/")[0])) files.add(f);
            if (files.isEmpty()) return;
            TransferHub hub = TransferHub.get(MainActivity.this);
            JSONObject renames;
            try { renames = new JSONObject(renamesJson == null ? "{}" : renamesJson); } catch (Exception e) { renames = new JSONObject(); }
            java.util.LinkedHashSet<String> tops = new java.util.LinkedHashSet<>();
            boolean loose = false;
            for (String[] f : files) { if (f[3].isEmpty()) loose = true; else tops.add(f[3].split("/")[0]); }
            boolean oneFolder = tops.size() == 1 && !loose;
            String first = tops.isEmpty() ? "" : renames.optString(tops.iterator().next(), tops.iterator().next());
            startTask(meta("dir", "toPc", "kind", oneFolder ? "folder" : "files", "folder", oneFolder ? first : "",
                    "saveTo", hub.pcName + " › Uploads" + (oneFolder ? " › " + first : ""), "onClash", "replace".equals(onClash) ? "replace" : "keep"));
            try {
                for (String[] f : files) {
                    String target = "";
                    if (!f[3].isEmpty()) {
                        int slash = f[3].indexOf('/');
                        String top = slash < 0 ? f[3] : f[3].substring(0, slash);
                        target = "pcfolder:" + renames.optString(top, top) + (slash < 0 ? "" : f[3].substring(slash));
                    }
                    hub.enqueueUpload(f[1], Long.parseLong(f[2]), srcKindOf(f), f[0], target);
                }
            } finally {
                hub.endTask();
            }
        }

        /** The picked folder goes to the PC, into the folder it named for it ("MyApp" or "MyApp (2)"). */
        @JavascriptInterface
        public void sendFolderToPc(String top) {
            sendFolderToPc2(top, "keep");
        }

        /** The same; onClash "replace" when the PC has this folder already and its files are to give way. */
        @JavascriptInterface
        public void sendFolderToPc2(String top, String onClash) {
            java.util.List<String[]> files = sendFolderFiles;
            if (files == null || top == null || top.isEmpty()) return;
            TransferHub hub = TransferHub.get(MainActivity.this);
            startTask(meta("dir", "toPc", "kind", "folder", "folder", top, "saveTo", hub.pcName + " › Uploads › " + top,
                    "onClash", "replace".equals(onClash) ? "replace" : "keep"));
            try {
                for (String[] f : files) {
                    int slash = f[3].indexOf('/');
                    String place = top + (slash < 0 ? "" : f[3].substring(slash));
                    hub.enqueueUpload(f[1], Long.parseLong(f[2]), srcKindOf(f), f[0], "pcfolder:" + place);
                }
            } finally {
                hub.endTask();
            }
        }

        /** The picked folder goes to another device through the PC: the offer's item ids, in the order of the files. */
        @JavascriptInterface
        public void sendFolderToDevice(String offer, String idsJson) {
            java.util.List<String[]> files = sendFolderFiles;
            if (files == null) return;
            try {
                JSONArray ids = new JSONArray(idsJson);
                TransferHub hub = TransferHub.get(MainActivity.this);
                String top = files.isEmpty() ? "" : files.get(0)[3].split("/")[0];
                startTask(meta("id", "rl-" + offer, "dir", "toDevice", "kind", "folder", "folder", top, "link", "relay"));
                try {
                    for (int i = 0; i < files.size() && i < ids.length(); i++) {
                        String[] f = files.get(i);
                        hub.enqueueUpload(f[1], Long.parseLong(f[2]), srcKindOf(f), f[0], "share:" + offer + ":" + ids.optString(i));
                    }
                } finally {
                    hub.endTask();
                }
            } catch (Exception e) {
                toast("The files could not be sent");
            }
        }

        @JavascriptInterface
        public void folderSyncNow(String id) {
            worker.execute(() -> FolderSync.run(MainActivity.this, id, new java.util.concurrent.atomic.AtomicBoolean()));
        }

        @JavascriptInterface
        public void folderSyncRemove(String id) {
            FolderSync.remove(MainActivity.this, id);
        }

        /**
         * The page's look: the colour under the status bar (its top band) and under the navigation bar (its bottom bar),
         * as "#RRGGBB", and whether the bars' icons must be dark (on a light colour). Remembered for the next start.
         */
        @JavascriptInterface
        public void setBars(String top, String rest, boolean statusDark, boolean navDark) {
            final int t, r;
            try {
                t = android.graphics.Color.parseColor(top);
                r = android.graphics.Color.parseColor(rest);
            } catch (Exception e) {
                return;
            }
            getSharedPreferences(PREFS, MODE_PRIVATE).edit().putInt("bars_top", t).putInt("bars_rest", r)
                    .putBoolean("bars_status_dark", statusDark).putBoolean("bars_nav_dark", navDark).apply();
            runOnUiThread(() -> {
                if (bars != null) bars.setColors(t, r);
                applyBarIcons(statusDark, navDark);
            });
        }

        /** The language picked on the page (english, hinglish, hindi), so the phone's own notes use it too. */
        @JavascriptInterface
        public void setLanguage(String lang) {
            Tr.setLang(MainActivity.this, lang);
            PeerLinkService.relabel(MainActivity.this);
        }

        /** The text on this phone's clipboard ("" when none), for "Send clipboard to PC". */
        @JavascriptInterface
        public String getClipboard() {
            final String[] out = {""};
            final java.util.concurrent.CountDownLatch done = new java.util.concurrent.CountDownLatch(1);
            runOnUiThread(() -> {
                try {
                    android.content.ClipboardManager cm = (android.content.ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
                    if (cm != null && cm.hasPrimaryClip() && cm.getPrimaryClip().getItemCount() > 0) {
                        CharSequence t = cm.getPrimaryClip().getItemAt(0).coerceToText(MainActivity.this);
                        out[0] = t == null ? "" : t.toString();
                    }
                } catch (Exception ignored) {
                } finally {
                    done.countDown();
                }
            });
            try {
                done.await(2, java.util.concurrent.TimeUnit.SECONDS);
            } catch (InterruptedException ignored) {
            }
            return out[0];
        }

        /** This phone's direct links and the USB cable: {links: [{name, kind, ip}], usb: {connected, tethering}}. */
        @JavascriptInterface
        public String linkInfo() {
            return LinkScanner.forPage(MainActivity.this).toString();
        }

        /** Opens Android's tethering settings (to turn on USB tethering), or the network settings when there is none. */
        @JavascriptInterface
        public void openTetherSettings() {
            runOnUiThread(() -> {
                try {
                    startActivity(new Intent().setClassName("com.android.settings", "com.android.settings.TetherSettings").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                } catch (Exception e) {
                    try {
                        startActivity(new Intent(android.provider.Settings.ACTION_WIRELESS_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                    } catch (Exception ignored) {
                        toast("Open Settings → Hotspot & tethering → USB tethering");
                    }
                }
            });
        }

        /** Every saved PC/laptop: [{id, name, label, kind, lan, away, last, deviceId, features, lastOnline, active, hasKey}]. */
        @JavascriptInterface
        public String pcList() {
            return PcProfiles.forPage(MainActivity.this).toString();
        }

        /** Adds or updates one: {id, name?, label?, kind?, lan?, away?, last?, deviceId?, lastOnline?, token?, activate?}. */
        @JavascriptInterface
        public boolean pcSave(String json) {
            try {
                JSONObject o = new JSONObject(json);
                String token = o.has("token") ? o.optString("token", "") : null;
                if (o.has("replaces")) PcProfiles.rekey(MainActivity.this, o.optString("replaces"), o.optString("id"));
                return PcProfiles.upsert(MainActivity.this, o, token, o.optBoolean("activate", false));
            } catch (Exception e) {
                return false;
            }
        }

        /** Switches to another saved PC: {ok, server, token, id}, or "" when it is not saved. */
        @JavascriptInterface
        public String pcActivate(String id) {
            JSONObject r = PcProfiles.activate(MainActivity.this, id);
            if (r != null) serverBaseUrl = r.optString("server", serverBaseUrl);
            // The PC now in use is asked at once (the wait open to the previous one is ended).
            if (r != null) PeerLink.get(MainActivity.this).kick();
            return r == null ? "" : r.toString();
        }

        /** Forgets a saved PC and its key on this phone (the page tells that PC first when it can). */
        @JavascriptInterface
        public boolean pcRemove(String id) {
            boolean removed = PcProfiles.remove(MainActivity.this, id);
            // Nothing of a forgotten PC stays on this phone: its offline copies and the lists the Files app kept.
            if (removed) {
                OfflinePins.get(MainActivity.this).forgetPc(id);
                ListingCache.forgetPc(MainActivity.this, id);
                FolderSync.forgetPc(MainActivity.this, id);
            }
            return removed;
        }

        /** This phone's name as its owner set it (shown on the PCs it pairs with). */
        @JavascriptInterface
        public void setMeName(String name) {
            String clean = name == null ? "" : name.trim();
            if (clean.length() > 60) clean = clean.substring(0, 60);
            getSharedPreferences(PREFS, MODE_PRIVATE).edit().putString("me_name", clean).apply();
        }

        /** What the PC can do and how it stores backups (saved for the Files app and camera backup). */
        @JavascriptInterface
        public void setPcInfo(String json) {
            try {
                JSONObject o = new JSONObject(json);
                SharedPreferences.Editor e = getSharedPreferences(PREFS, MODE_PRIVATE).edit();
                if (o.has("features")) e.putString("features", o.optJSONArray("features").toString());
                if (o.has("backupCustom")) e.putBoolean("backup_custom", o.optBoolean("backupCustom"));
                e.apply();
            } catch (Exception ignored) { }
        }

        /** A cloudssd://pair link the app was opened with (JSON {host, token|pair}), handed over once. */
        @JavascriptInterface
        public String consumePairLink() {
            String link = pendingPairLink;
            pendingPairLink = "";
            return link;
        }

        /** The files shared to "Send to a device…" that wait for a place: [{name, size}] (size -1 when not known). */
        @JavascriptInterface
        public String shareInFiles() {
            JSONArray a = new JSONArray();
            List<Uri> uris;
            synchronized (shareIn) {
                uris = new ArrayList<>(shareIn);
            }
            for (Uri u : uris) {
                Object[] ns = nameAndSize(u);
                try {
                    a.put(new JSONObject().put("name", ns[0]).put("size", ns[1]));
                } catch (JSONException ignored) {
                }
            }
            return a.toString();
        }

        /** They go to the PC in use (into its "From phone" folder; they wait when it is away). */
        @JavascriptInterface
        public void shareInToPc() {
            final List<Uri> uris = takeShareIn();
            worker.execute(() -> uploadUris(uris, "", false));
        }

        /** They go to another device through the PC: one offer item id per file, in the same order. */
        @JavascriptInterface
        public void shareInToDevice(final String offerId, String itemIdsJson) {
            final List<Uri> uris = takeShareIn();
            try {
                final JSONArray ids = new JSONArray(itemIdsJson);
                final JSONObject told = nextTaskMeta;
                nextTaskMeta = null;
                worker.execute(() -> {
                    nextTaskMeta = told;
                    startTask(meta("id", "rl-" + offerId, "dir", "toDevice", "link", "relay"));
                    try {
                        for (int i = 0; i < uris.size() && i < ids.length(); i++) uploadUri(uris.get(i), "share:" + offerId + ":" + ids.optString(i), false);
                    } finally {
                        TransferHub.get(MainActivity.this).endTask();
                    }
                });
            } catch (JSONException e) {
                toast("The files could not be sent");
            }
        }

        /** The share was cancelled in the page: forget the files. */
        @JavascriptInterface
        public void shareInCancel() {
            takeShareIn();
        }

        /** True once after the "listening" note was tapped: the page then shows Settings → Find my phone. */
        @JavascriptInterface
        public boolean consumeFindMyPhone() {
            boolean was = pendingFindMyPhone;
            pendingFindMyPhone = false;
            return was;
        }

        /** Looks for Cloud-SSD PCs on this Wi-Fi/hotspot; results arrive as 'discovery' events. */
        @JavascriptInterface
        public void discoverPcs(final int port) {
            if (!discovering.compareAndSet(false, true)) return;
            worker.execute(() -> {
                try {
                    runDiscovery(port > 0 ? port : 8090);
                } finally {
                    discovering.set(false);
                }
            });
        }

        @JavascriptInterface
        public String getPhoneStats() {
            JSONObject obj = new JSONObject();
            try {
                long[] st = storageTotals();
                obj.put("storageTotal", st[0]);
                obj.put("storageFree", st[1]);
                obj.put("storageUsed", st[0] - st[1]);

                ActivityManager am = (ActivityManager) getSystemService(Context.ACTIVITY_SERVICE);
                ActivityManager.MemoryInfo mi = new ActivityManager.MemoryInfo();
                if (am != null) {
                    am.getMemoryInfo(mi);
                    obj.put("ramTotal", mi.totalMem);
                    obj.put("ramFree", mi.availMem);
                    obj.put("ramUsed", mi.totalMem - mi.availMem);
                    obj.put("lowMemory", mi.lowMemory);
                }
                obj.put("model", Build.MODEL);
                obj.put("manufacturer", Build.MANUFACTURER);
                obj.put("androidVersion", Build.VERSION.RELEASE);
                obj.put("storageAccess", storageAccess());
            } catch (Exception ignored) { }
            return obj.toString();
        }

        /**
         * Where the phone's memory is right now, straight from the kernel (/proc/meminfo). Android does not let one app
         * read another app's memory, so this is by kind (apps, cached files, kernel, free), plus this app's own share.
         */
        @JavascriptInterface
        public String getPhoneRamBreakdown() {
            JSONObject obj = new JSONObject();
            try {
                Map<String, Long> m = new HashMap<>();
                try (BufferedReader br = new BufferedReader(new FileReader("/proc/meminfo"))) {
                    String line;
                    while ((line = br.readLine()) != null) {
                        int colon = line.indexOf(':');
                        if (colon <= 0) continue;
                        String[] v = line.substring(colon + 1).trim().split("\\s+");
                        try {
                            m.put(line.substring(0, colon).trim(), Long.parseLong(v[0]) * 1024L);
                        } catch (NumberFormatException ignored) { }
                    }
                } catch (Exception e) {
                    obj.put("meminfoError", String.valueOf(e.getMessage()));
                }

                ActivityManager am = (ActivityManager) getSystemService(Context.ACTIVITY_SERVICE);
                ActivityManager.MemoryInfo mi = new ActivityManager.MemoryInfo();
                if (am != null) am.getMemoryInfo(mi);

                long total = m.containsKey("MemTotal") ? m.get("MemTotal") : mi.totalMem;
                long available = m.containsKey("MemAvailable") ? m.get("MemAvailable") : mi.availMem;
                obj.put("totalBytes", total);
                obj.put("availableBytes", available);
                obj.put("usedBytes", Math.max(0, total - available));
                obj.put("lowMemory", mi.lowMemory);
                obj.put("detailed", m.containsKey("MemTotal"));

                if (m.containsKey("MemTotal")) {
                    long free = get(m, "MemFree");
                    long apps = get(m, "AnonPages");
                    long cached = get(m, "Cached") + get(m, "Buffers") + get(m, "SwapCached");
                    long kernel = get(m, "Slab") + get(m, "KernelStack") + get(m, "PageTables");
                    long other = Math.max(0, total - free - apps - cached - kernel);
                    obj.put("freeBytes", free);
                    obj.put("appsBytes", apps);
                    obj.put("cachedBytes", cached);
                    obj.put("kernelBytes", kernel);
                    obj.put("otherBytes", other);
                    long swapTotal = get(m, "SwapTotal");
                    obj.put("swapTotalBytes", swapTotal);
                    obj.put("swapUsedBytes", Math.max(0, swapTotal - get(m, "SwapFree")));
                }

                // Processes Android lets this app see (on modern Android: only itself), with their real memory (PSS).
                JSONArray procs = new JSONArray();
                if (am != null) {
                    List<ActivityManager.RunningAppProcessInfo> list = am.getRunningAppProcesses();
                    if (list != null && !list.isEmpty()) {
                        int n = Math.min(list.size(), 30);
                        int[] pids = new int[n];
                        for (int i = 0; i < n; i++) pids[i] = list.get(i).pid;
                        Debug.MemoryInfo[] infos = am.getProcessMemoryInfo(pids);
                        PackageManager pm = getPackageManager();
                        for (int i = 0; i < n; i++) {
                            long pss = infos != null && i < infos.length ? infos[i].getTotalPss() * 1024L : 0;
                            if (pss <= 0) continue;
                            String pkg = list.get(i).processName;
                            String label = pkg;
                            try {
                                label = String.valueOf(pm.getApplicationLabel(pm.getApplicationInfo(pkg.split(":")[0], 0)));
                            } catch (Exception ignored) { }
                            JSONObject p = new JSONObject();
                            p.put("name", label);
                            p.put("pkg", pkg);
                            p.put("bytes", pss);
                            procs.put(p);
                        }
                    }
                }
                obj.put("processes", procs);
            } catch (Exception ex) {
                try { obj.put("error", String.valueOf(ex.getMessage())); } catch (Exception ignored) { }
            }
            return obj.toString();
        }

        @JavascriptInterface
        public String getStorageAccess() {
            return storageAccess();
        }

        /** Photos/videos/audio + notifications (the normal permission dialog). */
        @JavascriptInterface
        public void requestAllPermissions() {
            runOnUiThread(() -> {
                if (Build.VERSION.SDK_INT >= 33) {
                    requestPermissions(new String[]{
                            "android.permission.READ_MEDIA_IMAGES", "android.permission.READ_MEDIA_VIDEO",
                            "android.permission.READ_MEDIA_AUDIO", "android.permission.POST_NOTIFICATIONS"}, REQ_PERMS);
                } else if (Build.VERSION.SDK_INT >= 23) {
                    requestPermissions(new String[]{
                            "android.permission.READ_EXTERNAL_STORAGE", "android.permission.WRITE_EXTERNAL_STORAGE"}, REQ_PERMS);
                }
            });
        }

        /** Opens Android's "All files access" switch for this app: needed to show documents, APKs and every folder. */
        @JavascriptInterface
        public void requestAllFilesAccess() {
            runOnUiThread(() -> {
                if (Build.VERSION.SDK_INT < 30) {
                    requestAllPermissions();
                    return;
                }
                try {
                    startActivity(new Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION, Uri.parse("package:" + getPackageName())));
                } catch (Exception e) {
                    try {
                        startActivity(new Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION));
                    } catch (Exception e2) {
                        toast("Open Settings > Apps > Cloud-SSD > Permissions > Files");
                    }
                }
            });
        }

        /** What Android lets this app do, for the Permissions page: {files: all|media|none, notifications, background}. */
        @JavascriptInterface
        public String permState() {
            JSONObject o = new JSONObject();
            try {
                o.put("files", storageAccess());
                o.put("notifications", Build.VERSION.SDK_INT < 33 || hasPermission("android.permission.POST_NOTIFICATIONS"));
                o.put("background", batteryUnrestricted());
            } catch (Exception ignored) { }
            return o.toString();
        }

        /** Opens this app's page in Android's Settings: every permission of the app is there. */
        @JavascriptInterface
        public void openAppSettings() {
            runOnUiThread(() -> {
                try {
                    startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + getPackageName())));
                } catch (Exception e) {
                    toast("Open Settings > Apps > Cloud-SSD");
                }
            });
        }

        /** The app's icon on the home screen now: one of the keys of APP_ICONS ("ocean" when none was picked). */
        @JavascriptInterface
        public String appIcon() {
            PackageManager pm = getPackageManager();
            for (String[] icon : APP_ICONS) {
                int state = pm.getComponentEnabledSetting(iconAlias(icon[1]));
                if (state == PackageManager.COMPONENT_ENABLED_STATE_ENABLED) return icon[0];
            }
            return APP_ICONS[0][0];
        }

        /**
         * Changes the app's icon on the home screen: the alias of the picked icon is switched on and the others off
         * (the launcher shows the new one a few seconds later). False for a key that is not an icon.
         */
        @JavascriptInterface
        public boolean setAppIcon(String key) {
            String[] picked = null;
            for (String[] icon : APP_ICONS) if (icon[0].equals(key)) picked = icon;
            if (picked == null) return false;
            try {
                PackageManager pm = getPackageManager();
                // On first, then the others off: there is no moment without an icon.
                pm.setComponentEnabledSetting(iconAlias(picked[1]), PackageManager.COMPONENT_ENABLED_STATE_ENABLED, PackageManager.DONT_KILL_APP);
                for (String[] icon : APP_ICONS) {
                    if (icon == picked) continue;
                    pm.setComponentEnabledSetting(iconAlias(icon[1]), PackageManager.COMPONENT_ENABLED_STATE_DISABLED, PackageManager.DONT_KILL_APP);
                }
                return true;
            } catch (Exception e) {
                return false;
            }
        }

        @JavascriptInterface
        public boolean hasStoragePermission() {
            return !"none".equals(storageAccess());
        }

        /** Sizes of everything on the phone by kind; the result arrives as a 'storage' event. */
        @JavascriptInterface
        public void scanStorage() {
            worker.execute(() -> emit("storage", buildStorageReport()));
        }

        /** Folder listing; arrives as a 'list' event carrying reqId. */
        @JavascriptInterface
        public void listDir(final int reqId, final String path) {
            worker.execute(() -> emit("list", withReq(reqId, listDirectory(path))));
        }

        /** downloads | images | videos | audio | documents | apps; arrives as a 'list' event carrying reqId. */
        @JavascriptInterface
        public void listCategory(final int reqId, final String category) {
            worker.execute(() -> emit("list", withReq(reqId, listCategoryFiles(category))));
        }

        @JavascriptInterface
        public void searchPhone(final int reqId, final String query) {
            worker.execute(() -> emit("list", withReq(reqId, searchFiles(query))));
        }

        // ---- transfers (TransferHub keeps them going in the background) ----------------------------------------

        @JavascriptInterface
        public void uploadFileByPath(final String localFilePath) {
            if (localFilePath == null || localFilePath.trim().isEmpty()) return;
            final File file = new File(localFilePath);
            if (!file.isFile()) {
                toast(Tr.t(MainActivity.this, "File not found: {0}", file.getName()));
                return;
            }
            TransferHub.get(MainActivity.this).enqueueUpload(file.getName(), file.length(), "file", file.getAbsolutePath(), "");
        }

        /** "Upload here": the files picked next go into this cloud folder ("&lt;drive&gt;:&lt;folder&gt;") instead of the PC's upload folder. */
        @JavascriptInterface
        public void setUploadTarget(String folderId) {
            uploadTarget = folderId == null ? "" : folderId.trim();
        }

        @JavascriptInterface
        public void pickAndUploadFiles(final String type) {
            runOnUiThread(() -> {
                // OPEN_DOCUMENT (not GET_CONTENT): the app may keep reading the files, so Retry works after it was closed.
                Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
                intent.addCategory(Intent.CATEGORY_OPENABLE);
                intent.setType("*/*");
                if ("photos".equalsIgnoreCase(type) || "images".equalsIgnoreCase(type))
                    intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[]{"image/*", "video/*"});
                intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
                try {
                    startActivityForResult(intent, REQ_PICK_UPLOAD);
                } catch (Exception e) {
                    toast(Tr.t(MainActivity.this, "Cannot open file picker: {0}", e.getMessage()));
                }
            });
        }

        /** Saves a PC file into Downloads. The URL may carry the key; only its route is kept, the hub adds the key itself. */
        @JavascriptInterface
        public void downloadFile(final String downloadUrl, final String rawFileName) {
            String name = rawFileName == null || rawFileName.trim().isEmpty() ? "download_" + System.currentTimeMillis() : rawFileName.trim();
            if (downloadUrl != null && downloadUrl.startsWith("cloud:")) TransferHub.get(MainActivity.this).enqueueDownload(downloadUrl, name, -1);
            else TransferHub.get(MainActivity.this).enqueueDownload(routeOf(downloadUrl), name, -1);
        }

        /** Files another device or the PC sent to this phone: {folder, files: [{url, name, size}]} into Downloads/folder. */
        @JavascriptInterface
        public void receiveFiles(String json) {
            try {
                JSONObject o = new JSONObject(json);
                String folder = o.optString("folder", "");
                JSONArray a = o.getJSONArray("files");
                // One task for the offer (the page tells who sent it, where it goes, how it came).
                JSONObject t = o.optJSONObject("task");
                startTask(t != null ? t : meta("dir", "toPhone", "saveTo", "Downloads › " + folder.replace("/", " › "), "openPath", "Download/" + folder));
                try {
                    for (int i = 0; i < a.length(); i++) {
                        JSONObject f = a.getJSONObject(i);
                        // A file of a sent folder (a project…) has its own place: "Cloud-SSD/Received/PC/MyApp/src".
                        TransferHub.get(MainActivity.this).enqueueDownload(routeOf(f.optString("url")), f.optString("name", "file"), f.optLong("size", -1),
                                f.optString("folder", folder));
                    }
                } finally {
                    TransferHub.get(MainActivity.this).endTask();
                }
            } catch (Exception e) {
                toast("The files could not be saved");
            }
        }

        /**
         * "MyApp" when Downloads/parent has nothing of that name yet, else "MyApp (2)", "MyApp (3)"…: a second copy of a
         * sent folder goes next to the first instead of mixing its files in.
         */
        @JavascriptInterface
        public String freeFolderName(String parent, String name) {
            String base = TransferHub.cleanFolder(name == null ? "" : name.replace('/', '_'));
            if (base.isEmpty()) base = "Folder";
            String p = TransferHub.cleanFolder(parent);
            for (int i = 1; i < 1000; i++) {
                String n = i == 1 ? base : base + " (" + i + ")";
                if (!folderTaken(p.isEmpty() ? n : p + "/" + n)) return n;
            }
            return base + " (" + System.currentTimeMillis() + ")";
        }

        private boolean folderTaken(String folder) {
            return TransferHub.get(MainActivity.this).folderTaken(folder);
        }

        /** Phone files for another device, through the PC: {offer, items: [{id, path}]} (the offer was made by the page). */
        @JavascriptInterface
        public void sendToDevice(String json) {
            try {
                JSONObject o = new JSONObject(json);
                String offer = o.getString("offer");
                JSONArray a = o.getJSONArray("items");
                startTask(meta("id", "rl-" + offer, "dir", "toDevice", "link", "relay"));
                try {
                    for (int i = 0; i < a.length(); i++) {
                        JSONObject it = a.getJSONObject(i);
                        File f = new File(it.getString("path"));
                        if (!f.isFile()) continue;
                        TransferHub.get(MainActivity.this).enqueueUpload(f.getName(), f.length(), "file", f.getAbsolutePath(), "share:" + offer + ":" + it.getString("id"));
                    }
                } finally {
                    TransferHub.get(MainActivity.this).endTask();
                }
            } catch (Exception e) {
                toast("The files could not be sent");
            }
        }

        /** Several downloads at once: [{url, name, size}]. */
        @JavascriptInterface
        public void downloadFiles(String json) {
            try {
                JSONArray a = new JSONArray(json);
                for (int i = 0; i < a.length(); i++) {
                    JSONObject o = a.getJSONObject(i);
                    String url = o.optString("url");
                    TransferHub.get(MainActivity.this).enqueueDownload(url.startsWith("cloud:") ? url : routeOf(url), o.optString("name", "download"), o.optLong("size", -1));
                }
            } catch (Exception e) {
                toast("Download could not start");
            }
        }

        @JavascriptInterface
        public void cancelTransfer(int id) {
            TransferHub.get(MainActivity.this).cancel(id);
        }

        @JavascriptInterface
        public void retryTransfer(int id) {
            TransferHub.get(MainActivity.this).retry(id);
        }

        @JavascriptInterface
        public void dismissTransfer(int id) {
            TransferHub.get(MainActivity.this).dismiss(id);
        }

        /** The PC answered again (or another PC was chosen): what waited for it goes now. */
        @JavascriptInterface
        public void resumeHeldTransfers() {
            TransferHub.get(MainActivity.this).resumeHeld();
        }

        // ---- "Offline rakho": folders and files of a PC kept on this phone ---------------------------------------

        /** Keep a folder (with all inside) or a file of that PC on this phone. Returns "" or why not. */
        @JavascriptInterface
        public String offlinePin(String pcId, String id, String name, boolean dir) {
            return OfflinePins.get(MainActivity.this).pin(pcId, id, name, dir);
        }

        @JavascriptInterface
        public void offlineUnpin(String pcId, String id) {
            OfflinePins.get(MainActivity.this).unpin(pcId, id);
        }

        /** {pins, files: {id: [size, modified]}, busy, mobileData, used} for that PC. */
        @JavascriptInterface
        public String offlineState(String pcId) {
            try {
                return OfflinePins.get(MainActivity.this).state(pcId).toString();
            } catch (Exception e) {
                return "{}";
            }
        }

        /** The kept copy of a file as a path on this phone (for the PDF reader), or "". */
        @JavascriptInterface
        public String offlinePath(String pcId, String id) {
            File f = OfflinePins.get(MainActivity.this).local(pcId, id);
            return f == null ? "" : f.getAbsolutePath();
        }

        /** Copies the kept copy of a file into Downloads/Cloud-SSD (works with the PC away); a toast says how it went. */
        @JavascriptInterface
        public void offlineSaveToPhone(final String pcId, final String id, final String name) {
            final File src = OfflinePins.get(MainActivity.this).local(pcId, id);
            if (src == null || !src.isFile()) {
                toast("That file is not kept on this phone any more");
                return;
            }
            worker.execute(() -> {
                String clean = (name == null || name.trim().isEmpty() ? src.getName() : name.trim()).replaceAll("[\\\\/:*?\"<>|]", "_");
                try (InputStream in = new FileInputStream(src); OutputStream out = openInDownloads(clean)) {
                    byte[] buf = new byte[64 * 1024];
                    for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
                    toast(Tr.t(MainActivity.this, "Saved to Downloads/Cloud-SSD: {0}", clean));
                } catch (Exception e) {
                    toast(Tr.t(MainActivity.this, "Could not save {0}: {1}", clean, e.getMessage()));
                }
            });
        }

        /** Android's share sheet for a cloud file, through the app's Files provider (a kept copy works offline). */
        @JavascriptInterface
        public void cloudShare(final String id, final String fileName) {
            runOnUiThread(() -> {
                try {
                    Uri uri = DocumentsContract.buildDocumentUri(CloudDocumentsProvider.AUTHORITY, id);
                    Intent send = new Intent(Intent.ACTION_SEND).setType(mimeOf(fileName)).putExtra(Intent.EXTRA_STREAM, uri)
                            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                    startActivity(Intent.createChooser(send, Tr.t(MainActivity.this, "Share {0}", fileName)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                } catch (Exception e) {
                    toast(Tr.t(MainActivity.this, "Cannot share: {0}", e.getMessage()));
                }
            });
        }

        /** Brings the kept folders of the PC in use up to date (in the background). */
        @JavascriptInterface
        public void offlineSync() {
            OfflinePins.get(MainActivity.this).kick();
        }

        @JavascriptInterface
        public void offlineMobileData(boolean on) {
            OfflinePins.get(MainActivity.this).setMobileData(on);
        }

        /** Everything queued, running, waiting or failed (and what just finished), so the page can show it after a restart. */
        @JavascriptInterface
        public String listTransfers() {
            return TransferHub.get(MainActivity.this).events().toString();
        }

        @JavascriptInterface
        public void copyText(String text) {
            runOnUiThread(() -> {
                ClipboardManager cm = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
                if (cm != null) cm.setPrimaryClip(ClipData.newPlainText("Cloud-SSD", text));
            });
        }

        // ---- camera backup -----------------------------------------------------------------------------------

        /** Camera backup status for the Home card (JSON, see CameraBackup.statusJson). */
        @JavascriptInterface
        public String getCameraBackup() {
            return CameraBackup.statusJson(MainActivity.this);
        }

        /** Turns camera backup on (and starts it) or off. False when the app may not see photos yet. */
        @JavascriptInterface
        public boolean setCameraBackup(boolean on) {
            if (on && "none".equals(storageAccess())) return false;
            // Lets the backup keep the place a photo was taken; Android grants it together with photo access.
            if (on && Build.VERSION.SDK_INT >= 29 && !hasPermission("android.permission.ACCESS_MEDIA_LOCATION"))
                runOnUiThread(() -> requestPermissions(new String[]{"android.permission.ACCESS_MEDIA_LOCATION"}, 4321));
            CameraBackup.setEnabled(MainActivity.this, on);
            if (on) CameraBackup.runNow(MainActivity.this);
            return true;
        }

        @JavascriptInterface
        public void backupNow() {
            CameraBackup.runNow(MainActivity.this);
        }

        /** Photo/video folders on the phone; arrives as a 'mediaFolders' event. */
        @JavascriptInterface
        public void listMediaFolders() {
            worker.execute(() -> emit("mediaFolders", CameraBackup.mediaFolders(MainActivity.this)));
        }

        @JavascriptInterface
        public void setBackupFolders(String json) {
            try {
                CameraBackup.setFolders(MainActivity.this, new JSONArray(json));
            } catch (Exception ignored) { }
        }

        /** {requireCharging, requireBatteryNotLow, network: "wifi"|"any", directToDrive, deviceName} */
        @JavascriptInterface
        public void setBackupOptions(String json) {
            try {
                CameraBackup.setOptions(MainActivity.this, new JSONObject(json));
            } catch (Exception ignored) { }
        }

        // ---- opening files -------------------------------------------------------------------------------------

        /** Opens a PC file in another app, e.g. a video player. The URL is a signed link (no pairing key in it). */
        @JavascriptInterface
        public void openWith(final String fileUrl, final String mimeType, final String fileName) {
            runOnUiThread(() -> {
                try {
                    if (fileUrl != null && fileUrl.contains("token=")) {
                        toast("Update Cloud-SSD on the PC to open files in other apps safely.");
                        return;
                    }
                    Intent intent = new Intent(Intent.ACTION_VIEW);
                    String type = (mimeType != null && !mimeType.isEmpty() && !mimeType.equals("application/octet-stream")) ? mimeType : "*/*";
                    intent.setDataAndType(Uri.parse(fileUrl), type);
                    intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                    startActivity(Intent.createChooser(intent, Tr.t(MainActivity.this, "Open {0} with", fileName != null ? fileName : Tr.t(MainActivity.this, "the file"))));
                } catch (Exception e) {
                    toast(Tr.t(MainActivity.this, "Cannot open: {0}", e.getMessage()));
                }
            });
        }

        /** Opens a cloud item (id) in another app through the Files-app provider, which streams it. */
        @JavascriptInterface
        public void cloudOpenWith(final String id, final String fileName) {
            runOnUiThread(() -> {
                try {
                    Uri uri = DocumentsContract.buildDocumentUri(CloudDocumentsProvider.AUTHORITY, id);
                    Intent intent = new Intent(Intent.ACTION_VIEW);
                    intent.setDataAndType(uri, mimeOf(fileName));
                    intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_GRANT_READ_URI_PERMISSION);
                    startActivity(Intent.createChooser(intent, Tr.t(MainActivity.this, "Open {0} with", fileName)));
                } catch (Exception e) {
                    toast(Tr.t(MainActivity.this, "Cannot open: {0}", e.getMessage()));
                }
            });
        }

        /** Opens a file that is on the phone in another app. */
        @JavascriptInterface
        public void openLocalFile(final String path) {
            runOnUiThread(() -> {
                try {
                    File f = new File(path);
                    if (!f.exists()) {
                        toast(Tr.t(MainActivity.this, "File not found: {0}", f.getName()));
                        return;
                    }
                    Uri uri = contentUriFor(f);
                    if (uri == null) {
                        toast("Android has not indexed this file yet; try again in a moment.");
                        return;
                    }
                    Intent intent = new Intent(Intent.ACTION_VIEW);
                    intent.setDataAndType(uri, mimeOf(f.getName()));
                    intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_GRANT_READ_URI_PERMISSION);
                    startActivity(Intent.createChooser(intent, Tr.t(MainActivity.this, "Open {0} with", f.getName())));
                } catch (Exception e) {
                    toast(Tr.t(MainActivity.this, "Cannot open: {0}", e.getMessage()));
                }
            });
        }

        /** source: a phone path, "pc:" + route, or "cloud:" + id; arrives as 'pdf' events. */
        @JavascriptInterface
        public void openPdf(final int reqId, final String source, final String name, final long size) {
            final String src = source != null && source.startsWith("pc:") ? "pc:" + routeOf(source.substring(3)) : source;
            worker.execute(() -> PdfPages.openPdf(MainActivity.this, reqId, src, size, ev -> emit("pdf", ev)));
        }

        @JavascriptInterface
        public void closePdf(String key) {
            PdfPages.close(key);
        }

        // ---- Google Drive without the PC -----------------------------------------------------------------------

        /** {available, direct, accounts: [{email, folder, needsReauth, totalBytes, freeBytes}], source} */
        @JavascriptInterface
        public String driveStatus() {
            try {
                return new JSONObject().put("available", GoogleAuth.available(MainActivity.this))
                        .put("direct", getSharedPreferences(PREFS, MODE_PRIVATE).getBoolean("drive_direct", true))
                        .put("accounts", GoogleAuth.accounts(MainActivity.this)).toString();
            } catch (Exception e) {
                return "{}";
            }
        }

        @JavascriptInterface
        public void driveAddAccount() {
            runOnUiThread(() -> {
                try {
                    GoogleAuth.begin(MainActivity.this, r -> emit("drive", r));
                } catch (IOException e) {
                    try {
                        emit("drive", new JSONObject().put("ok", false).put("error", e.getMessage()));
                    } catch (Exception ignored) { }
                }
            });
        }

        @JavascriptInterface
        public void driveRemoveAccount(String email) {
            GoogleAuth.signOut(MainActivity.this, email);
            CloudDocumentsProvider.notifyRootsChanged(MainActivity.this);
            try {
                emit("drive", new JSONObject().put("ok", true).put("removed", email));
            } catch (Exception ignored) { }
        }

        @JavascriptInterface
        public void setDriveDirect(boolean on) {
            getSharedPreferences(PREFS, MODE_PRIVATE).edit().putBoolean("drive_direct", on).apply();
            CloudDocumentsProvider.notifyRootsChanged(MainActivity.this);
        }

        /**
         * "Files when it is off": the page tells how this phone and a PC stand (both said yes or not), so the PC's copy
         * in the cloud can be opened while the PC cannot be asked. active = false forgets the PC.
         */
        @JavascriptInterface
        public void whenOffSync(String pcId, String name, boolean active) {
            if (WhenOffStore.set(MainActivity.this, pcId, name, active)) CloudDocumentsProvider.notifyRootsChanged(MainActivity.this);
        }

        /** The Google key a PC handed over for this phone's own sign-in (never a sign-in itself). True when it is new. */
        @JavascriptInterface
        public boolean setGoogleKey(String clientId, String clientSecret) {
            boolean fresh = GoogleAuth.setKey(MainActivity.this, clientId, clientSecret);
            if (fresh) CloudDocumentsProvider.notifyRootsChanged(MainActivity.this);
            return fresh;
        }

        /** Files on the cloud by drive id, from the PC or Google Drive (whichever answers); arrives as a 'cloud' event. */
        @JavascriptInterface
        public void cloudOp(final int reqId, final String op, final String argsJson) {
            worker.execute(() -> emit("cloud", runCloudOp(reqId, op, argsJson)));
        }

        // ---- Space & RAM ---------------------------------------------------------------------------------------

        /** what: summary | folder (arg = path) | apps | ram | cleanups | large; arrives as a 'space' event. */
        @JavascriptInterface
        public void spaceGet(final int reqId, final String what, final String arg) {
            worker.execute(() -> {
                JSONObject r = new JSONObject();
                try {
                    r.put("reqId", reqId).put("what", what);
                    if (!"ram".equals(what)) SpaceScanner.ensureFresh(5 * 60_000L);
                    Object data;
                    switch (what) {
                        case "summary": data = SpaceScanner.summary(MainActivity.this, buildStorageReport(), new JSONObject(getPhoneRamBreakdown()), deviceName()); break;
                        case "folder": data = SpaceScanner.folder(arg); break;
                        case "apps": data = SpaceScanner.apps(MainActivity.this); break;
                        case "ram": data = new JSONObject().put("ram", SpaceScanner.summary(MainActivity.this, new JSONObject(), new JSONObject(getPhoneRamBreakdown()), deviceName()).get("ram"))
                                .put("apps", SpaceScanner.ramApps(MainActivity.this)); break;
                        case "cleanups": data = SpaceScanner.cleanups(MainActivity.this); break;
                        case "large": data = SpaceScanner.large(); break;
                        default: throw new IllegalArgumentException("unknown " + what);
                    }
                    r.put("ok", true).put("data", data);
                } catch (Exception e) {
                    try { r.put("ok", false).put("error", String.valueOf(e.getMessage())); } catch (Exception ignored) { }
                }
                emit("space", r);
            });
        }

        /** One ⋮ action on this phone; the result arrives as a 'spaceResult' event {reqId, ok, message, freed}. */
        @JavascriptInterface
        public void spaceAction(final int reqId, final String kind, final String target) {
            worker.execute(() -> runSpaceAction(kind, target, (ok, message, freed) -> {
                try {
                    emit("spaceResult", new JSONObject().put("reqId", reqId).put("ok", ok).put("message", message).put("freed", freed));
                } catch (Exception ignored) { }
            }));
        }

        /** {share, control, usageAccess}: what the PC may see and do with this phone. */
        @JavascriptInterface
        public String getSpaceSharing() {
            SharedPreferences p = getSharedPreferences(PREFS, MODE_PRIVATE);
            try {
                return new JSONObject().put("share", p.getBoolean("space_share", true)).put("control", p.getBoolean("space_control", true))
                        .put("usageAccess", SpaceScanner.hasUsageAccess(MainActivity.this)).toString();
            } catch (Exception e) {
                return "{}";
            }
        }

        @JavascriptInterface
        public void setSpaceSharing(String json) {
            try {
                JSONObject o = new JSONObject(json);
                SharedPreferences.Editor e = getSharedPreferences(PREFS, MODE_PRIVATE).edit();
                if (o.has("share")) e.putBoolean("space_share", o.optBoolean("share"));
                if (o.has("control")) e.putBoolean("space_control", o.optBoolean("control"));
                e.apply();
                reportNow = true;
            } catch (Exception ignored) { }
        }

        /** Android's "Usage access" screen, where the user lets this app see app sizes and recently used apps. */
        @JavascriptInterface
        public void openUsageAccess() {
            runOnUiThread(() -> {
                try {
                    startActivity(new Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS));
                } catch (Exception e) {
                    toast("Open Settings → Apps → Special app access → Usage access");
                }
            });
        }

        /** Opens Tailscale, or its Play Store page when it is not installed. */
        @JavascriptInterface
        public void openTailscale() {
            runOnUiThread(() -> {
                Intent launch = getPackageManager().getLaunchIntentForPackage(TAILSCALE);
                try {
                    if (launch != null) startActivity(launch);
                    else startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=" + TAILSCALE)));
                } catch (Exception e) {
                    try {
                        startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse("https://play.google.com/store/apps/details?id=" + TAILSCALE)));
                    } catch (Exception ignored) { }
                }
            });
        }

        /** Play on phone: opens the full-screen game screen, which starts the game on the PC and streams it. */
        @JavascriptInterface
        public void startGameStream() {
            runOnUiThread(() -> startActivity(new Intent(MainActivity.this, StreamActivity.class)));
        }

        /** The backup way to play (Artemis), while our own stream is new. False when Artemis is not installed. */
        @JavascriptInterface
        public boolean openArtemis() {
            Intent launch = getPackageManager().getLaunchIntentForPackage("com.limelight.noir");
            if (launch == null) return false;
            runOnUiThread(() -> startActivity(launch));
            return true;
        }

        /**
         * Android shows how much RAM each app uses only in its own screens (no app may read that for others). Opens
         * "Running services" (Developer options), else Developer options, else App settings.
         */
        @JavascriptInterface
        public void openRunningServices() {
            runOnUiThread(() -> {
                Intent[] tries = {
                        new Intent().setClassName("com.android.settings", "com.android.settings.Settings$RunningServicesActivity"),
                        new Intent(Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS),
                        new Intent(Settings.ACTION_MANAGE_APPLICATIONS_SETTINGS)
                };
                for (Intent i : tries) {
                    try {
                        startActivity(i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                        return;
                    } catch (Exception ignored) { }
                }
                toast("Open Settings → Developer options → Running services");
            });
        }
    }

    // ------------------------------------------------------------------ Space & RAM: actions and sharing with the PC

    private static final String TAILSCALE = "com.tailscale.ipn";
    private static final int REQ_TRASH = 3101;
    /** Android's folder picker for "sync a phone folder with this PC folder". */
    private static final int REQ_SYNC_TREE = 3201;
    private static final int REQ_SEND_TREE = 3202;
    /** The folder picked to send (a project…): its files, waiting for the page to say where they go. */
    private volatile java.util.List<String[]> sendFolderFiles;   // {uri or path, name, size, folder[, "file"]}

    /** "file" for a path found by walking the phone's storage, else a content address from the picker. */
    private static String srcKindOf(String[] f) {
        return f.length > 4 ? f[4] : "uri";
    }

    /**
     * Every file under the chosen paths (folders walked with All files access, no picker), each with its place:
     * a folder "MyApp" gives "MyApp", "MyApp/src"…; a chosen file has none. Build output and junk are left out
     * unless everything; links are not followed; at most 5000 files.
     */
    private JSONObject walkPaths(JSONArray paths, boolean everything) throws Exception {
        java.util.List<String[]> files = new java.util.ArrayList<>();
        java.util.TreeSet<String> skippedFolders = new java.util.TreeSet<>();
        java.util.LinkedHashSet<String> tops = new java.util.LinkedHashSet<>();
        long[] skipped = {0, 0};
        for (int i = 0; i < paths.length(); i++) {
            File f = new File(paths.getString(i));
            if (f.isDirectory()) {
                String top = TransferHub.cleanFolder(f.getName().replace('/', '_'));
                if (top.isEmpty()) top = "Folder";
                tops.add(top);
                java.util.ArrayDeque<Object[]> todo = new java.util.ArrayDeque<>();
                todo.add(new Object[]{f, top});
                while (!todo.isEmpty()) {
                    Object[] d = todo.poll();
                    File dirF = (File) d[0];
                    File[] kids = dirF.listFiles();
                    if (kids == null) continue;
                    java.util.Arrays.sort(kids, (a, b) -> a.getName().compareToIgnoreCase(b.getName()));
                    for (File k : kids) {
                        String name = k.getName();
                        // A link to elsewhere is not followed (it could loop or leave the folder).
                        if (!k.getCanonicalPath().equals(k.getAbsolutePath()) && !k.getCanonicalFile().getParentFile().equals(dirF.getCanonicalFile())) continue;
                        if (k.isDirectory()) {
                            if (!everything && SKIP_FOLDERS.contains(name.toLowerCase(Locale.US))) { skippedFolders.add(name); continue; }
                            todo.add(new Object[]{k, d[1] + "/" + TransferHub.cleanFolder(name.replace('/', '_'))});
                        } else if (k.isFile()) {
                            boolean junk = name.equals("Thumbs.db") || name.equals("desktop.ini") || name.equals(".DS_Store")
                                    || name.endsWith(".part") || name.endsWith(".tmp") || name.startsWith("~$");
                            if ((!everything && junk) || files.size() >= 5000) { skipped[0]++; skipped[1] += k.length(); continue; }
                            files.add(new String[]{k.getAbsolutePath(), name, String.valueOf(k.length()), (String) d[1], "file"});
                        }
                    }
                }
            } else if (f.isFile() && files.size() < 5000) {
                files.add(new String[]{f.getAbsolutePath(), f.getName(), String.valueOf(f.length()), "", "file"});
            }
        }
        sendFolderFiles = files;
        JSONArray items = new JSONArray();
        long bytes = 0;
        for (String[] x : files) {
            items.put(new JSONObject().put("name", x[1]).put("size", Long.parseLong(x[2])).put("folder", x[3]));
            bytes += Long.parseLong(x[2]);
        }
        return new JSONObject().put("count", files.size()).put("bytes", bytes).put("items", items)
                .put("folders", new JSONArray(tops)).put("loose", files.stream().filter(x -> x[3].isEmpty()).count())
                .put("skipped", new JSONObject().put("files", skipped[0]).put("bytes", skipped[1]).put("folders", new JSONArray(skippedFolders)));
    }
    private volatile boolean sendFolderEverything;
    /** Left out unless "everything" (made again by a build or a package manager), as the PC does. */
    private static final java.util.Set<String> SKIP_FOLDERS = new java.util.HashSet<>(java.util.Arrays.asList(
            ".git", ".svn", ".hg", "node_modules", "bower_components", "bin", "obj", "build", "dist", "out", "target",
            ".gradle", ".idea", ".vs", ".vscode-test", "__pycache__", ".venv", "venv", ".tox", ".mypy_cache", ".pytest_cache",
            ".next", ".nuxt", ".cache", ".parcel-cache", ".dart_tool", "pods", "deriveddata", ".terraform", "packages"));
    private String pendingSyncFolder, pendingSyncLabel;

    interface SpaceDone {
        void done(boolean ok, String message, long freed);
    }

    private volatile SpaceDone trashDone;
    private final java.util.Set<String> spaceFolders = Collections.synchronizedSet(new java.util.HashSet<String>());
    private java.util.concurrent.ScheduledExecutorService spaceSync;
    private volatile long lastReport;
    private volatile boolean reportNow = true;

    private static String deviceName() {
        String m = Build.MODEL == null ? "Phone" : Build.MODEL;
        return Build.MANUFACTURER != null && !m.toLowerCase(Locale.US).startsWith(Build.MANUFACTURER.toLowerCase(Locale.US)) ? Build.MANUFACTURER + " " + m : m;
    }

    private static long freeBytes() {
        try {
            return new StatFs(Environment.getExternalStorageDirectory().getPath()).getAvailableBytes();
        } catch (Exception e) {
            return 0;
        }
    }

    private static boolean deleteTree(File f) {
        if (f.isDirectory()) {
            File[] list = f.listFiles();
            if (list != null) for (File c : list) deleteTree(c);
        }
        return f.delete() || !f.exists();
    }

    private void rescan(String path) {
        try {
            android.media.MediaScannerConnection.scanFile(this, new String[]{path}, null, null);
        } catch (Exception ignored) { }
    }

    /** True when Tailscale's VPN is up on this phone (any VPN counts: Android does not say which). */
    private boolean vpnActive() {
        try {
            android.net.ConnectivityManager cm = (android.net.ConnectivityManager) getSystemService(Context.CONNECTIVITY_SERVICE);
            if (Build.VERSION.SDK_INT < 23 || cm == null) return false;
            android.net.NetworkCapabilities caps = cm.getNetworkCapabilities(cm.getActiveNetwork());
            return caps != null && caps.hasTransport(android.net.NetworkCapabilities.TRANSPORT_VPN);
        } catch (Exception e) {
            return false;
        }
    }

    private boolean tailscaleInstalled() {
        try {
            getPackageManager().getPackageInfo(TAILSCALE, 0);
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    /** One ⋮ action, asked for on this screen or by the PC. Android's own prompts (trash, uninstall) appear on the phone. */
    private void runSpaceAction(String kind, String target, SpaceDone done) {
        try {
            switch (kind) {
                case "open":
                    new AndroidBridge().openLocalFile(target);
                    done.done(true, "Opened on the phone.", 0);
                    return;
                case "delete": {
                    File f = new File(target);
                    if (SpaceScanner.isProtected(target)) { done.done(false, "Android's own folders cannot be deleted here.", 0); return; }
                    if (!f.exists()) { done.done(false, "It is not there any more.", 0); return; }
                    long before = freeBytes();
                    boolean ok = deleteTree(f);
                    rescan(target);
                    reportNow = true;
                    done.done(ok, ok ? "Deleted." : "Android did not let it be deleted.", Math.max(0, freeBytes() - before));
                    return;
                }
                case "recycle": {
                    File f = new File(target);
                    Uri u = Build.VERSION.SDK_INT >= 30 ? contentUriFor(f) : null;
                    if (u == null) { done.done(false, "This file cannot go to the trash on this phone; use Delete.", 0); return; }
                    trash(Collections.singletonList(u), done);
                    return;
                }
                case "cloud": {
                    File f = new File(target);
                    if (SpaceScanner.isProtected(target) || !f.exists()) { done.done(false, "That cannot be moved.", 0); return; }
                    String base = "cloudssd:Offloaded_Files/From phone";
                    int n = moveToCloud(f, f.isDirectory() ? base + "/" + f.getName() : base);
                    reportNow = true;
                    done.done(n > 0, n > 0 ? "Uploading " + n + (n == 1 ? " file" : " files") + " to the cloud drive; each is removed from the phone once it is safely there."
                            : "There is nothing to move.", 0);
                    return;
                }
                case "uninstall":
                    runOnUiThread(() -> {
                        try {
                            startActivity(new Intent(Intent.ACTION_DELETE, Uri.parse("package:" + target)));
                        } catch (Exception ignored) { }
                    });
                    reportNow = true;
                    done.done(true, "Android asks on the phone whether to uninstall it.", 0);
                    return;
                case "app-info":
                    runOnUiThread(() -> {
                        try {
                            startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + target)));
                        } catch (Exception ignored) { }
                    });
                    done.done(true, "App info is open on the phone (Clear cache, Force stop).", 0);
                    return;
                case "clean":
                    clean(target, done);
                    return;
                case "folder":
                    spaceFolders.add(target);
                    reportNow = true;
                    done.done(true, "", 0);
                    return;
                case "report":
                    reportNow = true;
                    done.done(true, "Sending a fresh report.", 0);
                    return;
                default:
                    done.done(false, "This phone app cannot do \"" + kind + "\". Update it.", 0);
            }
        } catch (Exception e) {
            done.done(false, String.valueOf(e.getMessage()), 0);
        }
    }

    /** Queues every file (in a folder: all of them, keeping the folders) for upload; each is deleted after its upload. */
    private int moveToCloud(File f, String target) {
        if (f.isFile()) {
            TransferHub.get(this).enqueueMove(f.getName(), f.length(), f.getAbsolutePath(), target);
            return 1;
        }
        int n = 0;
        File[] list = f.listFiles();
        if (list != null) for (File c : list) n += moveToCloud(c, c.isDirectory() ? target + "/" + c.getName() : target);
        return n;
    }

    private void clean(String id, SpaceDone done) {
        long before = freeBytes();
        switch (id) {
            case "app-cache": {
                File[] dirs = {getCacheDir(), getExternalCacheDir()};
                for (File d : dirs) {
                    File[] list = d == null ? null : d.listFiles();
                    if (list != null) for (File c : list) deleteTree(c);
                }
                thumbCache.evictAll();
                break;
            }
            case "apks":
                for (File f : SpaceScanner.apkFiles()) if (f.delete()) rescan(f.getAbsolutePath());
                break;
            case "thumbnails": {
                File[] list = new File(SpaceScanner.ROOT, "DCIM/.thumbnails").listFiles();
                if (list != null) for (File c : list) deleteTree(c);
                break;
            }
            case "backed-up": {
                List<Object[]> items = CameraBackup.backedUp(this, 30L * 24 * 3600_000L);
                if (items.isEmpty()) { done.done(true, "Nothing to remove: no backed-up photo is older than 30 days.", 0); return; }
                List<Uri> uris = new ArrayList<>();
                for (Object[] o : items) uris.add((Uri) o[0]);
                if (Build.VERSION.SDK_INT >= 30) { trash(uris, done); return; }
                for (Uri u : uris) {
                    try { getContentResolver().delete(u, null, null); } catch (Exception ignored) { }
                }
                break;
            }
            case "system":
                runOnUiThread(() -> {
                    try {
                        startActivity(new Intent(StorageManager.ACTION_MANAGE_STORAGE));
                    } catch (Exception e) {
                        toast("Open Settings → Storage");
                    }
                });
                done.done(true, "Android's Free up space screen is open on the phone.", 0);
                return;
            default:
                done.done(false, "Unknown clean-up.", 0);
                return;
        }
        reportNow = true;
        long freed = Math.max(0, freeBytes() - before);
        done.done(true, "Cleaned.", freed);
    }

    /** Android 11+: moves photos/videos to the system trash after its own prompt (they can be restored for 30 days). */
    private void trash(List<Uri> uris, SpaceDone done) {
        if (Build.VERSION.SDK_INT < 30) { done.done(false, "No trash on this Android version.", 0); return; }
        try {
            android.app.PendingIntent pi = MediaStore.createTrashRequest(getContentResolver(), uris, true);
            trashDone = done;
            runOnUiThread(() -> {
                try {
                    startIntentSenderForResult(pi.getIntentSender(), REQ_TRASH, null, 0, 0, 0);
                } catch (Exception e) {
                    SpaceDone d = trashDone;
                    trashDone = null;
                    if (d != null) d.done(false, "Android's trash prompt did not open: " + e.getMessage(), 0);
                }
            });
        } catch (Exception e) {
            done.done(false, "Could not move it to the trash: " + e.getMessage(), 0);
        }
    }

    /** While the app is open: tell the PC about this phone (every 45 s, or at once after a change) and do what it asked. */
    private void startSpaceSync() {
        if (spaceSync != null) return;
        spaceSync = java.util.concurrent.Executors.newSingleThreadScheduledExecutor();
        spaceSync.scheduleWithFixedDelay(this::spaceSyncTick, 4, 5, java.util.concurrent.TimeUnit.SECONDS);
    }

    private void stopSpaceSync() {
        if (spaceSync != null) {
            spaceSync.shutdownNow();
            spaceSync = null;
        }
    }

    private boolean pcHas(String feature) {
        String f = getSharedPreferences(PREFS, MODE_PRIVATE).getString("features", "[]");
        return f.contains("\"" + feature + "\"");
    }

    private void spaceSyncTick() {
        try {
            SharedPreferences p = getSharedPreferences(PREFS, MODE_PRIVATE);
            if (!p.getBoolean("space_share", true) || !pcHas("space-phone") || TokenStore.pcToken(this).isEmpty()) return;
            String server = PcLink.resolve(this);
            if (server.isEmpty()) return;
            boolean control = p.getBoolean("space_control", true);
            if (control) {
                String cmds = spaceHttp("GET", server + "/api/space/phone-commands", null);
                JSONArray list = cmds == null ? null : new JSONArray(cmds);
                for (int i = 0; list != null && i < list.length(); i++) {
                    JSONObject c = list.getJSONObject(i);
                    final String id = c.optString("id"), srv = server;
                    runSpaceAction(c.optString("kind"), c.optString("target"), (ok, message, freed) -> worker.execute(() -> {
                        try {
                            spaceHttp("POST", srv + "/api/space/phone-result?id=" + Uploader.enc(id) + "&ok=" + (ok ? 1 : 0) + "&freed=" + freed
                                    + "&message=" + Uploader.enc(message == null ? "" : message), "");
                        } catch (Exception ignored) { }
                        reportNow = true;
                    }));
                }
            }
            if (reportNow || System.currentTimeMillis() - lastReport > 45_000) {
                reportNow = false;
                lastReport = System.currentTimeMillis();
                SpaceScanner.ensureFresh(60_000);
                JSONObject r = SpaceScanner.report(this, buildStorageReport(), new JSONObject(new AndroidBridge().getPhoneRamBreakdown()), deviceName(),
                        new java.util.HashSet<>(spaceFolders), control);
                spaceHttp("POST", server + "/api/space/phone-report", r.toString());
            }
        } catch (Exception e) {
            Log.w(TAG, "Space sync: " + e.getMessage());
        }
    }

    /** One small request to the paired PC with this phone's key; the answer body, or null when it did not answer 2xx. */
    private String spaceHttp(String method, String url, String body) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        try {
            c.setRequestMethod(method);
            c.setConnectTimeout(6000);
            c.setReadTimeout(30000);
            c.setRequestProperty("X-Pairing-Token", TokenStore.pcToken(this));
            if (body != null) {
                byte[] b = body.getBytes("UTF-8");
                c.setDoOutput(true);
                c.setFixedLengthStreamingMode(b.length);
                c.setRequestProperty("Content-Type", "application/json; charset=utf-8");
                try (java.io.OutputStream o = c.getOutputStream()) {
                    o.write(b);
                }
            }
            int code = c.getResponseCode();
            if (code < 200 || code >= 300) return null;
            try (InputStream in = c.getInputStream()) {
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                byte[] buf = new byte[8192];
                for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
                return out.toString("UTF-8");
            }
        } finally {
            c.disconnect();
        }
    }

    // ------------------------------------------------------------------ find the PC

    /** Tries every address of this /24 on the bridge port; a PC answers /api/status with its name even before pairing. */
    private void runDiscovery(final int port) {
        final JSONArray found = new JSONArray();
        final List<String> bases = new ArrayList<>();
        final List<String> own = new ArrayList<>();
        try {
            for (NetworkInterface ni : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!ni.isUp() || ni.isLoopback() || !isLanInterface(ni.getName())) continue;
                for (InetAddress a : Collections.list(ni.getInetAddresses())) {
                    if (!(a instanceof Inet4Address) || !a.isSiteLocalAddress()) continue;
                    String ip = a.getHostAddress();
                    own.add(ip);
                    String base = ip.substring(0, ip.lastIndexOf('.') + 1);
                    if (!bases.contains(base)) bases.add(base);
                }
            }
        } catch (Exception e) {
            Log.w(TAG, "discovery: cannot list interfaces", e);
        }

        final List<String> hosts = new ArrayList<>();
        // USB debugging with "adb reverse" makes the PC reachable as this phone's own localhost.
        hosts.add("127.0.0.1");
        for (String base : bases) {
            for (int i = 1; i <= 254; i++) {
                String ip = base + i;
                if (!own.contains(ip)) hosts.add(ip);
            }
        }

        ExecutorService pool = Executors.newFixedThreadPool(48);
        final CountDownLatch done = new CountDownLatch(hosts.size());
        for (final String host : hosts) {
            pool.execute(() -> {
                try {
                    JSONObject pc = probePc(host, port);
                    if (pc != null) {
                        synchronized (found) {
                            found.put(pc);
                            emit("discovery", discoveryJson(found, false, bases.isEmpty()));
                        }
                    }
                } finally {
                    done.countDown();
                }
            });
        }
        try { done.await(); } catch (InterruptedException ignored) { }
        pool.shutdown();
        synchronized (found) {
            emit("discovery", discoveryJson(found, true, bases.isEmpty()));
        }
    }

    private static JSONObject discoveryJson(JSONArray found, boolean done, boolean noWifi) {
        JSONObject o = new JSONObject();
        try {
            o.put("found", new JSONArray(found.toString()));
            o.put("done", done);
            o.put("noWifi", noWifi);
        } catch (Exception ignored) { }
        return o;
    }

    private static JSONObject probePc(String host, int port) {
        try (Socket s = new Socket()) {
            s.connect(new InetSocketAddress(host, port), 400);
        } catch (Exception e) {
            return null;
        }
        HttpURLConnection conn = null;
        try {
            conn = (HttpURLConnection) new URL("http://" + host + ":" + port + "/api/status").openConnection();
            conn.setConnectTimeout(1500);
            conn.setReadTimeout(2500);
            if (conn.getResponseCode() != 200) return null;
            JSONObject status = new JSONObject(readAll(conn.getInputStream()));
            if (!status.has("pcName") || !status.has("port")) return null; // some other program on this port
            JSONObject pc = new JSONObject();
            pc.put("url", "http://" + host + ":" + port);
            pc.put("ip", host);
            pc.put("pcName", status.optString("pcName", "PC"));
            // The public id lets the page tell a saved PC from a new one without sending any key.
            pc.put("pcId", status.optString("pcId", ""));
            pc.put("version", status.optString("version", ""));
            return pc;
        } catch (Exception e) {
            return null;
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    private static String readAll(InputStream in) throws IOException {
        if (in == null) return "";
        StringBuilder sb = new StringBuilder();
        try (BufferedReader r = new BufferedReader(new InputStreamReader(in, "UTF-8"))) {
            char[] buf = new char[4096];
            int n;
            while ((n = r.read(buf)) > 0) sb.append(buf, 0, n);
        }
        return sb.toString();
    }

    // ------------------------------------------------------------------ phone storage

    /** {total, free} of the phone's internal storage, the same numbers Android's Settings shows. */
    private long[] storageTotals() {
        if (Build.VERSION.SDK_INT >= 26) {
            try {
                StorageStatsManager ssm = (StorageStatsManager) getSystemService(Context.STORAGE_STATS_SERVICE);
                if (ssm != null) {
                    return new long[]{ssm.getTotalBytes(StorageManager.UUID_DEFAULT), ssm.getFreeBytes(StorageManager.UUID_DEFAULT)};
                }
            } catch (Exception e) {
                Log.w(TAG, "StorageStatsManager failed, falling back to StatFs", e);
            }
        }
        StatFs stat = new StatFs(Environment.getDataDirectory().getPath());
        return new long[]{stat.getBlockCountLong() * stat.getBlockSizeLong(), stat.getAvailableBlocksLong() * stat.getBlockSizeLong()};
    }

    /** One pass over the phone's file index: count and size per kind, plus what is left for apps and the system. */
    private JSONObject buildStorageReport() {
        JSONObject o = new JSONObject();
        try {
            long[] st = storageTotals();
            String access = storageAccess();
            o.put("total", st[0]);
            o.put("free", st[1]);
            o.put("used", st[0] - st[1]);
            o.put("access", access);

            String[] kinds = {"images", "videos", "audio", "documents", "apps", "other"};
            Map<String, long[]> sums = new HashMap<>();
            for (String k : kinds) sums.put(k, new long[2]);
            long[] downloads = new long[2];
            long filesBytes = 0;

            if (!"none".equals(access)) {
                String downloadDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS).getAbsolutePath() + "/";
                String[] proj = {MediaStore.MediaColumns.DATA, MediaStore.MediaColumns.SIZE, MediaStore.Files.FileColumns.MEDIA_TYPE, MediaStore.MediaColumns.MIME_TYPE};
                try (Cursor c = getContentResolver().query(MediaStore.Files.getContentUri("external"), proj, null, null, null)) {
                    if (c != null) {
                        while (c.moveToNext()) {
                            String path = c.getString(0);
                            long size = c.getLong(1);
                            int mediaType = c.getInt(2);
                            if (path == null || size <= 0) continue; // folders have no size
                            String kind;
                            if (mediaType == MediaStore.Files.FileColumns.MEDIA_TYPE_IMAGE) kind = "images";
                            else if (mediaType == MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO) kind = "videos";
                            else if (mediaType == MediaStore.Files.FileColumns.MEDIA_TYPE_AUDIO) kind = "audio";
                            else {
                                // Not in the gallery (e.g. inside a .nomedia folder): count pictures and clips there as
                                // "other", so each number equals what its category list shows.
                                String k = kindOf(path);
                                kind = "documents".equals(k) || "apps".equals(k) ? k : "other";
                            }
                            long[] s = sums.get(kind);
                            s[0]++;
                            s[1] += size;
                            filesBytes += size;
                            if (path.startsWith(downloadDir)) {
                                downloads[0]++;
                                downloads[1] += size;
                            }
                        }
                    }
                }
                // With full access the Download folder itself is the truth (the index can miss fresh files).
                List<File> direct = downloadFiles();
                if (direct != null) {
                    downloads[0] = direct.size();
                    downloads[1] = 0;
                    for (File f : direct) downloads[1] += f.length();
                }
            }

            JSONObject cats = new JSONObject();
            for (String k : kinds) {
                cats.put(k, new JSONObject().put("count", sums.get(k)[0]).put("bytes", sums.get(k)[1]));
            }
            cats.put("downloads", new JSONObject().put("count", downloads[0]).put("bytes", downloads[1]));
            o.put("cats", cats);
            o.put("filesBytes", filesBytes);
            // Installed apps, their data and Android itself: everything used that is not one of the files above.
            o.put("appsSystemBytes", Math.max(0, (st[0] - st[1]) - filesBytes));
        } catch (Exception e) {
            Log.w(TAG, "storage scan failed", e);
            try { o.put("error", String.valueOf(e.getMessage())); } catch (Exception ignored) { }
        }
        return o;
    }

    /** Files directly in Download, newest first; null when Android does not let this app read the folder itself. */
    private List<File> downloadFiles() {
        if (!"all".equals(storageAccess())) return null;
        File[] items = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS).listFiles();
        if (items == null) return null;
        List<File> out = new ArrayList<>();
        for (File f : items) if (f.isFile() && !f.getName().startsWith(".")) out.add(f);
        Collections.sort(out, (a, b) -> Long.compare(b.lastModified(), a.lastModified()));
        return out;
    }

    private static JSONObject fileJson(File f) throws Exception {
        JSONObject fi = new JSONObject();
        fi.put("name", f.getName());
        fi.put("path", f.getAbsolutePath());
        fi.put("size", f.length());
        fi.put("modified", f.lastModified());
        return fi;
    }

    private JSONObject listDirectory(String requestedPath) {
        JSONObject result = new JSONObject();
        JSONArray folders = new JSONArray();
        JSONArray files = new JSONArray();
        try {
            File root = Environment.getExternalStorageDirectory();
            File dir = (requestedPath == null || requestedPath.trim().isEmpty()) ? root : new File(requestedPath.trim());
            result.put("path", dir.getAbsolutePath());
            result.put("root", root.getAbsolutePath());
            result.put("access", storageAccess());
            if (!dir.isDirectory()) {
                result.put("error", "Folder not found");
            } else {
                File[] items = dir.listFiles();
                if (items == null) {
                    result.put("error", "Android does not allow reading this folder");
                } else {
                    Arrays.sort(items, (a, b) -> a.getName().compareToIgnoreCase(b.getName()));
                    int n = 0;
                    for (File f : items) {
                        if (f.getName().startsWith(".")) continue;
                        if (++n > MAX_LIST_ITEMS) {
                            result.put("truncated", true);
                            break;
                        }
                        if (f.isDirectory()) {
                            JSONObject fo = new JSONObject();
                            fo.put("name", f.getName());
                            fo.put("path", f.getAbsolutePath());
                            fo.put("modified", f.lastModified());
                            String[] children = f.list();
                            if (children != null) fo.put("itemCount", children.length);
                            folders.put(fo);
                        } else {
                            files.put(fileJson(f));
                        }
                    }
                }
            }
        } catch (Exception ex) {
            try { result.put("error", String.valueOf(ex.getMessage())); } catch (Exception ignored) { }
        }
        try {
            result.put("folders", folders);
            result.put("files", files);
        } catch (Exception ignored) { }
        return result;
    }

    private static String likeAny(List<String> exts) {
        StringBuilder sb = new StringBuilder();
        for (String e : exts) {
            if (sb.length() > 0) sb.append(" OR ");
            sb.append(MediaStore.MediaColumns.DATA).append(" LIKE '%.").append(e).append("'");
        }
        return "(" + sb + ")";
    }

    private JSONObject listCategoryFiles(String category) {
        JSONObject result = new JSONObject();
        JSONArray files = new JSONArray();
        String cat = category == null ? "" : category.toLowerCase(Locale.US);
        try {
            result.put("category", cat);
            result.put("access", storageAccess());
            String downloadDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS).getAbsolutePath();
            String selection;
            String[] args = null;
            switch (cat) {
                case "images": selection = MediaStore.Files.FileColumns.MEDIA_TYPE + "=" + MediaStore.Files.FileColumns.MEDIA_TYPE_IMAGE; break;
                case "videos": selection = MediaStore.Files.FileColumns.MEDIA_TYPE + "=" + MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO; break;
                case "audio": selection = MediaStore.Files.FileColumns.MEDIA_TYPE + "=" + MediaStore.Files.FileColumns.MEDIA_TYPE_AUDIO; break;
                case "documents": case "docs": selection = likeAny(DOC_EXT); break;
                case "apps": selection = likeAny(APP_EXT); break;
                case "downloads": selection = MediaStore.MediaColumns.DATA + " LIKE ?"; args = new String[]{downloadDir + "/%"}; break;
                default: selection = null;
            }
            // The index can lag behind for a folder that was just written to; with full access read Download directly.
            List<File> direct = "downloads".equals(cat) ? downloadFiles() : null;
            if (direct != null) {
                for (File f : direct) files.put(fileJson(f));
            } else if (queryFiles(selection, args, files, 1000)) {
                result.put("truncated", true);
            }
        } catch (Exception ex) {
            try { result.put("error", String.valueOf(ex.getMessage())); } catch (Exception ignored) { }
        }
        try {
            result.put("folders", new JSONArray());
            result.put("files", files);
        } catch (Exception ignored) { }
        return result;
    }

    private JSONObject searchFiles(String query) {
        JSONObject result = new JSONObject();
        JSONArray files = new JSONArray();
        try {
            result.put("query", query);
            String q = query == null ? "" : query.trim();
            if (!q.isEmpty()) {
                String like = "%" + q.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_") + "%";
                if (queryFiles(MediaStore.MediaColumns.DISPLAY_NAME + " LIKE ? ESCAPE '\\'", new String[]{like}, files, 200)) result.put("truncated", true);
            }
        } catch (Exception ex) {
            try { result.put("error", String.valueOf(ex.getMessage())); } catch (Exception ignored) { }
        }
        try {
            result.put("folders", new JSONArray());
            result.put("files", files);
        } catch (Exception ignored) { }
        return result;
    }

    /** Newest first from the phone's file index; true when there were more than max. */
    private boolean queryFiles(String selection, String[] args, JSONArray out, int max) throws Exception {
        String[] proj = {MediaStore.MediaColumns.DATA, MediaStore.MediaColumns.SIZE, MediaStore.MediaColumns.DATE_MODIFIED};
        try (Cursor c = getContentResolver().query(MediaStore.Files.getContentUri("external"), proj, selection, args,
                MediaStore.MediaColumns.DATE_MODIFIED + " DESC")) {
            if (c == null) return false;
            while (c.moveToNext()) {
                String path = c.getString(0);
                long size = c.getLong(1);
                if (path == null || size <= 0) continue;
                if (out.length() >= max) return true;
                JSONObject fi = new JSONObject();
                fi.put("name", path.substring(path.lastIndexOf('/') + 1));
                fi.put("path", path);
                fi.put("size", size);
                fi.put("modified", c.getLong(2) * 1000L);
                out.put(fi);
            }
        }
        return false;
    }

    /** content:// address of a phone file, which other apps are allowed to open (a raw file path often is not). */
    private Uri contentUriFor(File f) {
        Uri base = MediaStore.Files.getContentUri("external");
        try (Cursor c = getContentResolver().query(base, new String[]{MediaStore.MediaColumns._ID},
                MediaStore.MediaColumns.DATA + "=?", new String[]{f.getAbsolutePath()}, null)) {
            if (c != null && c.moveToFirst()) return ContentUris.withAppendedId(base, c.getLong(0));
        } catch (Exception ignored) { }
        return null;
    }

    // ------------------------------------------------------------------ thumbnails

    private WebResourceResponse thumbResponse(String path) {
        byte[] data = path == null ? null : thumbCache.get(path);
        if (data == null && path != null) {
            data = makeThumb(new File(path));
            if (data != null) thumbCache.put(path, data);
        }
        Map<String, String> headers = new HashMap<>();
        headers.put("Access-Control-Allow-Origin", "*");
        if (data == null) {
            return new WebResourceResponse("image/jpeg", null, 404, "Not Found", headers, new ByteArrayInputStream(new byte[0]));
        }
        headers.put("Cache-Control", "max-age=86400");
        return new WebResourceResponse("image/jpeg", null, 200, "OK", headers, new ByteArrayInputStream(data));
    }

    private byte[] makeThumb(File f) {
        if (!f.isFile()) return null;
        Bitmap bmp = null;
        try {
            String kind = kindOf(f.getName());
            if (Build.VERSION.SDK_INT >= 29) {
                Size size = new Size(320, 320);
                if ("images".equals(kind)) bmp = ThumbnailUtils.createImageThumbnail(f, size, null);
                else if ("videos".equals(kind)) bmp = ThumbnailUtils.createVideoThumbnail(f, size, null);
                else if ("audio".equals(kind)) bmp = ThumbnailUtils.createAudioThumbnail(f, size, null);
            } else if ("images".equals(kind)) {
                BitmapFactory.Options opts = new BitmapFactory.Options();
                opts.inJustDecodeBounds = true;
                BitmapFactory.decodeFile(f.getPath(), opts);
                int sample = 1;
                while (opts.outWidth / sample > 640 || opts.outHeight / sample > 640) sample *= 2;
                opts = new BitmapFactory.Options();
                opts.inSampleSize = sample;
                bmp = BitmapFactory.decodeFile(f.getPath(), opts);
            } else if ("videos".equals(kind)) {
                bmp = ThumbnailUtils.createVideoThumbnail(f.getPath(), MediaStore.Images.Thumbnails.MINI_KIND);
            }
            if (bmp == null) return null;
            ByteArrayOutputStream out = new ByteArrayOutputStream(24 * 1024);
            bmp.compress(Bitmap.CompressFormat.JPEG, 78, out);
            return out.toByteArray();
        } catch (Exception | OutOfMemoryError e) {
            return null; // no embedded art, broken file, â€¦: the page shows its icon instead
        } finally {
            if (bmp != null) bmp.recycle();
        }
    }

    // ------------------------------------------------------------------ files on the cloud (PC or Google Drive)

    /** Runs one file operation by drive id and answers {reqId, ok, status, data, error, source}. */
    private JSONObject runCloudOp(int reqId, String op, String argsJson) {
        JSONObject r = new JSONObject();
        CloudBackend b = Backends.files(this);
        try {
            r.put("reqId", reqId);
            JSONObject a = argsJson == null || argsJson.isEmpty() ? new JSONObject() : new JSONObject(argsJson);
            Object data;
            switch (op) {
                case "roots": {
                    JSONArray out = new JSONArray();
                    for (RootInfo ri : b.roots())
                        out.put(new JSONObject().put("id", ri.id).put("title", ri.title).put("readOnly", ri.readOnly).put("canCreate", ri.canCreate)
                                .put("totalBytes", ri.totalBytes).put("freeBytes", ri.freeBytes));
                    data = out;
                    break;
                }
                case "list": {
                    JSONArray out = new JSONArray();
                    for (DocInfo d : b.list(a.getString("path"))) out.put(docJson(d));
                    data = new JSONObject().put("entries", out);
                    break;
                }
                case "search": {
                    JSONArray out = new JSONArray();
                    for (DocInfo d : b.search(a.getString("path"), a.getString("q"), a.optInt("max", 100))) out.put(docJson(d));
                    data = new JSONObject().put("entries", out);
                    break;
                }
                case "stat": data = docJson(b.stat(a.getString("path"))); break;
                case "create": data = docJson(b.create(a.getString("parent"), a.getString("name"), a.optBoolean("dir"))); break;
                case "rename": data = docJson(b.rename(a.getString("path"), a.getString("name"))); break;
                case "move": data = docJson(b.move(a.getString("path"), a.getString("to"))); break;
                case "delete": b.delete(a.getString("path")); data = new JSONObject().put("success", true); break;
                // A PC's copy in the cloud: where it is and what the PC last wrote about it. Always from Google Drive.
                case "copy-info": data = new DriveBackend(this).copyInfo(a.getString("pc")); break;
                default: throw new BackendException(400, "Unknown operation " + op);
            }
            r.put("ok", true).put("status", 200).put("data", data);
        } catch (BackendException e) {
            try { r.put("ok", false).put("status", e.status).put("error", e.getMessage()); } catch (Exception ignored) { }
        } catch (Exception e) {
            try { r.put("ok", false).put("status", 500).put("error", String.valueOf(e.getMessage())); } catch (Exception ignored) { }
        }
        try { r.put("source", b.label()); } catch (Exception ignored) { }
        return r;
    }

    private static long get(Map<String, Long> m, String key) {
        Long v = m.get(key);
        return v == null ? 0 : v;
    }

    private static JSONObject withReq(int reqId, JSONObject o) {
        try { o.put("reqId", reqId); } catch (Exception ignored) { }
        return o;
    }

    private static WebResourceResponse bytesResponse(String mime, byte[] data, int status) {
        Map<String, String> headers = new HashMap<>();
        headers.put("Access-Control-Allow-Origin", "*");
        if (data == null) return new WebResourceResponse(mime, null, 404, "Not Found", headers, new ByteArrayInputStream(new byte[0]));
        headers.put("Cache-Control", "max-age=86400");
        return new WebResourceResponse(mime, null, status, "OK", headers, new ByteArrayInputStream(data));
    }

    /** cloudssd.cloud/read?id=…: a cloud file for the page's video, picture and audio players, with Range support. */
    private WebResourceResponse cloudRead(Uri u, Map<String, String> requestHeaders) {
        String id = u.getQueryParameter("id");
        Map<String, String> headers = new HashMap<>();
        headers.put("Access-Control-Allow-Origin", "*");
        headers.put("Accept-Ranges", "bytes");
        try {
            CloudBackend b = Backends.files(this);
            DocInfo d = b.stat(id);
            long start = 0;
            String range = requestHeaders == null ? null : requestHeaders.get("Range");
            if (range != null && range.startsWith("bytes=")) {
                String spec = range.substring(6).split(",")[0];
                int dash = spec.indexOf('-');
                if (dash > 0) start = Long.parseLong(spec.substring(0, dash).trim());
            }
            InputStream in = b.openRead(id, start);
            String mime = mimeOf(d.name);
            if (start > 0 && d.size > 0) {
                headers.put("Content-Range", "bytes " + start + "-" + (d.size - 1) + "/" + d.size);
                return new WebResourceResponse(mime, null, 206, "Partial Content", headers, in);
            }
            if (d.size > 0) headers.put("Content-Length", String.valueOf(d.size));
            return new WebResourceResponse(mime, null, 200, "OK", headers, in);
        } catch (Exception e) {
            return new WebResourceResponse("text/plain", null, 404, "Not Found", headers, new ByteArrayInputStream(new byte[0]));
        }
    }

    /** A kept copy for the page's players and readers: like cloudRead, from this phone's storage. */
    private WebResourceResponse offlineRead(Uri u, Map<String, String> requestHeaders) {
        Map<String, String> headers = new HashMap<>();
        headers.put("Access-Control-Allow-Origin", "*");
        headers.put("Accept-Ranges", "bytes");
        File f = OfflinePins.get(this).local(u.getQueryParameter("pc"), u.getQueryParameter("id"));
        if (f == null) return new WebResourceResponse("text/plain", null, 404, "Not Found", headers, new ByteArrayInputStream(new byte[0]));
        String id = u.getQueryParameter("id");
        String mime = mimeOf(id.substring(Math.max(id.lastIndexOf('/'), id.indexOf(':')) + 1));
        long size = f.length(), start = 0;
        String range = requestHeaders == null ? null : requestHeaders.get("Range");
        if (range != null && range.startsWith("bytes=")) {
            String spec = range.substring(6).split(",")[0];
            int dash = spec.indexOf('-');
            try {
                if (dash > 0) start = Long.parseLong(spec.substring(0, dash).trim());
            } catch (NumberFormatException ignored) { }
        }
        try {
            java.io.FileInputStream in = new java.io.FileInputStream(f);
            if (start > 0 && start < size) {
                long skipped = 0;
                while (skipped < start) {
                    long n = in.skip(start - skipped);
                    if (n <= 0) break;
                    skipped += n;
                }
                headers.put("Content-Range", "bytes " + start + "-" + (size - 1) + "/" + size);
                headers.put("Content-Length", String.valueOf(size - start));
                return new WebResourceResponse(mime, null, 206, "Partial Content", headers, in);
            }
            headers.put("Content-Length", String.valueOf(size));
            return new WebResourceResponse(mime, null, 200, "OK", headers, in);
        } catch (IOException e) {
            return new WebResourceResponse("text/plain", null, 404, "Not Found", headers, new ByteArrayInputStream(new byte[0]));
        }
    }

    private WebResourceResponse cloudThumb(Uri u) {
        try (InputStream in = Backends.files(this).thumbnail(u.getQueryParameter("id"), 320)) {
            if (in == null) return bytesResponse("image/jpeg", null, 404);
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buf = new byte[16 * 1024];
            for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
            return bytesResponse("image/jpeg", out.toByteArray(), 200);
        } catch (Exception e) {
            return bytesResponse("image/jpeg", null, 404);
        }
    }

    /**
     * cloudssd.pcthumb/?id=&lt;drive id&gt;&amp;w=&lt;px&gt;&amp;m=&lt;modified&gt;&amp;s=&lt;size&gt;: a thumbnail from the PC in use, kept in this
     * phone's cache (by PC, item, size and date, so a changed photo gets a new one). Served from the cache when the PC is
     * away. The pairing key never appears in the page's image addresses.
     */
    private WebResourceResponse pcThumb(Uri u) {
        String id = u.getQueryParameter("id");
        String w = u.getQueryParameter("w");
        String pc = PcProfiles.activeId(this);
        if (id == null || id.isEmpty() || pc.isEmpty()) return bytesResponse("image/jpeg", null, 404);
        int px = 160;
        try { px = Math.max(64, Math.min(640, Integer.parseInt(w))); } catch (Exception ignored) { }
        File dir = new File(new File(getCacheDir(), "pc-thumbs"), PcProfiles.key(pc));
        File file = new File(dir, PcProfiles.key(id + "|" + u.getQueryParameter("m") + "|" + u.getQueryParameter("s") + "|" + px) + ".jpg");
        if (file.isFile() && file.length() > 0) {
            file.setLastModified(System.currentTimeMillis());
            try { return bytesResponse("image/jpeg", readFile(file), 200); } catch (Exception ignored) { }
        }
        try (InputStream in = new PcBackend(this).thumbnail(id, px)) {
            if (in == null) return bytesResponse("image/jpeg", null, 404);
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buf = new byte[16 * 1024];
            for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
            byte[] bytes = out.toByteArray();
            if (bytes.length > 0) {
                dir.mkdirs();
                File tmp = new File(dir, file.getName() + ".part");
                try (java.io.FileOutputStream fo = new java.io.FileOutputStream(tmp)) { fo.write(bytes); }
                if (!tmp.renameTo(file)) tmp.delete();
                trimPcThumbs();
            }
            return bytesResponse("image/jpeg", bytes, 200);
        } catch (Exception e) {
            return bytesResponse("image/jpeg", null, 404);
        }
    }

    private static byte[] readFile(File f) throws IOException {
        try (InputStream in = new java.io.FileInputStream(f)) {
            ByteArrayOutputStream out = new ByteArrayOutputStream((int) Math.max(1024, f.length()));
            byte[] buf = new byte[16 * 1024];
            for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
            return out.toByteArray();
        }
    }

    private long pcThumbsTrimmedAt;

    /** At most 64 MB of PC thumbnails on the phone: the least recently shown go first. */
    private void trimPcThumbs() {
        long now = System.currentTimeMillis();
        if (now - pcThumbsTrimmedAt < 60_000) return;
        pcThumbsTrimmedAt = now;
        List<File> all = new ArrayList<>();
        File root = new File(getCacheDir(), "pc-thumbs");
        File[] pcs = root.listFiles();
        if (pcs == null) return;
        long total = 0;
        for (File d : pcs) {
            File[] fs = d.listFiles();
            if (fs == null) continue;
            for (File f : fs) { all.add(f); total += f.length(); }
        }
        if (total <= 64L * 1024 * 1024) return;
        Collections.sort(all, (a, b) -> Long.compare(a.lastModified(), b.lastModified()));
        for (File f : all) {
            if (total <= 48L * 1024 * 1024) break;
            total -= f.length();
            f.delete();
        }
    }

    // ------------------------------------------------------------------ uploads from the picker or other apps

    /**
     * Queues files chosen in a picker or shared from another app.
     * @param persisted the app may keep reading them (OPEN_DOCUMENT), so a retry works after the app was closed
     * @param target    the cloud folder ("&lt;drive&gt;:&lt;folder&gt;") to save into, or "" for the PC's upload folder
     */
    private void uploadUris(List<Uri> uris, final String target, boolean persisted) {
        // Several files at once are one task; one file gets a task of its own anyway.
        if (uris.size() > 1) startTask(meta("dir", "toPc", "saveTo", target.isEmpty() ? TransferHub.get(this).pcName + " › Uploads" : target));
        try {
            for (final Uri uri : uris) uploadUri(uri, target, persisted);
        } finally {
            TransferHub.get(this).endTask();
        }
    }

    /** {name, size} of a shared file (size -1 when the app that shared it does not say and it cannot be measured). */
    private Object[] nameAndSize(Uri uri) {
        String name = "upload_" + System.currentTimeMillis();
        long size = -1;
        if ("file".equals(uri.getScheme()) && uri.getPath() != null) {
            File f = new File(uri.getPath());
            return new Object[]{f.getName(), f.length()};
        }
        try (Cursor cursor = getContentResolver().query(uri, null, null, null, null)) {
            if (cursor != null && cursor.moveToFirst()) {
                int nameIdx = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                int sizeIdx = cursor.getColumnIndex(OpenableColumns.SIZE);
                if (nameIdx >= 0 && cursor.getString(nameIdx) != null) name = cursor.getString(nameIdx);
                if (sizeIdx >= 0 && !cursor.isNull(sizeIdx)) size = cursor.getLong(sizeIdx);
            }
        } catch (Exception ignored) { }
        if (size < 0) {
            try (android.content.res.AssetFileDescriptor fd = getContentResolver().openAssetFileDescriptor(uri, "r")) {
                if (fd != null && fd.getLength() >= 0) size = fd.getLength();
            } catch (Exception ignored) { }
        }
        return new Object[]{name, size};
    }

    private void uploadUri(Uri uri, String target, boolean persisted) {
        Object[] ns = nameAndSize(uri);
        String name = (String) ns[0];
        long size = (Long) ns[1];
        if ("file".equals(uri.getScheme()) && uri.getPath() != null) {
            TransferHub.get(this).enqueueUpload(name, size, "file", new File(uri.getPath()).getAbsolutePath(), target);
            return;
        }
        boolean kept = false;
        if (persisted) {
            try {
                getContentResolver().takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION);
                kept = true;
            } catch (Exception ignored) { }
        }
        TransferHub.get(this).enqueueUpload(name, size, kept ? "uri" : "uri-temp", uri.toString(), target);
    }

    // ------------------------------------------------------------------ activity

    /** Pairing links, the return from Google sign-in, and files shared to this app from other apps. */
    private void handleIntent(Intent intent) {
        if (intent == null || intent.getAction() == null) return;
        String action = intent.getAction();
        Uri d = intent.getData();
        if (ACTION_FIND_MY_PHONE.equals(action)) {
            // Kept for the start of the page too (it may not be loaded yet); the page takes it once.
            pendingFindMyPhone = true;
            emit("findMyPhone", "{}");
            return;
        }
        if (Intent.ACTION_VIEW.equals(action) && d != null && "cloudssd".equals(d.getScheme()) && "pair".equals(d.getHost())) {
            try {
                JSONObject o = new JSONObject();
                o.put("host", d.getQueryParameter("host"));
                if (d.getQueryParameter("pair") != null) o.put("pair", d.getQueryParameter("pair"));
                if (d.getQueryParameter("token") != null) o.put("token", d.getQueryParameter("token"));
                pendingPairLink = o.toString();
                emit("pair", o);
            } catch (Exception ignored) { }
        } else if (Intent.ACTION_VIEW.equals(action) && d != null && GoogleAuth.isRedirect(d)) {
            final Uri data = d;
            worker.execute(() -> {
                JSONObject r = GoogleAuth.handleRedirect(MainActivity.this, data);
                CloudDocumentsProvider.notifyRootsChanged(MainActivity.this);
                emit("drive", r);
            });
        } else if (Intent.ACTION_SEND.equals(action) || Intent.ACTION_SEND_MULTIPLE.equals(action)) {
            final List<Uri> uris = new ArrayList<>();
            if (Intent.ACTION_SEND.equals(action)) {
                Uri u = intent.getParcelableExtra(Intent.EXTRA_STREAM);
                if (u != null) uris.add(u);
            } else {
                ArrayList<Uri> list = intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM);
                if (list != null) uris.addAll(list);
            }
            if (uris.isEmpty()) return;
            // "Send to a device…": the page asks where (the PC, or a phone, tablet or laptop through it).
            if (intent.getComponent() != null && intent.getComponent().getClassName().endsWith(".SendToDevice")) {
                synchronized (shareIn) {
                    shareIn.clear();
                    shareIn.addAll(uris);
                }
                emit("shareIn", "{}");
                return;
            }
            toast(uris.size() == 1 ? Tr.t(MainActivity.this, "Sending 1 file to PC…") : Tr.t(MainActivity.this, "Sending {0} files to PC…", uris.size()));
            worker.execute(() -> uploadUris(uris, "", false));
        }
        // cloudssd://auth (back from the Desktop-key sign-in page) only brings the app to the front.
    }

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        SharedPreferences prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        serverBaseUrl = prefs.getString("server", "");
        TokenStore.pcToken(this); // moves a key saved in plain text by an older version into the key store

        CameraBackup.ensureScheduled(this);
        if (Build.VERSION.SDK_INT >= 33 && !hasPermission("android.permission.POST_NOTIFICATIONS")) {
            requestPermissions(new String[]{"android.permission.POST_NOTIFICATIONS"}, 102);
        }

        int background = nightMode() ? 0xFF121316 : 0xFFF8F9FC;
        webView = new WebView(this);
        final FrameLayout root = new FrameLayout(this);
        // The bars as the page's look left them last time (until the page says again), else the phone's light or dark.
        SharedPreferences barPrefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        bars = new BarsDrawable(barPrefs.getInt("bars_top", background), barPrefs.getInt("bars_rest", background));
        root.setBackground(bars);
        if (barPrefs.contains("bars_top")) applyBarIcons(barPrefs.getBoolean("bars_status_dark", !nightMode()), barPrefs.getBoolean("bars_nav_dark", !nightMode()));
        root.addView(webView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        setContentView(root);
        appLock = new AppLock(this, root, background);
        // Android 15+ draws apps under the status and navigation bars; keep the page clear of them and of the keyboard.
        root.setOnApplyWindowInsetsListener((v, insets) -> {
            if (Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                android.graphics.Insets ime = insets.getInsets(WindowInsets.Type.ime());
                v.setPadding(bars.left, bars.top, bars.right, Math.max(bars.bottom, ime.bottom));
                MainActivity.this.bars.setTopHeight(bars.top);
                // Consumed: the page must not add the same space a second time through CSS safe-area insets.
                return WindowInsets.CONSUMED;
            }
            v.setPadding(insets.getSystemWindowInsetLeft(), insets.getSystemWindowInsetTop(),
                    insets.getSystemWindowInsetRight(), insets.getSystemWindowInsetBottom());
            bars.setTopHeight(insets.getSystemWindowInsetTop());
            return insets.consumeSystemWindowInsets();
        });

        String version = "0";
        try {
            version = getPackageManager().getPackageInfo(getPackageName(), 0).versionName;
        } catch (Exception ignored) { }

        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setDatabaseEnabled(true);
        // The page shows phone photos with file:// addresses; it may not read other files' contents through scripts.
        settings.setAllowFileAccess(true);
        settings.setAllowContentAccess(true);
        settings.setAllowFileAccessFromFileURLs(false);
        settings.setAllowUniversalAccessFromFileURLs(false);
        settings.setMediaPlaybackRequiresUserGesture(false);
        settings.setCacheMode(WebSettings.LOAD_DEFAULT);
        // Pinch zoom for people who need bigger text; the +/- buttons stay hidden.
        settings.setBuiltInZoomControls(true);
        settings.setDisplayZoomControls(false);
        settings.setUserAgentString(settings.getUserAgentString() + " CloudSSD-App/" + version);
        // Inspecting the page over USB only in a debug build of the app.
        WebView.setWebContentsDebuggingEnabled(debuggable());

        webView.setBackgroundColor(background);
        webView.addJavascriptInterface(new AndroidBridge(), "AndroidBridge");

        webView.setWebChromeClient(new WebChromeClient() {
            @Override
            public boolean onConsoleMessage(ConsoleMessage m) {
                Log.i(TAG, "js " + m.messageLevel() + ": " + m.message() + " (" + m.sourceId() + ":" + m.lineNumber() + ")");
                return true;
            }

            @Override
            public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback, FileChooserParams params) {
                if (webChooserCallback != null) webChooserCallback.onReceiveValue(null);
                webChooserCallback = callback;
                Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
                intent.addCategory(Intent.CATEGORY_OPENABLE);
                intent.setType("*/*");
                intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
                try {
                    startActivityForResult(Intent.createChooser(intent, Tr.t(MainActivity.this, "Select files to send")), REQ_WEB_CHOOSER);
                } catch (Exception e) {
                    webChooserCallback.onReceiveValue(null);
                    webChooserCallback = null;
                    return false;
                }
                return true;
            }
        });

        webView.setWebViewClient(new WebViewClient() {
            /**
             * Only the app's own page loads inside: a link to anywhere else opens in the browser, so no other site
             * ever runs next to the app's bridge.
             */
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                Uri u = request.getUrl();
                String s = u.toString();
                if (s.startsWith("file:///android_asset/")) return false;
                if ("http".equals(u.getScheme()) || "https".equals(u.getScheme())) {
                    try {
                        startActivity(new Intent(Intent.ACTION_VIEW, u).addCategory(Intent.CATEGORY_BROWSABLE).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                    } catch (Exception ignored) { }
                }
                return true;
            }

            @Override
            public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
                Uri u = request.getUrl();
                String host = u.getHost();
                if (THUMB_HOST.equals(host)) return thumbResponse(u.getQueryParameter("p"));
                if (PDF_HOST.equals(host)) {
                    int page = 0, width = 1080;
                    try {
                        page = Integer.parseInt(u.getQueryParameter("p"));
                        width = Integer.parseInt(u.getQueryParameter("w"));
                    } catch (Exception ignored) { }
                    return bytesResponse("image/jpeg", PdfPages.page(u.getQueryParameter("k"), page, width), 200);
                }
                if (CLOUD_HOST.equals(host)) {
                    if ("/thumb".equals(u.getPath())) return cloudThumb(u);
                    return cloudRead(u, request.getRequestHeaders());
                }
                if (PC_THUMB_HOST.equals(host)) return pcThumb(u);
                if (OFFLINE_HOST.equals(host)) return offlineRead(u, request.getRequestHeaders());
                return null;
            }
        });

        webView.setDownloadListener((url, userAgent, contentDisposition, mimetype, contentLength) -> {
            // Only PC addresses go to the transfer queue (which adds the key); anything else goes to the browser.
            if (isOurServer(url) || url.startsWith("/")) {
                TransferHub.get(this).enqueueDownload(routeOf(url), URLUtil.guessFileName(url, contentDisposition, mimetype), contentLength);
            } else {
                try {
                    startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                } catch (Exception ignored) { }
            }
        });

        TransferHub.get(this).addListener(transferListener);
        OfflinePins.get(this).setListener(offlineListener);
        PeerLink.get(this).setListener(peerListener);
        PeerLink.get(this).start();
        FolderSync.listener = e -> emit("folderSync", e);
        // Opening the app ends a "Stop" on the listening note; it listens on in the background when that is wanted.
        getSharedPreferences(PREFS, MODE_PRIVATE).edit().remove("peer_paused").apply();
        PeerLinkService.ensure(this);
        handleIntent(getIntent());
        webView.loadUrl("file:///android_asset/index.html");
    }

    @Override
    protected void onDestroy() {
        TransferHub.get(this).removeListener(transferListener);
        OfflinePins.get(this).setListener(null);
        PeerLink.get(this).setListener(null);
        FolderSync.listener = null;
        super.onDestroy();
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        handleIntent(intent);
    }

    @Override
    protected void onResume() {
        super.onResume();
        // Coming back from Android's permission screens: let the page re-read what it may now see.
        JSONObject o = new JSONObject();
        try { o.put("storageAccess", storageAccess()); } catch (Exception ignored) { }
        emit("resume", o);
        startSpaceSync();
        if (appLock != null) appLock.onResume();
    }

    @Override
    protected void onPause() {
        stopSpaceSync();
        super.onPause();
    }

    @Override
    protected void onStop() {
        if (appLock != null) appLock.onStop();
        super.onStop();
    }

    /** Every screen the app opens itself (pickers, share sheets, other apps): a quick return does not lock it again. */
    @Override
    public void startActivityForResult(Intent intent, int requestCode, Bundle options) {
        if (appLock != null && requestCode != AppLock.REQ_UNLOCK) appLock.ownScreen();
        super.startActivityForResult(intent, requestCode, options);
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        JSONObject o = new JSONObject();
        try { o.put("storageAccess", storageAccess()); } catch (Exception ignored) { }
        emit("resume", o);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent intent) {
        if (requestCode == AppLock.REQ_UNLOCK) {
            if (appLock != null) appLock.onActivityResult(resultCode);
            return;
        }
        if (requestCode == REQ_SEND_TREE) {
            final Uri tree = resultCode == Activity.RESULT_OK && intent != null ? intent.getData() : null;
            if (tree == null) return;
            // Kept readable across a restart, so a cut-off send continues.
            try { getContentResolver().takePersistableUriPermission(tree, Intent.FLAG_GRANT_READ_URI_PERMISSION); } catch (Exception ignored) { }
            final boolean everything = sendFolderEverything;
            worker.execute(() -> {
                try {
                    emit("folderPicked", walkTree(tree, everything));
                } catch (Exception e) {
                    try { emit("folderPicked", new JSONObject().put("error", String.valueOf(e.getMessage()))); } catch (Exception ignored) { }
                }
            });
            return;
        }
        if (requestCode == REQ_SYNC_TREE) {
            final Uri tree = resultCode == Activity.RESULT_OK && intent != null ? intent.getData() : null;
            final String folder = pendingSyncFolder, label = pendingSyncLabel;
            pendingSyncFolder = null;
            if (tree == null || folder == null) return;
            worker.execute(() -> {
                try {
                    String name = treeName(tree);
                    JSONObject pair = FolderSync.add(MainActivity.this, PcProfiles.activeId(MainActivity.this), folder, label, tree, name);
                    emit("folderSync", new JSONObject().put("id", pair.optString("id")).put("state", "added"));
                    FolderSync.run(MainActivity.this, pair.optString("id"), new java.util.concurrent.atomic.AtomicBoolean());
                    FolderSync.schedule(MainActivity.this);
                } catch (Exception e) {
                    toast(Tr.t(MainActivity.this, "Could not sync that folder: {0}", String.valueOf(e.getMessage())));
                }
            });
            return;
        }
        if (requestCode == REQ_TRASH) {
            SpaceDone d = trashDone;
            trashDone = null;
            reportNow = true;
            if (d != null) d.done(resultCode == Activity.RESULT_OK, resultCode == Activity.RESULT_OK
                    ? "In the phone's trash. Android empties it after 30 days (or empty it now in Files or Gallery)." : "Not moved to the trash.", 0);
            return;
        }
        if (requestCode != REQ_PICK_UPLOAD && requestCode != REQ_WEB_CHOOSER) {
            super.onActivityResult(requestCode, resultCode, intent);
            return;
        }
        final List<Uri> uris = new ArrayList<>();
        if (resultCode == Activity.RESULT_OK && intent != null) {
            if (intent.getClipData() != null) {
                for (int i = 0; i < intent.getClipData().getItemCount(); i++) uris.add(intent.getClipData().getItemAt(i).getUri());
            } else if (intent.getData() != null) {
                uris.add(intent.getData());
            }
        }
        if (requestCode == REQ_WEB_CHOOSER) {
            if (webChooserCallback != null) webChooserCallback.onReceiveValue(uris.isEmpty() ? null : uris.toArray(new Uri[0]));
            webChooserCallback = null;
        } else {
            final String target = uploadTarget; // set by "Upload here" in the cloud file browser, for this pick only
            uploadTarget = "";
            if (!uris.isEmpty()) worker.execute(() -> uploadUris(uris, target, true));
        }
    }

    /** The ring may show its full screen over the lock screen (always before Android 14; there the person decides). */
    private boolean fullScreenAllowed() {
        if (Build.VERSION.SDK_INT < 34) return true;
        android.app.NotificationManager nm = (android.app.NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        return nm != null && nm.canUseFullScreenIntent();
    }

    /** Dark icons on a light status / navigation bar, light icons on a dark (or blue) one. */
    @SuppressWarnings("deprecation")
    private void applyBarIcons(boolean statusDark, boolean navDark) {
        if (Build.VERSION.SDK_INT >= 30) {
            android.view.WindowInsetsController c = getWindow().getInsetsController();
            if (c == null) return;
            int light = android.view.WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS;
            int lightNav = android.view.WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS;
            c.setSystemBarsAppearance((statusDark ? light : 0) | (navDark ? lightNav : 0), light | lightNav);
            return;
        }
        android.view.View d = getWindow().getDecorView();
        int f = d.getSystemUiVisibility() & ~android.view.View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR;
        if (statusDark) f |= android.view.View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR;
        if (Build.VERSION.SDK_INT >= 26) {
            f &= ~android.view.View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR;
            if (navDark) f |= android.view.View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR;
        }
        d.setSystemUiVisibility(f);
    }

    /** The name of a folder the person picked in Android's folder picker. */
    /**
     * Every file in a picked folder with its place in it ("MyApp/src"), as Warpinator sends a relative path with each
     * file; build output skipped unless everything. Kept here (with its content address); the page gets names and sizes.
     */
    private JSONObject walkTree(Uri tree, boolean everything) throws Exception {
        String top = TransferHub.cleanFolder(treeName(tree).replace('/', '_'));
        if (top.isEmpty()) top = "Folder";
        java.util.List<String[]> files = new java.util.ArrayList<>();
        java.util.TreeSet<String> skippedFolders = new java.util.TreeSet<>();
        long[] skipped = {0, 0};   // files, bytes: junk files and those over the limit (skipped folders are not walked)
        java.util.ArrayDeque<String[]> todo = new java.util.ArrayDeque<>();
        todo.add(new String[]{DocumentsContract.getTreeDocumentId(tree), top});
        String[] cols = {DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                DocumentsContract.Document.COLUMN_MIME_TYPE, DocumentsContract.Document.COLUMN_SIZE};
        while (!todo.isEmpty()) {
            String[] d = todo.poll();
            Uri kids = DocumentsContract.buildChildDocumentsUriUsingTree(tree, d[0]);
            try (android.database.Cursor c = getContentResolver().query(kids, cols, null, null, null)) {
                if (c == null) continue;
                while (c.moveToNext()) {
                    String id = c.getString(0), name = c.getString(1), mime = c.getString(2);
                    long size = c.isNull(3) ? 0 : c.getLong(3);
                    if (name == null) continue;
                    if (DocumentsContract.Document.MIME_TYPE_DIR.equals(mime)) {
                        if (!everything && SKIP_FOLDERS.contains(name.toLowerCase(Locale.US))) { skippedFolders.add(name); continue; }
                        todo.add(new String[]{id, d[1] + "/" + TransferHub.cleanFolder(name.replace('/', '_'))});
                        continue;
                    }
                    boolean junk = name.equals("Thumbs.db") || name.equals("desktop.ini") || name.equals(".DS_Store")
                            || name.endsWith(".part") || name.endsWith(".tmp") || name.startsWith("~$");
                    if ((!everything && junk) || files.size() >= 5000) { skipped[0]++; skipped[1] += size; continue; }
                    files.add(new String[]{DocumentsContract.buildDocumentUriUsingTree(tree, id).toString(), name, String.valueOf(size), d[1]});
                }
            }
        }
        sendFolderFiles = files;
        JSONArray items = new JSONArray();
        long bytes = 0;
        for (String[] f : files) {
            items.put(new JSONObject().put("name", f[1]).put("size", Long.parseLong(f[2])).put("folder", f[3]));
            bytes += Long.parseLong(f[2]);
        }
        return new JSONObject().put("name", top).put("count", files.size()).put("bytes", bytes).put("items", items)
                .put("skipped", new JSONObject().put("files", skipped[0]).put("bytes", skipped[1]).put("folders", new JSONArray(skippedFolders)));
    }

    private String treeName(Uri tree) {
        try (android.database.Cursor c = getContentResolver().query(
                DocumentsContract.buildDocumentUriUsingTree(tree, DocumentsContract.getTreeDocumentId(tree)),
                new String[]{DocumentsContract.Document.COLUMN_DISPLAY_NAME}, null, null, null)) {
            if (c != null && c.moveToFirst() && c.getString(0) != null) return c.getString(0);
        } catch (Exception ignored) {
            // Shown by its address instead.
        }
        return tree.getLastPathSegment();
    }

    @Override
    public void onBackPressed() {
        // Locked: Back leaves the app (the page under the cover is not touched).
        if (appLock != null && appLock.locked()) {
            moveTaskToBack(true);
            return;
        }
        if (webView == null) {
            super.onBackPressed();
            return;
        }
        webView.evaluateJavascript("window.onAndroidBack ? window.onAndroidBack() : false;", value -> {
            if (!"true".equalsIgnoreCase(value)) MainActivity.super.onBackPressed();
        });
    }
}
