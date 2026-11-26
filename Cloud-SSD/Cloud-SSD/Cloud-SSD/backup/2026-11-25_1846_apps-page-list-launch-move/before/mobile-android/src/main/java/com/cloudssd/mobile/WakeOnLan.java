package com.cloudssd.mobile;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.util.ArrayList;
import java.util.List;

/**
 * Wakes a sleeping PC on this Wi-Fi (Wake-on-LAN): the "magic packet" — six 0xFF bytes, then the PC's network card
 * address 16 times — sent to everyone on the network. The PC told the app its card and broadcast address while it was
 * on (its status "wol"). It works when the PC's card and BIOS allow it, which is usual for a cable and rare for Wi-Fi.
 */
final class WakeOnLan {
    private static final int[] PORTS = {9, 7};

    private WakeOnLan() {
    }

    /** "AA:BB:CC:DD:EE:FF" (or with "-", or none) → the 102-byte packet; null when it is not six bytes. */
    static byte[] packet(String mac) {
        String hex = mac == null ? "" : mac.replaceAll("[:\\-\\s]", "");
        if (!hex.matches("[0-9A-Fa-f]{12}")) return null;
        byte[] card = new byte[6];
        for (int i = 0; i < 6; i++) card[i] = (byte) Integer.parseInt(hex.substring(i * 2, i * 2 + 2), 16);
        byte[] p = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++) p[i] = (byte) 0xFF;
        for (int i = 0; i < 16; i++) System.arraycopy(card, 0, p, 6 + i * 6, 6);
        return p;
    }

    /**
     * Sends the packet three times to the network's broadcast address and to 255.255.255.255, on ports 9 and 7.
     * True when at least one went out.
     */
    static boolean send(String mac, String broadcast) {
        byte[] p = packet(mac);
        if (p == null) return false;
        List<InetAddress> to = new ArrayList<>();
        try {
            // Only an IPv4 address as digits: never a name to look up.
            if (broadcast != null && broadcast.matches("\\d{1,3}(\\.\\d{1,3}){3}")) to.add(InetAddress.getByName(broadcast));
            to.add(InetAddress.getByName("255.255.255.255"));
        } catch (Exception e) {
            return false;
        }
        int sent = 0;
        try (DatagramSocket socket = new DatagramSocket()) {
            socket.setBroadcast(true);
            for (int round = 0; round < 3; round++) {
                for (InetAddress a : to) {
                    for (int port : PORTS) {
                        try {
                            socket.send(new DatagramPacket(p, p.length, a, port));
                            sent++;
                        } catch (Exception ignored) {
                            // That address is not reachable from this network (no Wi-Fi): the others may be.
                        }
                    }
                }
                try {
                    Thread.sleep(60);
                } catch (InterruptedException e) {
                    Thread.currentThread().interrupt();
                    break;
                }
            }
        } catch (Exception e) {
            return sent > 0;
        }
        return sent > 0;
    }
}
