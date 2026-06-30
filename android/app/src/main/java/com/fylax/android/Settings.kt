package com.fylax.android

import android.content.Context

object Settings {

    private const val PREFS = "fylax"
    private const val KEY_LISTS = "lists"
    private const val KEY_DNS_MODE = "dns_mode"
    private const val KEY_DNS_PRIMARY = "dns_primary"
    private const val KEY_DNS_SECONDARY = "dns_secondary"
    private const val KEY_DOH = "doh"
    private const val KEY_ALLOWED = "allowed"
    private const val KEY_BLOCKED_MANUAL = "blocked_manual"
    private const val KEY_THEME = "theme"

    const val MODE_SYSTEM = "system"
    const val MODE_CUSTOM = "custom"

    const val THEME_SYSTEM = 0
    const val THEME_LIGHT = 1
    const val THEME_DARK = 2

    data class BlockList(val url: String, val enabled: Boolean)

    private fun prefs(context: Context) =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    fun lists(context: Context): List<BlockList> {
        val raw = prefs(context).getString(KEY_LISTS, null) ?: return emptyList()
        if (raw.isEmpty()) return emptyList()
        return raw.split("\n").mapNotNull { line ->
            val parts = line.split("\t", limit = 2)
            if (parts.size != 2) null else BlockList(parts[1], parts[0] == "1")
        }
    }

    fun saveLists(context: Context, lists: List<BlockList>) {
        val raw = lists.joinToString("\n") { "${if (it.enabled) "1" else "0"}\t${it.url}" }
        prefs(context).edit().putString(KEY_LISTS, raw).apply()
    }

    fun enabledUrls(context: Context): List<String> =
        lists(context).filter { it.enabled }.map { it.url }

    fun allowed(context: Context): List<String> {
        val raw = prefs(context).getString(KEY_ALLOWED, null) ?: return emptyList()
        if (raw.isEmpty()) return emptyList()
        return raw.split("\n").filter { it.isNotBlank() }
    }

    fun saveAllowed(context: Context, domains: List<String>) {
        prefs(context).edit().putString(KEY_ALLOWED, domains.joinToString("\n")).apply()
    }

    fun blockedManual(context: Context): List<String> {
        val raw = prefs(context).getString(KEY_BLOCKED_MANUAL, null) ?: return emptyList()
        if (raw.isEmpty()) return emptyList()
        return raw.split("\n").filter { it.isNotBlank() }
    }

    fun saveBlockedManual(context: Context, domains: List<String>) {
        prefs(context).edit().putString(KEY_BLOCKED_MANUAL, domains.joinToString("\n")).apply()
    }

    fun themeMode(context: Context): Int = prefs(context).getInt(KEY_THEME, THEME_SYSTEM)

    fun saveThemeMode(context: Context, mode: Int) {
        prefs(context).edit().putInt(KEY_THEME, mode).apply()
    }

    fun dnsMode(context: Context): String =
        prefs(context).getString(KEY_DNS_MODE, MODE_SYSTEM) ?: MODE_SYSTEM

    fun dnsPrimary(context: Context): String =
        prefs(context).getString(KEY_DNS_PRIMARY, "") ?: ""

    fun dnsSecondary(context: Context): String =
        prefs(context).getString(KEY_DNS_SECONDARY, "") ?: ""

    fun dohEnabled(context: Context): Boolean =
        prefs(context).getString(KEY_DOH, "0") == "1"

    fun saveDns(context: Context, mode: String, primary: String, secondary: String, doh: Boolean) {
        prefs(context).edit()
            .putString(KEY_DNS_MODE, mode)
            .putString(KEY_DNS_PRIMARY, primary)
            .putString(KEY_DNS_SECONDARY, secondary)
            .putString(KEY_DOH, if (doh) "1" else "0")
            .apply()
    }
}
