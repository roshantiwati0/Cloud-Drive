package com.cloudssd.mobile;

import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.NetworkInterface;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Locale;

/**
 * The ways this phone can reach a PC without the internet: its Wi-Fi, its own hotspot (the PC joined it), USB
 * tethering, Ethernet or Bluetooth tethering. Each is a network interface with a local address; the mobile-data
 * interface never counts (its "local" range is the carrier's).
 */
final class LinkScanner {
    private LinkScanner() {
    }

    static final class Link {
        final String name, kind, ip;

        Link(String name, String kind, String ip) {
            this.name = name;
            this.kind = kind;
            this.ip = ip;
        }
    }

    /** wifi | hotspot | usb | ethernet | bluetooth, or null for an interface that is not a direct link. */
    static String kindOf(String name) {
        String n = name == null ? "" : name.toLowerCase(Locale.US);
        if (n.startsWith("ap") || n.startsWith("swlan") || n.startsWith("softap")) return "hotspot";
        if (n.startsWith("wlan") || n.startsWith("wifi")) return "wifi";
        if (n.startsWith("rndis") || n.startsWith("usb") || n.startsWith("ncm")) return "usb";
        if (n.startsWith("eth")) return "ethernet";
        if (n.startsWith("bt-pan") || n.startsWith("bnep")) return "bluetooth";
        return null;
    }

    /** Every direct link that is up now, with this phone's address on it. */
    static List<Link> links() {
        List<Link> out = new ArrayList<>();
        try {
            for (NetworkInterface ni : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!ni.isUp() || ni.isLoopback()) continue;
                String kind = kindOf(ni.getName());
                if (kind == null) continue;
                for (InetAddress a : Collections.list(ni.getInetAddresses())) {
                    if (a instanceof Inet4Address && a.isSiteLocalAddress()) out.add(new Link(ni.getName(), kind, a.getHostAddress()));
                }
            }
        } catch (Exception ignored) {
        }
        return out;
    }

    static boolean hasDirect() {
        return !links().isEmpty();
    }

    /** "wlan0=192.168.1.4,rndis0=…": changes when a link comes or goes (so a proven address is checked again). */
    static String signature() {
        StringBuilder sb = new StringBuilder();
        for (Link l : links()) sb.append(l.name).append('=').append(l.ip).append(',');
        return sb.toString();
    }

    /** True when host (an IPv4 address) is in the same /24 as one of this phone's direct links. */
    static boolean sameSubnet(String host) {
        int dot = host.lastIndexOf('.');
        if (dot < 0) return false;
        String base = host.substring(0, dot + 1);
        for (Link l : links()) if (l.ip.startsWith(base)) return true;
        return false;
    }

    /**
     * The USB cable: {connected (to a computer), tethering (USB tethering is on)}. From the system's sticky USB state;
     * {connected:false} when it is not known.
     */
    static JSONObject usb(Context c) {
        JSONObject o = new JSONObject();
        try {
            Intent i = c.registerReceiver(null, new IntentFilter("android.hardware.usb.action.USB_STATE"));
            boolean connected = i != null && i.getBooleanExtra("connected", false);
            boolean tethering = i != null && (i.getBooleanExtra("rndis", false) || i.getBooleanExtra("ncm", false));
            for (Link l : links()) if ("usb".equals(l.kind)) tethering = true;
            o.put("connected", connected);
            o.put("tethering", tethering);
        } catch (Exception e) {
            try {
                o.put("connected", false);
            } catch (JSONException ignored) {
            }
        }
        return o;
    }

    /** For the page: {links: [{name, kind, ip}], usb: {connected, tethering}}. */
    static JSONObject forPage(Context c) {
        JSONObject o = new JSONObject();
        try {
            JSONArray arr = new JSONArray();
            for (Link l : links()) arr.put(new JSONObject().put("name", l.name).put("kind", l.kind).put("ip", l.ip));
            o.put("links", arr);
            o.put("usb", usb(c));
        } catch (JSONException ignored) {
        }
        return o;
    }
}
