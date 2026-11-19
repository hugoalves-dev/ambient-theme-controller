package dev.hugodotnet.ambienttheme.core

/** Separates addresses that can be on the computer's local network from carrier/VPN ones. */
object Addresses {
    /** RFC 1918 private ranges (10/8, 172.16/12, 192.168/16), which is what Wi-Fi and hotspots use. */
    fun isPrivateLan(ip: String): Boolean {
        val parts = ip.split(".").mapNotNull { it.toIntOrNull() }
        if (parts.size != 4) return false
        return parts[0] == 10 || (parts[0] == 172 && parts[1] in 16..31) || (parts[0] == 192 && parts[1] == 168)
    }
}
