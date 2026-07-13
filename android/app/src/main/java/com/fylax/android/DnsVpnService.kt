package com.fylax.android

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Intent
import android.net.ConnectivityManager
import android.net.LinkProperties
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.net.VpnService
import android.os.ParcelFileDescriptor
import java.io.FileInputStream
import java.io.FileOutputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet6Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.ExecutorService
import java.util.concurrent.SynchronousQueue
import java.util.concurrent.ThreadPoolExecutor
import java.util.concurrent.TimeUnit
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody

class DnsVpnService : VpnService() {

    companion object {
        const val ACTION_START = "com.fylax.android.START"
        const val ACTION_STOP = "com.fylax.android.STOP"

        @Volatile
        var isRunning = false
            private set

        @Volatile
        var useCustomDns = false

        @Volatile
        var useDoh = false

        @Volatile
        var dnsPrimary: InetAddress? = null

        @Volatile
        var dnsSecondary: InetAddress? = null

        @Volatile
        var systemDns: List<InetAddress> = emptyList()

        private const val DNS_PORT = 53
        private const val VPN_ADDRESS = "10.111.222.2"
        private const val VPN_DNS = "10.111.222.1"
        private const val VPN_ADDRESS6 = "fd00:1:1::2"
        private const val VPN_DNS6 = "fd00:1:1::1"
        private const val CHANNEL_ID = "fylax"
        private const val NOTIFICATION_ID = 1
    }

    private var tun: ParcelFileDescriptor? = null
    private var worker: Thread? = null
    private var pool: ExecutorService? = null
    private val writeLock = Any()
    private val refreshing = ConcurrentHashMap.newKeySet<String>()
    private var connectivity: ConnectivityManager? = null
    private var networkCallback: ConnectivityManager.NetworkCallback? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                stopVpn()
                return START_NOT_STICKY
            }
            else -> startVpn()
        }
        return START_STICKY
    }

    private fun startVpn() {
        if (isRunning) return
        useCustomDns = Settings.dnsMode(this) == Settings.MODE_CUSTOM
        useDoh = Settings.dohEnabled(this)
        dnsPrimary = parseDnsOrNull(Settings.dnsPrimary(this))
        dnsSecondary = parseDnsOrNull(Settings.dnsSecondary(this))
        registerNetworkWatch()
        DnsCache.clear()
        Blocklist.setAllowed(Settings.allowed(this))
        Blocklist.setManualBlocked(Settings.blockedManual(this))
        Blocklist.loadCachedFirstAsync(this, Settings.enabledUrls(this))
        createChannel()
        startForeground(NOTIFICATION_ID, buildNotification())
        val descriptor = Builder()
            .setSession("Fylax")
            .addAddress(VPN_ADDRESS, 32)
            .addAddress(VPN_ADDRESS6, 128)
            .addDnsServer(VPN_DNS)
            .addDnsServer(VPN_DNS6)
            .addRoute(VPN_DNS, 32)
            .addRoute(VPN_DNS6, 128)
            .setBlocking(true)
            .establish() ?: run {
                stopSelf()
                return
            }
        tun = descriptor
        pool = ThreadPoolExecutor(
            0, 32, 30L, TimeUnit.SECONDS,
            SynchronousQueue(), ThreadPoolExecutor.CallerRunsPolicy()
        )
        isRunning = true
        worker = Thread { runLoop(descriptor) }.apply { start() }
    }

    private fun stopVpn() {
        isRunning = false
        unregisterNetworkWatch()
        try {
            tun?.close()
        } catch (_: Exception) {
        }
        tun = null
        pool?.shutdownNow()
        pool = null
        worker = null
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun registerNetworkWatch() {
        val manager = getSystemService(ConnectivityManager::class.java)
        connectivity = manager
        updateSystemDns(manager)
        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) = updateSystemDns(manager)
            override fun onLost(network: Network) = updateSystemDns(manager)
            override fun onLinkPropertiesChanged(network: Network, linkProperties: LinkProperties) =
                updateSystemDns(manager)
        }
        networkCallback = callback
        manager.registerNetworkCallback(
            NetworkRequest.Builder()
                .addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
                .build(),
            callback
        )
    }

    private fun unregisterNetworkWatch() {
        val callback = networkCallback
        if (callback != null) {
            try {
                connectivity?.unregisterNetworkCallback(callback)
            } catch (_: Exception) {
            }
        }
        networkCallback = null
        connectivity = null
    }

    private fun updateSystemDns(manager: ConnectivityManager) {
        for (network in manager.allNetworks) {
            val capabilities = manager.getNetworkCapabilities(network) ?: continue
            if (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_VPN)) continue
            if (!capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)) continue
            val properties = manager.getLinkProperties(network) ?: continue
            if (properties.dnsServers.isNotEmpty()) {
                systemDns = properties.dnsServers
                return
            }
        }
    }

    private fun runLoop(descriptor: ParcelFileDescriptor) {
        val input = FileInputStream(descriptor.fileDescriptor)
        val output = FileOutputStream(descriptor.fileDescriptor)
        val buffer = ByteArray(32767)
        while (isRunning) {
            val length = try {
                input.read(buffer)
            } catch (_: Exception) {
                break
            }
            if (length <= 0) continue
            try {
                handlePacket(buffer.copyOf(length), output)
            } catch (_: Exception) {
            }
        }
    }

    private fun handlePacket(packet: ByteArray, output: FileOutputStream) {
        if (packet.isEmpty()) return
        when ((packet[0].toInt() shr 4) and 0xF) {
            4 -> handleV4(packet, output)
            6 -> handleV6(packet, output)
        }
    }

    private fun handleV4(packet: ByteArray, output: FileOutputStream) {
        val ihl = (packet[0].toInt() and 0xF) * 4
        if (packet.size < ihl + 8) return
        if ((packet[9].toInt() and 0xFF) != 17) return

        val udpStart = ihl
        val dstPort = ((packet[udpStart + 2].toInt() and 0xFF) shl 8) or (packet[udpStart + 3].toInt() and 0xFF)
        if (dstPort != DNS_PORT) return

        val dnsStart = udpStart + 8
        if (dnsStart >= packet.size) return
        val dns = packet.copyOfRange(dnsStart, packet.size)
        val question = parseQuestion(dns) ?: return
        respond(dns, question) { reply -> writeResponse4(packet, ihl, udpStart, reply, output) }
    }

    private fun handleV6(packet: ByteArray, output: FileOutputStream) {
        if (packet.size < 48) return
        if ((packet[6].toInt() and 0xFF) != 17) return

        val udpStart = 40
        val dstPort = ((packet[udpStart + 2].toInt() and 0xFF) shl 8) or (packet[udpStart + 3].toInt() and 0xFF)
        if (dstPort != DNS_PORT) return

        val dnsStart = udpStart + 8
        if (dnsStart >= packet.size) return
        val dns = packet.copyOfRange(dnsStart, packet.size)
        val question = parseQuestion(dns) ?: return
        respond(dns, question) { reply -> writeResponse6(packet, reply, output) }
    }

    private fun respond(dns: ByteArray, question: Question, writer: (ByteArray) -> Unit) {
        val type = qtypeName(question.qtype)
        if (Blocklist.isBlocked(question.host)) {
            QueryLog.record(question.host, type, true)
            writer(buildSinkhole(dns, question.end, question.qtype))
            return
        }
        val cached = DnsCache.get(question.host, question.qtype)
        if (cached != null) {
            QueryLog.record(question.host, type, false)
            val response = cached.data.copyOf()
            response[0] = dns[0]
            response[1] = dns[1]
            writer(response)
            if (cached.stale) {
                val key = "${question.qtype}:${question.host}"
                if (refreshing.add(key)) {
                    pool?.execute {
                        try {
                            DnsCache.put(question.host, question.qtype, resolve(dns))
                        } catch (_: Exception) {
                        } finally {
                            refreshing.remove(key)
                        }
                    }
                }
            }
            return
        }
        pool?.execute {
            try {
                val reply = resolve(dns)
                if (cnameBlocked(reply)) {
                    QueryLog.record(question.host, type, true)
                    writer(buildSinkhole(dns, question.end, question.qtype))
                    return@execute
                }
                QueryLog.record(question.host, type, false)
                val boosted = DnsCache.put(question.host, question.qtype, reply)
                writer(boosted ?: reply)
            } catch (_: Exception) {
            }
        }
    }

    private fun qtypeName(t: Int): String = when (t) {
        1 -> "A"
        28 -> "AAAA"
        5 -> "CNAME"
        65 -> "HTTPS"
        15 -> "MX"
        16 -> "TXT"
        33 -> "SRV"
        12 -> "PTR"
        2 -> "NS"
        6 -> "SOA"
        else -> "TYPE$t"
    }

    private fun cnameBlocked(response: ByteArray): Boolean {
        try {
            if (response.size < 12) return false
            val qd = ((response[4].toInt() and 0xFF) shl 8) or (response[5].toInt() and 0xFF)
            val an = ((response[6].toInt() and 0xFF) shl 8) or (response[7].toInt() and 0xFF)
            var pos = 12
            repeat(qd) {
                pos = skipName(response, pos) + 4
            }
            repeat(an) {
                pos = skipName(response, pos)
                if (pos + 10 > response.size) return false
                val type = ((response[pos].toInt() and 0xFF) shl 8) or (response[pos + 1].toInt() and 0xFF)
                val rdlength = ((response[pos + 8].toInt() and 0xFF) shl 8) or (response[pos + 9].toInt() and 0xFF)
                val rdataStart = pos + 10
                if (type == 5) {
                    val target = readName(response, rdataStart).first
                    if (target.isNotEmpty() && Blocklist.isBlocked(target)) return true
                }
                pos = rdataStart + rdlength
            }
        } catch (_: Exception) {
        }
        return false
    }

    private fun skipName(data: ByteArray, start: Int): Int = readName(data, start).second

    private fun readName(data: ByteArray, start: Int): Pair<String, Int> {
        val sb = StringBuilder()
        var pos = start
        var jumped = false
        var after = start
        var safety = 0
        while (pos < data.size && safety++ < 128) {
            val len = data[pos].toInt() and 0xFF
            if (len == 0) {
                pos++
                if (!jumped) after = pos
                break
            }
            if ((len and 0xC0) == 0xC0) {
                if (pos + 1 >= data.size) break
                val ptr = ((len and 0x3F) shl 8) or (data[pos + 1].toInt() and 0xFF)
                if (!jumped) after = pos + 2
                pos = ptr
                jumped = true
                continue
            }
            pos++
            if (pos + len > data.size) break
            if (sb.isNotEmpty()) sb.append('.')
            sb.append(String(data, pos, len, Charsets.US_ASCII))
            pos += len
        }
        return Pair(sb.toString().lowercase(), if (jumped) after else pos)
    }

    private fun resolve(query: ByteArray): ByteArray {
        if (useCustomDns) {
            val primary = dnsPrimary
            val secondary = dnsSecondary
            if (primary != null) {
                try {
                    return sendQuery(query, primary)
                } catch (_: Exception) {
                }
            }
            if (secondary != null) {
                return sendQuery(query, secondary)
            }
            throw IllegalStateException("no custom dns")
        }
        var last: Exception? = null
        for (server in systemDns) {
            try {
                return forwardUdp(query, server)
            } catch (e: Exception) {
                last = e
            }
        }
        throw last ?: IllegalStateException("no system dns")
    }

    private fun sendQuery(payload: ByteArray, server: InetAddress): ByteArray =
        if (useDoh) forwardDoh(payload, server) else forwardUdp(payload, server)

    private fun forwardUdp(payload: ByteArray, server: InetAddress): ByteArray {
        DatagramSocket().use { socket ->
            protect(socket)
            socket.soTimeout = 5000
            socket.send(DatagramPacket(payload, payload.size, InetSocketAddress(server, DNS_PORT)))
            val buffer = ByteArray(4096)
            val reply = DatagramPacket(buffer, buffer.size)
            socket.receive(reply)
            return buffer.copyOf(reply.length)
        }
    }

    private val dohMediaType = "application/dns-message".toMediaType()

    private val dohClient = OkHttpClient.Builder()
        .connectTimeout(5, TimeUnit.SECONDS)
        .readTimeout(5, TimeUnit.SECONDS)
        .callTimeout(6, TimeUnit.SECONDS)
        .build()

    private fun forwardDoh(payload: ByteArray, server: InetAddress): ByteArray {
        val host = server.hostAddress
        val target = if (server is Inet6Address) "https://[$host]/dns-query" else "https://$host/dns-query"
        val request = Request.Builder()
            .url(target)
            .post(payload.toRequestBody(dohMediaType))
            .header("Accept", "application/dns-message")
            .build()
        dohClient.newCall(request).execute().use { response ->
            if (!response.isSuccessful) throw IllegalStateException("doh ${response.code}")
            return response.body.bytes()
        }
    }

    private fun parseDnsOrNull(value: String): InetAddress? =
        try {
            val trimmed = value.trim()
            if (trimmed.isEmpty()) null else InetAddress.getByName(trimmed)
        } catch (_: Exception) {
            null
        }

    private fun writeResponse4(
        request: ByteArray,
        ihl: Int,
        udpStart: Int,
        payload: ByteArray,
        output: FileOutputStream
    ) {
        val total = ihl + 8 + payload.size
        val out = ByteArray(total)
        System.arraycopy(request, 0, out, 0, ihl)
        System.arraycopy(request, udpStart, out, udpStart, 8)
        System.arraycopy(payload, 0, out, udpStart + 8, payload.size)

        out[2] = (total shr 8).toByte()
        out[3] = total.toByte()
        out[10] = 0
        out[11] = 0

        for (i in 0 until 4) {
            val tmp = out[12 + i]
            out[12 + i] = out[16 + i]
            out[16 + i] = tmp
        }
        for (i in 0 until 2) {
            val tmp = out[udpStart + i]
            out[udpStart + i] = out[udpStart + 2 + i]
            out[udpStart + 2 + i] = tmp
        }

        val udpLength = 8 + payload.size
        out[udpStart + 4] = (udpLength shr 8).toByte()
        out[udpStart + 5] = udpLength.toByte()
        out[udpStart + 6] = 0
        out[udpStart + 7] = 0

        val checksum = ipChecksum(out, ihl)
        out[10] = (checksum shr 8).toByte()
        out[11] = checksum.toByte()

        synchronized(writeLock) {
            output.write(out)
            output.flush()
        }
    }

    private fun writeResponse6(request: ByteArray, payload: ByteArray, output: FileOutputStream) {
        val udpStart = 40
        val udpLength = 8 + payload.size
        val total = udpStart + udpLength
        val out = ByteArray(total)
        System.arraycopy(request, 0, out, 0, udpStart)
        System.arraycopy(request, udpStart, out, udpStart, 8)
        System.arraycopy(payload, 0, out, udpStart + 8, payload.size)

        out[4] = (udpLength shr 8).toByte()
        out[5] = udpLength.toByte()

        for (i in 0 until 16) {
            val tmp = out[8 + i]
            out[8 + i] = out[24 + i]
            out[24 + i] = tmp
        }
        for (i in 0 until 2) {
            val tmp = out[udpStart + i]
            out[udpStart + i] = out[udpStart + 2 + i]
            out[udpStart + 2 + i] = tmp
        }

        out[udpStart + 4] = (udpLength shr 8).toByte()
        out[udpStart + 5] = udpLength.toByte()
        out[udpStart + 6] = 0
        out[udpStart + 7] = 0

        val checksum = udpChecksum6(out, udpStart, udpLength)
        out[udpStart + 6] = (checksum shr 8).toByte()
        out[udpStart + 7] = checksum.toByte()

        synchronized(writeLock) {
            output.write(out)
            output.flush()
        }
    }

    private fun ipChecksum(data: ByteArray, length: Int): Int {
        var sum = 0
        var i = 0
        while (i < length - 1) {
            sum += ((data[i].toInt() and 0xFF) shl 8) or (data[i + 1].toInt() and 0xFF)
            i += 2
        }
        if (i < length) sum += (data[i].toInt() and 0xFF) shl 8
        while ((sum shr 16) != 0) sum = (sum and 0xFFFF) + (sum shr 16)
        return sum.inv() and 0xFFFF
    }

    private fun udpChecksum6(packet: ByteArray, udpStart: Int, udpLength: Int): Int {
        var sum = 0L
        var i = 8
        while (i < 40) {
            sum += ((packet[i].toInt() and 0xFF) shl 8) or (packet[i + 1].toInt() and 0xFF)
            i += 2
        }
        sum += (udpLength.toLong() ushr 16) and 0xFFFF
        sum += udpLength.toLong() and 0xFFFF
        sum += 17L
        var j = udpStart
        val end = udpStart + udpLength
        while (j < end - 1) {
            sum += ((packet[j].toInt() and 0xFF) shl 8) or (packet[j + 1].toInt() and 0xFF)
            j += 2
        }
        if (j < end) {
            sum += (packet[j].toInt() and 0xFF) shl 8
        }
        while ((sum shr 16) != 0L) sum = (sum and 0xFFFF) + (sum shr 16)
        var result = sum.inv().toInt() and 0xFFFF
        if (result == 0) result = 0xFFFF
        return result
    }

    private data class Question(val host: String, val qtype: Int, val end: Int)

    private fun parseQuestion(dns: ByteArray): Question? {
        if (dns.size < 13) return null
        var pos = 12
        val builder = StringBuilder()
        while (pos < dns.size) {
            val length = dns[pos].toInt() and 0xFF
            if (length == 0) {
                pos++
                break
            }
            if (length and 0xC0 != 0) return null
            pos++
            if (pos + length > dns.size) return null
            if (builder.isNotEmpty()) builder.append('.')
            builder.append(String(dns, pos, length, Charsets.US_ASCII))
            pos += length
        }
        if (pos + 4 > dns.size) return null
        if (builder.isEmpty()) return null
        val qtype = ((dns[pos].toInt() and 0xFF) shl 8) or (dns[pos + 1].toInt() and 0xFF)
        return Question(builder.toString().lowercase(), qtype, pos + 4)
    }

    private fun buildSinkhole(query: ByteArray, questionEnd: Int, qtype: Int): ByteArray {
        val rdata: ByteArray? = when (qtype) {
            1 -> ByteArray(4)
            28 -> ByteArray(16)
            else -> null
        }
        val answerLen = if (rdata != null) 12 + rdata.size else 0
        val response = ByteArray(questionEnd + answerLen)
        System.arraycopy(query, 0, response, 0, questionEnd)
        response[2] = (0x80 or (query[2].toInt() and 0x01)).toByte()
        response[3] = 0x80.toByte()
        response[4] = 0
        response[5] = 1
        response[6] = 0
        response[7] = (if (rdata != null) 1 else 0).toByte()
        response[8] = 0
        response[9] = 0
        response[10] = 0
        response[11] = 0
        if (rdata != null) {
            val p = questionEnd
            response[p] = 0xC0.toByte()
            response[p + 1] = 0x0C.toByte()
            response[p + 2] = ((qtype shr 8) and 0xFF).toByte()
            response[p + 3] = (qtype and 0xFF).toByte()
            response[p + 4] = 0
            response[p + 5] = 1
            response[p + 6] = 0
            response[p + 7] = 0
            response[p + 8] = 0x01
            response[p + 9] = 0x2C
            response[p + 10] = ((rdata.size shr 8) and 0xFF).toByte()
            response[p + 11] = (rdata.size and 0xFF).toByte()
            System.arraycopy(rdata, 0, response, p + 12, rdata.size)
        }
        return response
    }

    private fun createChannel() {
        val manager = getSystemService(NotificationManager::class.java)
        val channel = NotificationChannel(CHANNEL_ID, "DNS Block", NotificationManager.IMPORTANCE_LOW)
        manager.createNotificationChannel(channel)
    }

    private fun buildNotification(): Notification {
        val intent = PendingIntent.getActivity(
            this,
            0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE
        )
        return Notification.Builder(this, CHANNEL_ID)
            .setContentTitle("DNS Block active")
            .setContentText("Filtering DNS queries")
            .setSmallIcon(R.drawable.ic_shield)
            .setContentIntent(intent)
            .setOngoing(true)
            .build()
    }

    override fun onDestroy() {
        stopVpn()
        super.onDestroy()
    }
}
