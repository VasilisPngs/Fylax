package com.fylax.android

object DnsCache {

    private const val MIN_TTL = 300L
    private const val MAX_TTL = 86_400L
    private const val STALE_TTL = 10L
    private const val MAX_ENTRIES = 4096
    private const val STALE_GRACE_MS = 86_400_000L

    class Result(val data: ByteArray, val stale: Boolean)

    private class Entry(val data: ByteArray, val expiresAt: Long)

    private val map = object : LinkedHashMap<String, Entry>(256, 0.75f, true) {
        override fun removeEldestEntry(eldest: MutableMap.MutableEntry<String, Entry>): Boolean =
            size > MAX_ENTRIES
    }

    @Synchronized
    fun get(host: String, qtype: Int): Result? {
        val key = "$qtype:$host"
        val entry = map[key] ?: return null
        val now = System.currentTimeMillis()
        if (now < entry.expiresAt) return Result(entry.data, false)
        if (now < entry.expiresAt + STALE_GRACE_MS) {
            val stale = entry.data.copyOf()
            boostTtls(stale, STALE_TTL, STALE_TTL)
            return Result(stale, true)
        }
        map.remove(key)
        return null
    }

    @Synchronized
    fun put(host: String, qtype: Int, response: ByteArray): ByteArray? {
        val boosted = response.copyOf()
        val ttl = boostTtls(boosted, MIN_TTL, MAX_TTL) ?: return null
        map["$qtype:$host"] = Entry(boosted, System.currentTimeMillis() + ttl * 1000L)
        return boosted
    }

    @Synchronized
    fun clear() = map.clear()

    private fun boostTtls(dns: ByteArray, floor: Long, cap: Long): Long? {
        if (dns.size < 12) return null
        val answers = ((dns[6].toInt() and 0xFF) shl 8) or (dns[7].toInt() and 0xFF)
        if (answers <= 0) return null

        var pos = 12
        while (pos < dns.size) {
            val length = dns[pos].toInt() and 0xFF
            if (length == 0) {
                pos++
                break
            }
            if (length and 0xC0 != 0) {
                pos += 2
                break
            }
            pos += length + 1
        }
        pos += 4

        var minTtl = Long.MAX_VALUE
        var index = 0
        while (index < answers) {
            pos = skipName(dns, pos) ?: return null
            if (pos + 10 > dns.size) return null
            val original = ((dns[pos + 4].toInt() and 0xFF).toLong() shl 24) or
                ((dns[pos + 5].toInt() and 0xFF).toLong() shl 16) or
                ((dns[pos + 6].toInt() and 0xFF).toLong() shl 8) or
                (dns[pos + 7].toInt() and 0xFF).toLong()
            val clamped = original.coerceIn(floor, cap)
            dns[pos + 4] = ((clamped shr 24) and 0xFFL).toByte()
            dns[pos + 5] = ((clamped shr 16) and 0xFFL).toByte()
            dns[pos + 6] = ((clamped shr 8) and 0xFFL).toByte()
            dns[pos + 7] = (clamped and 0xFFL).toByte()
            val rdlength = ((dns[pos + 8].toInt() and 0xFF) shl 8) or (dns[pos + 9].toInt() and 0xFF)
            if (clamped < minTtl) minTtl = clamped
            pos += 10 + rdlength
            index++
        }
        if (minTtl == Long.MAX_VALUE) return null
        return minTtl
    }

    private fun skipName(dns: ByteArray, start: Int): Int? {
        var pos = start
        while (pos < dns.size) {
            val length = dns[pos].toInt() and 0xFF
            when {
                length == 0 -> return pos + 1
                length and 0xC0 == 0xC0 -> return pos + 2
                else -> pos += length + 1
            }
        }
        return null
    }
}
