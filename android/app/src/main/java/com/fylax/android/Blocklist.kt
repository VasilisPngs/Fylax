package com.fylax.android

import java.io.BufferedReader
import java.net.HttpURLConnection
import java.net.URL

object Blocklist {

    private val WHITESPACE = Regex("\\s+")

    @Volatile
    private var blocked: Set<String> = emptySet()

    @Volatile
    private var allowed: Set<String> = emptySet()

    @Volatile
    private var manualBlocked: Set<String> = emptySet()

    fun setAllowed(domains: Collection<String>) {
        allowed = domains.map { it.trim().lowercase() }.filter { it.isNotEmpty() }.toSet()
    }

    fun setManualBlocked(domains: Collection<String>) {
        manualBlocked = domains.map { it.trim().lowercase() }.filter { it.isNotEmpty() }.toSet()
    }

    fun isBlocked(host: String): Boolean {
        if (matches(allowed, host)) return false
        if (matches(manualBlocked, host)) return true
        return matches(blocked, host)
    }

    private fun matches(set: Set<String>, host: String): Boolean {
        if (set.isEmpty()) return false
        if (set.contains(host)) return true
        var index = host.indexOf('.')
        while (index >= 0) {
            if (set.contains(host.substring(index + 1))) return true
            index = host.indexOf('.', index + 1)
        }
        return false
    }

    fun loadAsync(urls: List<String>, onResult: (Int) -> Unit = {}) {
        Thread { onResult(load(urls)) }.start()
    }

    fun load(urls: List<String>): Int {
        if (urls.isEmpty()) {
            blocked = emptySet()
            return 0
        }
        val partials = arrayOfNulls<HashSet<String>>(urls.size)
        val threads = urls.mapIndexed { index, url ->
            Thread {
                val local = HashSet<String>()
                try {
                    openReader(url).forEachLine { line -> parseLine(line)?.let { local.add(it) } }
                } catch (_: Exception) {
                }
                partials[index] = local
            }.apply { start() }
        }
        threads.forEach { it.join() }
        val result = HashSet<String>()
        for (partial in partials) {
            if (partial != null) result.addAll(partial)
        }
        blocked = result
        return result.size
    }

    private fun openReader(url: String): BufferedReader {
        var current = url
        var redirects = 0
        while (true) {
            val connection = (URL(current).openConnection() as HttpURLConnection).apply {
                instanceFollowRedirects = false
                connectTimeout = 15000
                readTimeout = 20000
                setRequestProperty("User-Agent", "Fylax")
                setRequestProperty("Accept", "text/plain, */*")
            }
            val code = connection.responseCode
            if (code in 300..399 && redirects < 5) {
                val location = connection.getHeaderField("Location")
                connection.disconnect()
                if (location.isNullOrEmpty()) throw IllegalStateException("redirect without location")
                current = URL(URL(current), location).toString()
                redirects++
                continue
            }
            if (code !in 200..299) {
                connection.disconnect()
                throw IllegalStateException("http $code")
            }
            return connection.inputStream.bufferedReader()
        }
    }

    private fun parseLine(raw: String): String? {
        val line = raw.trim()
        if (line.isEmpty()) return null
        val first = line[0]
        if (first == '#' || first == '!') return null
        if (line.startsWith("@@")) return null
        if (line.contains("##") || line.contains("#@#") || line.contains("#?#") ||
            line.contains("#$#") || line.contains("#%#")
        ) return null
        if (first == '/') return null

        if (first.isDigit() || first == ':') {
            val parts = line.split(WHITESPACE)
            return if (parts.size >= 2) sanitizeHost(parts[1]) else null
        }

        if (line.startsWith("||")) {
            var body = line.substring(2)
            val dollar = body.indexOf('$')
            if (dollar >= 0) body = body.substring(0, dollar)
            body = body.trimEnd('^', '|')
            return sanitizeHost(body)
        }

        if (line.split(WHITESPACE).size != 1) return null
        return sanitizeHost(line)
    }

    private fun sanitizeHost(value: String): String? {
        val host = value.trim().lowercase().trim('.')
        if (host.isEmpty() || '.' !in host) return null
        if (host.startsWith("-") || host.endsWith("-")) return null
        if (host.any { it !in 'a'..'z' && it !in '0'..'9' && it != '.' && it != '-' && it != '_' }) return null
        return host
    }
}
