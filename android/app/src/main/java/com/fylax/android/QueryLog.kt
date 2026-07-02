package com.fylax.android

object QueryLog {

    private const val MAX = 1000

    data class Entry(
        val host: String,
        val type: String,
        val blocked: Boolean,
        val time: Long
    )

    private val buffer = ArrayDeque<Entry>()
    private var requests = 0L
    private var blocked = 0L

    @Synchronized
    fun record(host: String, type: String, isBlocked: Boolean) {
        if (buffer.size >= MAX) buffer.removeFirst()
        buffer.addLast(Entry(host, type, isBlocked, System.currentTimeMillis()))
        requests++
        if (isBlocked) blocked++
    }

    @Synchronized
    fun snapshot(): List<Entry> = buffer.reversed()

    @Synchronized
    fun counts(): Pair<Long, Long> = requests to blocked

    @Synchronized
    fun clear() {
        buffer.clear()
        requests = 0
        blocked = 0
    }
}
