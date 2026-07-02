package com.fylax.android

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.net.InetAddresses
import android.net.VpnService
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.work.Constraints
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import java.net.InetAddress
import java.util.concurrent.TimeUnit


private val FylaxDarkColors = darkColorScheme(
    background = Color(0xFF121212),
    surface = Color(0xFF1B1B1B),
    surfaceVariant = Color(0xFF464646),
    surfaceContainer = Color(0xFF1B1B1B),
    surfaceContainerLow = Color(0xFF121212),
    surfaceContainerHigh = Color(0xFF292929),
    surfaceContainerHighest = Color(0xFF464646),
    onSurface = Color(0xFFFAFAFA),
    onSurfaceVariant = Color(0xFFD6D6D6),
    primary = Color(0xFFE2E2E2),
    onPrimary = Color(0xFF121212),
    primaryContainer = Color(0xFF292929),
    onPrimaryContainer = Color(0xFFFAFAFA),
    secondaryContainer = Color(0xFF292929),
    onSecondaryContainer = Color(0xFFFAFAFA),
    tertiary = Color(0xFFE2E2E2),
    errorContainer = Color(0xFF292929),
    onErrorContainer = Color(0xFFFAFAFA),
    outline = Color(0xFF919191),
    outlineVariant = Color(0xFF292929)
)

private val FylaxLightColors = lightColorScheme(
    background = Color(0xFFFAFAFA),
    surface = Color(0xFFF3F3F3),
    surfaceVariant = Color(0xFFE2E2E2),
    surfaceContainer = Color(0xFFF3F3F3),
    surfaceContainerLow = Color(0xFFFAFAFA),
    surfaceContainerHigh = Color(0xFFF3F3F3),
    surfaceContainerHighest = Color(0xFFE2E2E2),
    onSurface = Color(0xFF121212),
    onSurfaceVariant = Color(0xFF5E5E5E),
    primary = Color(0xFF1B1B1B),
    onPrimary = Color(0xFFFAFAFA),
    primaryContainer = Color(0xFFE2E2E2),
    onPrimaryContainer = Color(0xFF1B1B1B),
    secondaryContainer = Color(0xFFE2E2E2),
    onSecondaryContainer = Color(0xFF1B1B1B),
    tertiary = Color(0xFF1B1B1B),
    errorContainer = Color(0xFFE2E2E2),
    onErrorContainer = Color(0xFF1B1B1B),
    outline = Color(0xFF767676),
    outlineVariant = Color(0xFFE2E2E2)
)
class MainActivity : ComponentActivity() {

    private val active = mutableStateOf(DnsVpnService.isRunning)
    private val privateDns = mutableStateOf(false)
    private val mainHandler = Handler(Looper.getMainLooper())

    private val vpnLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == RESULT_OK) startVpn()
    }

    private val notificationLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) {}

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        privateDns.value = privateDnsActive(this)
        requestNotifications()
        scheduleUpdates()
        setContent {
            val ctx = this
            var themeMode by remember { mutableStateOf(Settings.themeMode(ctx)) }
            AppTheme(themeMode) {
                App(
                    active = active.value,
                    privateDns = privateDns.value,
                    onToggle = { onToggle() },
                    onReload = { callback -> reloadLists(callback) },
                    onDnsChanged = { applyDns() },
                    themeMode = themeMode,
                    onThemeChange = { mode ->
                        themeMode = mode
                        Settings.saveThemeMode(ctx, mode)
                    }
                )
            }
        }
    }

    override fun onResume() {
        super.onResume()
        active.value = DnsVpnService.isRunning
        privateDns.value = privateDnsActive(this)
    }

    private fun requestNotifications() {
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            notificationLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
    }

    private fun scheduleUpdates() {
        val request = PeriodicWorkRequestBuilder<UpdateWorker>(24, TimeUnit.HOURS)
            .setConstraints(
                Constraints.Builder()
                    .setRequiredNetworkType(NetworkType.CONNECTED)
                    .build()
            )
            .build()
        WorkManager.getInstance(applicationContext)
            .enqueueUniquePeriodicWork("blocklist-update", ExistingPeriodicWorkPolicy.KEEP, request)
    }

    private fun onToggle() {
        if (DnsVpnService.isRunning) {
            stopVpn()
        } else {
            if (Settings.dnsMode(this) == Settings.MODE_CUSTOM) {
                val primary = Settings.dnsPrimary(this).trim()
                if (primary.isEmpty() || !InetAddresses.isNumericAddress(primary)) {
                    Toast.makeText(this, "Set a Primary DNS or switch to System default", Toast.LENGTH_LONG).show()
                    return
                }
            }
            val intent = VpnService.prepare(this)
            if (intent != null) vpnLauncher.launch(intent) else startVpn()
        }
    }

    private fun startVpn() {
        startService(Intent(this, DnsVpnService::class.java).setAction(DnsVpnService.ACTION_START))
        active.value = true
    }

    private fun stopVpn() {
        startService(Intent(this, DnsVpnService::class.java).setAction(DnsVpnService.ACTION_STOP))
        active.value = false
    }

    private fun reloadLists(onResult: (Int) -> Unit) {
        if (DnsVpnService.isRunning) {
            Blocklist.loadAsync(Settings.enabledUrls(applicationContext)) { count ->
                mainHandler.post { onResult(count) }
            }
        } else {
            onResult(-1)
        }
    }

    private fun applyDns() {
        val context = applicationContext
        Thread {
            DnsVpnService.useCustomDns = Settings.dnsMode(context) == Settings.MODE_CUSTOM
            DnsVpnService.useDoh = Settings.dohEnabled(context)
            DnsVpnService.dnsPrimary = parseOrNull(Settings.dnsPrimary(context))
            DnsVpnService.dnsSecondary = parseOrNull(Settings.dnsSecondary(context))
        }.start()
    }

    private fun parseOrNull(value: String): InetAddress? =
        try {
            val trimmed = value.trim()
            if (trimmed.isEmpty()) null else InetAddress.getByName(trimmed)
        } catch (_: Exception) {
            null
        }
}

@Composable
private fun AppTheme(themeMode: Int, content: @Composable () -> Unit) {
    val dark = when (themeMode) {
        Settings.THEME_LIGHT -> false
        Settings.THEME_DARK -> true
        else -> isSystemInDarkTheme()
    }
    val colors = if (dark) FylaxDarkColors else FylaxLightColors
    MaterialTheme(colorScheme = colors, typography = AppTypography, content = content)
}

@Composable
private fun App(
    active: Boolean,
    privateDns: Boolean,
    onToggle: () -> Unit,
    onReload: ((Int) -> Unit) -> Unit,
    onDnsChanged: () -> Unit,
    themeMode: Int,
    onThemeChange: (Int) -> Unit
) {
    var tab by remember { mutableStateOf(0) }
    Surface(
        modifier = Modifier.fillMaxSize(),
        color = MaterialTheme.colorScheme.background
    ) {
        Box(modifier = Modifier.fillMaxSize()) {
            Box(modifier = Modifier.fillMaxSize()) {
                when (tab) {
                    0 -> HomeScreen(active = active, privateDns = privateDns, onToggle = onToggle, onReload = onReload)
                    1 -> ActivityScreen()
                    2 -> ShieldScreen(onReload = onReload, onDnsChanged = onDnsChanged)
                    else -> SettingsScreen(themeMode = themeMode, onThemeChange = onThemeChange)
                }
            }
            NavPill(
                tab = tab,
                onSelect = { tab = it },
                modifier = Modifier
                    .align(Alignment.BottomCenter)
                    .navigationBarsPadding()
                    .padding(bottom = 16.dp)
            )
        }
    }
}

@Composable
private fun NavPill(tab: Int, onSelect: (Int) -> Unit, modifier: Modifier = Modifier) {
    Surface(
        modifier = modifier,
        shape = RoundedCornerShape(50),
        color = MaterialTheme.colorScheme.surfaceContainerHigh,
        shadowElevation = 10.dp,
        tonalElevation = 3.dp
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(6.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            NavPillItem(R.drawable.ic_home, tab == 0) { onSelect(0) }
            NavPillItem(R.drawable.ic_search_activity, tab == 1) { onSelect(1) }
            NavPillItem(R.drawable.ic_shield, tab == 2) { onSelect(2) }
            NavPillItem(R.drawable.ic_settings, tab == 3) { onSelect(3) }
        }
    }
}

@Composable
private fun NavPillItem(icon: Int, selected: Boolean, onClick: () -> Unit) {
    val background by animateColorAsState(
        targetValue = if (selected) MaterialTheme.colorScheme.secondaryContainer else Color.Transparent,
        animationSpec = tween(220),
        label = "navBackground"
    )
    val tint by animateColorAsState(
        targetValue = if (selected) MaterialTheme.colorScheme.onSecondaryContainer else MaterialTheme.colorScheme.onSurfaceVariant,
        animationSpec = tween(220),
        label = "navTint"
    )
    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(50))
            .background(background)
            .clickable(
                interactionSource = remember { MutableInteractionSource() },
                indication = null
            ) { onClick() }
            .padding(horizontal = 22.dp, vertical = 10.dp),
        contentAlignment = Alignment.Center
    ) {
        Icon(painterResource(icon), null, tint = tint, modifier = Modifier.size(24.dp))
    }
}

@Composable
private fun ActivityScreen() {
    val context = LocalContext.current
    var entries by remember { mutableStateOf(QueryLog.snapshot()) }
    var counts by remember { mutableStateOf(QueryLog.counts()) }
    var search by remember { mutableStateOf("") }
    var onlyBlocked by remember { mutableStateOf(false) }
    val timeFmt = remember { java.text.SimpleDateFormat("HH:mm:ss", java.util.Locale.US) }

    val filtered = entries.filter {
        (!onlyBlocked || it.blocked) &&
            (search.isBlank() || it.host.contains(search.trim(), ignoreCase = true))
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .statusBarsPadding()
            .padding(16.dp)
    ) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text("Activity", fontSize = 20.sp, color = MaterialTheme.colorScheme.onSurface, modifier = Modifier.weight(1f))
            TextButton(onClick = {
                QueryLog.clear()
                entries = QueryLog.snapshot()
                counts = QueryLog.counts()
            }) {
                Text("Clear", fontSize = 13.sp)
            }
            IconButton(onClick = {
                entries = QueryLog.snapshot()
                counts = QueryLog.counts()
            }) {
                Icon(painterResource(R.drawable.ic_refresh), "Refresh", Modifier.size(22.dp))
            }
        }
        Spacer(Modifier.height(8.dp))
        Row(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.weight(1f)) {
                Text("${counts.first}", fontSize = 26.sp, color = MaterialTheme.colorScheme.onSurface)
                Text("Requests", fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Column(modifier = Modifier.weight(1f)) {
                Text("${counts.second}", fontSize = 26.sp, color = MaterialTheme.colorScheme.error)
                Text("Blocked", fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        Spacer(Modifier.height(12.dp))
        OutlinedTextField(
            value = search,
            onValueChange = { search = it },
            label = { Text("Search") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth()
        )
        Spacer(Modifier.height(8.dp))
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text("Blocked only", fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurface, modifier = Modifier.weight(1f))
            Switch(checked = onlyBlocked, onCheckedChange = { onlyBlocked = it })
        }
        Spacer(Modifier.height(8.dp))
        if (filtered.isEmpty()) {
            Text(
                "No activity yet",
                fontSize = 13.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(8.dp)
            )
        } else {
            LazyColumn(modifier = Modifier.fillMaxSize()) {
                items(filtered) { entry ->
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(vertical = 6.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column(modifier = Modifier.weight(1f)) {
                            Text(
                                text = entry.host,
                                fontSize = 14.sp,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                                color = if (entry.blocked) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurface
                            )
                            Text(
                                text = "${entry.type}  \u00B7  ${timeFmt.format(java.util.Date(entry.time))}",
                                fontSize = 12.sp,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                        if (entry.blocked) {
                            Button(onClick = {
                                val current = Settings.allowed(context).toMutableList()
                                if (current.none { it == entry.host }) {
                                    current.add(entry.host)
                                    Settings.saveAllowed(context, current)
                                    Blocklist.setAllowed(current)
                                    Toast.makeText(context, "Allowed \u00B7 ${entry.host}", Toast.LENGTH_SHORT).show()
                                } else {
                                    Toast.makeText(context, "Already allowed", Toast.LENGTH_SHORT).show()
                                }
                            }) {
                                Text("Allow", fontSize = 13.sp)
                            }
                        } else {
                            Button(onClick = {
                                val current = Settings.blockedManual(context).toMutableList()
                                if (current.none { it == entry.host }) {
                                    current.add(entry.host)
                                    Settings.saveBlockedManual(context, current)
                                    Blocklist.setManualBlocked(current)
                                    Toast.makeText(context, "Blocked \u00B7 ${entry.host}", Toast.LENGTH_SHORT).show()
                                } else {
                                    Toast.makeText(context, "Already blocked", Toast.LENGTH_SHORT).show()
                                }
                            }) {
                                Text("Block", fontSize = 13.sp)
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun HomeScreen(active: Boolean, privateDns: Boolean, onToggle: () -> Unit, onReload: ((Int) -> Unit) -> Unit) {
    val context = LocalContext.current
    var updating by remember { mutableStateOf(false) }
    Box(modifier = Modifier.fillMaxSize()) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .statusBarsPadding()
                .padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center
        ) {
            Text(
                text = if (active) "Protection is on" else "Protection is off",
                fontSize = 28.sp,
                color = MaterialTheme.colorScheme.onSurface
            )
            Spacer(Modifier.height(8.dp))
            Text(
                text = if (active) "DNS filtering is active" else "Tap to start filtering",
                fontSize = 15.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Spacer(Modifier.height(24.dp))
            AppToggle(checked = active, onToggle = onToggle)
            if (privateDns) {
                Spacer(Modifier.height(28.dp))
                Surface(
                    shape = RoundedCornerShape(12.dp),
                    color = MaterialTheme.colorScheme.errorContainer
                ) {
                    Text(
                        text = "Private DNS is on. Turn it off in Android settings — otherwise apps bypass Fylax and nothing is filtered.",
                        modifier = Modifier.padding(14.dp),
                        fontSize = 13.sp,
                        color = MaterialTheme.colorScheme.onErrorContainer
                    )
                }
            }
        }
        IconButton(
            onClick = {
                updating = true
                onReload { count ->
                    updating = false
                    Toast.makeText(
                        context,
                        if (count < 0) "Start protection first" else "Updated \u00B7 $count rules",
                        Toast.LENGTH_SHORT
                    ).show()
                }
            },
            modifier = Modifier
                .align(Alignment.TopEnd)
                .statusBarsPadding()
                .padding(8.dp)
        ) {
            Icon(
                painterResource(R.drawable.ic_cached),
                contentDescription = "Update filters",
                modifier = Modifier.size(24.dp),
                tint = MaterialTheme.colorScheme.onSurface
            )
        }
    }
    if (updating) {
        AlertDialog(
            onDismissRequest = {},
            confirmButton = {},
            text = {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    CircularProgressIndicator(modifier = Modifier.size(22.dp), strokeWidth = 2.dp)
                    Spacer(Modifier.width(16.dp))
                    Text("Updating DNS filters\u2026")
                }
            }
        )
    }
}

private fun privateDnsActive(context: android.content.Context): Boolean =
    try {
        android.provider.Settings.Global.getString(
            context.contentResolver,
            "private_dns_mode"
        ) == "hostname"
    } catch (_: Exception) {
        false
    }

@Composable
private fun AppToggle(checked: Boolean, onToggle: () -> Unit) {
    val trackWidth = 118.dp
    val trackHeight = 56.dp
    val thumbSize = 46.dp
    val pad = 5.dp
    val trackColor by animateColorAsState(
        targetValue = if (checked) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surfaceVariant,
        animationSpec = tween(220),
        label = "track"
    )
    val offset by animateDpAsState(
        targetValue = if (checked) trackWidth - thumbSize - pad else pad,
        animationSpec = tween(220),
        label = "thumb"
    )
    Box(
        modifier = Modifier
            .size(trackWidth, trackHeight)
            .clip(RoundedCornerShape(trackHeight / 2))
            .background(trackColor)
            .clickable(
                interactionSource = remember { MutableInteractionSource() },
                indication = null
            ) { onToggle() }
    ) {
        Box(
            modifier = Modifier
                .padding(start = offset, top = pad, bottom = pad)
                .size(thumbSize)
                .clip(CircleShape)
                .background(MaterialTheme.colorScheme.background)
        )
    }
}

@Composable
private fun ShieldScreen(onReload: ((Int) -> Unit) -> Unit, onDnsChanged: () -> Unit) {
    val context = LocalContext.current
    var lists by remember { mutableStateOf(Settings.lists(context)) }
    var newUrl by remember { mutableStateOf("") }
    var dnsMode by remember { mutableStateOf(Settings.dnsMode(context)) }
    var primary by remember { mutableStateOf(Settings.dnsPrimary(context)) }
    var secondary by remember { mutableStateOf(Settings.dnsSecondary(context)) }
    var doh by remember { mutableStateOf(Settings.dohEnabled(context)) }
    var allowed by remember { mutableStateOf(Settings.allowed(context)) }
    var newAllowed by remember { mutableStateOf("") }
    var blockedManual by remember { mutableStateOf(Settings.blockedManual(context)) }
    var newBlocked by remember { mutableStateOf("") }
    var loading by remember { mutableStateOf(false) }
    var pendingDelete by remember { mutableStateOf<Int?>(null) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .statusBarsPadding()
            .padding(16.dp)
    ) {
        pendingDelete?.let { idx ->
            AlertDialog(
                onDismissRequest = { pendingDelete = null },
                title = { Text("Delete blocklist?") },
                text = { Text(lists.getOrNull(idx)?.url ?: "", maxLines = 3, overflow = TextOverflow.Ellipsis) },
                confirmButton = {
                    TextButton(onClick = {
                        lists = lists.toMutableList().also { if (idx < it.size) it.removeAt(idx) }
                        pendingDelete = null
                    }) { Text("Delete") }
                },
                dismissButton = {
                    TextButton(onClick = { pendingDelete = null }) { Text("Cancel") }
                }
            )
        }
        Text("Blocklists", fontSize = 20.sp, color = MaterialTheme.colorScheme.onSurface)
        Spacer(Modifier.height(12.dp))
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(8.dp)) {
                lists.forEachIndexed { index, item ->
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(vertical = 4.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            text = item.url,
                            fontSize = 13.sp,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            color = MaterialTheme.colorScheme.onSurface,
                            modifier = Modifier.weight(1f)
                        )
                        Switch(
                            checked = item.enabled,
                            onCheckedChange = { value ->
                                lists = lists.toMutableList().also { it[index] = item.copy(enabled = value) }
                            }
                        )
                        IconButton(onClick = {
                            pendingDelete = index
                        }) {
                            Icon(painterResource(R.drawable.ic_delete), "Delete", Modifier.size(22.dp))
                        }
                    }
                }
                if (lists.isEmpty()) {
                    Text(
                        "No lists",
                        fontSize = 13.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(8.dp)
                    )
                }
            }
        }
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            OutlinedTextField(
                value = newUrl,
                onValueChange = { newUrl = it },
                label = { Text("Add blocklist URL") },
                singleLine = true,
                modifier = Modifier.weight(1f)
            )
            IconButton(onClick = {
                val url = newUrl.trim()
                if (url.isNotEmpty() && lists.none { it.url == url }) {
                    lists = lists + Settings.BlockList(url, true)
                    newUrl = ""
                }
            }) {
                Icon(painterResource(R.drawable.ic_add), "Add", Modifier.size(26.dp))
            }
        }
        Spacer(Modifier.height(12.dp))
        Button(
            onClick = {
                val pending = newUrl.trim()
                val merged = if (pending.isNotEmpty() && lists.none { it.url == pending }) {
                    lists + Settings.BlockList(pending, true)
                } else {
                    lists
                }
                lists = merged
                newUrl = ""
                Settings.saveLists(context, merged)
                loading = true
                onReload { count ->
                    loading = false
                    val message = if (count < 0) {
                        "Saved \u00B7 start protection to apply"
                    } else {
                        "Applied \u00B7 $count rules"
                    }
                    Toast.makeText(context, message, Toast.LENGTH_SHORT).show()
                }
            },
            enabled = !loading,
            modifier = Modifier.fillMaxWidth()
        ) {
            if (loading) {
                CircularProgressIndicator(
                    modifier = Modifier.size(20.dp),
                    strokeWidth = 2.dp,
                    color = MaterialTheme.colorScheme.onPrimary
                )
            } else {
                Text("Apply")
            }
        }

        Spacer(Modifier.height(28.dp))

        Text("Allowed", fontSize = 20.sp, color = MaterialTheme.colorScheme.onSurface)
        Spacer(Modifier.height(4.dp))
        Text(
            "Domains here are never blocked, even if a list blocks them.",
            fontSize = 13.sp,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
        Spacer(Modifier.height(12.dp))
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(8.dp)) {
                allowed.forEachIndexed { index, domain ->
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(vertical = 4.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            text = domain,
                            fontSize = 13.sp,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            color = MaterialTheme.colorScheme.onSurface,
                            modifier = Modifier.weight(1f)
                        )
                        IconButton(onClick = {
                            allowed = allowed.toMutableList().also { it.removeAt(index) }
                            Settings.saveAllowed(context, allowed)
                            Blocklist.setAllowed(allowed)
                        }) {
                            Icon(painterResource(R.drawable.ic_delete), "Delete", Modifier.size(22.dp))
                        }
                    }
                }
                if (allowed.isEmpty()) {
                    Text(
                        "No exceptions",
                        fontSize = 13.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(8.dp)
                    )
                }
            }
        }
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            OutlinedTextField(
                value = newAllowed,
                onValueChange = { newAllowed = it },
                label = { Text("Add allowed domain") },
                singleLine = true,
                modifier = Modifier.weight(1f)
            )
            IconButton(onClick = {
                val domain = newAllowed.trim().lowercase()
                if (domain.isNotEmpty() && allowed.none { it == domain }) {
                    allowed = allowed + domain
                    Settings.saveAllowed(context, allowed)
                    Blocklist.setAllowed(allowed)
                    newAllowed = ""
                }
            }) {
                Icon(painterResource(R.drawable.ic_add), "Add", Modifier.size(26.dp))
            }
        }

        Spacer(Modifier.height(28.dp))

        Text("Blocked", fontSize = 20.sp, color = MaterialTheme.colorScheme.onSurface)
        Spacer(Modifier.height(4.dp))
        Text(
            "Domains here are always blocked, on top of your lists.",
            fontSize = 13.sp,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
        Spacer(Modifier.height(12.dp))
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(8.dp)) {
                blockedManual.forEachIndexed { index, domain ->
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(vertical = 4.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            text = domain,
                            fontSize = 13.sp,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            color = MaterialTheme.colorScheme.onSurface,
                            modifier = Modifier.weight(1f)
                        )
                        IconButton(onClick = {
                            blockedManual = blockedManual.toMutableList().also { it.removeAt(index) }
                            Settings.saveBlockedManual(context, blockedManual)
                            Blocklist.setManualBlocked(blockedManual)
                        }) {
                            Icon(painterResource(R.drawable.ic_delete), "Delete", Modifier.size(22.dp))
                        }
                    }
                }
                if (blockedManual.isEmpty()) {
                    Text(
                        "Nothing added",
                        fontSize = 13.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(8.dp)
                    )
                }
            }
        }
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            OutlinedTextField(
                value = newBlocked,
                onValueChange = { newBlocked = it },
                label = { Text("Add blocked domain") },
                singleLine = true,
                modifier = Modifier.weight(1f)
            )
            IconButton(onClick = {
                val domain = newBlocked.trim().lowercase()
                if (domain.isNotEmpty() && blockedManual.none { it == domain }) {
                    blockedManual = blockedManual + domain
                    Settings.saveBlockedManual(context, blockedManual)
                    Blocklist.setManualBlocked(blockedManual)
                    newBlocked = ""
                }
            }) {
                Icon(painterResource(R.drawable.ic_add), "Add", Modifier.size(26.dp))
            }
        }

        Spacer(Modifier.height(28.dp))

        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(
                painter = painterResource(R.drawable.ic_dns),
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onSurface,
                modifier = Modifier.size(22.dp)
            )
            Spacer(Modifier.width(8.dp))
            Text("DNS", fontSize = 20.sp, color = MaterialTheme.colorScheme.onSurface)
        }
        Spacer(Modifier.height(8.dp))
        DnsOption(
            selected = dnsMode == Settings.MODE_SYSTEM,
            title = "System default",
            subtitle = "Provided by your network / ISP",
            onClick = { dnsMode = Settings.MODE_SYSTEM }
        )
        DnsOption(
            selected = dnsMode == Settings.MODE_CUSTOM,
            title = "Custom",
            subtitle = "Choose your own resolver",
            onClick = { dnsMode = Settings.MODE_CUSTOM }
        )
        if (dnsMode == Settings.MODE_CUSTOM) {
            Spacer(Modifier.height(8.dp))
            OutlinedTextField(
                value = primary,
                onValueChange = { primary = it },
                label = { Text("Primary") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(8.dp))
            OutlinedTextField(
                value = secondary,
                onValueChange = { secondary = it },
                label = { Text("Secondary") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(8.dp))
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    "DNS over HTTPS (DoH)",
                    fontSize = 15.sp,
                    color = MaterialTheme.colorScheme.onSurface,
                    modifier = Modifier.weight(1f)
                )
                Switch(checked = doh, onCheckedChange = { doh = it })
            }
        }
        Spacer(Modifier.height(12.dp))
        Button(
            onClick = {
                Settings.saveDns(context, dnsMode, primary.trim(), secondary.trim(), doh)
                onDnsChanged()
                Toast.makeText(context, "DNS saved", Toast.LENGTH_SHORT).show()
            },
            modifier = Modifier.fillMaxWidth()
        ) {
            Text("Save DNS")
        }
        Spacer(Modifier.height(120.dp))
    }
}

@Composable
private fun DnsOption(selected: Boolean, title: String, subtitle: String, onClick: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .clickable(
                interactionSource = remember { MutableInteractionSource() },
                indication = null
            ) { onClick() }
            .padding(vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        RadioButton(selected = selected, onClick = onClick)
        Spacer(Modifier.width(8.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(title, fontSize = 15.sp, color = MaterialTheme.colorScheme.onSurface)
            Text(subtitle, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun SettingsScreen(themeMode: Int, onThemeChange: (Int) -> Unit) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .statusBarsPadding()
            .padding(16.dp)
    ) {
        Text("Theme", fontSize = 20.sp, color = MaterialTheme.colorScheme.onSurface)
        Spacer(Modifier.height(8.dp))
        ThemeOption("System", themeMode == Settings.THEME_SYSTEM) { onThemeChange(Settings.THEME_SYSTEM) }
        ThemeOption("Light", themeMode == Settings.THEME_LIGHT) { onThemeChange(Settings.THEME_LIGHT) }
        ThemeOption("Dark", themeMode == Settings.THEME_DARK) { onThemeChange(Settings.THEME_DARK) }

        Spacer(Modifier.weight(1f))

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(bottom = 120.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Icon(
                painter = painterResource(R.drawable.ic_shield),
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(48.dp)
            )
            Spacer(Modifier.height(12.dp))
            Text("Fylax", fontSize = 20.sp, color = MaterialTheme.colorScheme.onSurface)
            Spacer(Modifier.height(4.dp))
            Text(
                "Version ${BuildConfig.VERSION_NAME}",
                fontSize = 14.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}

@Composable
private fun ThemeOption(label: String, selected: Boolean, onClick: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .clickable(
                interactionSource = remember { MutableInteractionSource() },
                indication = null
            ) { onClick() }
            .padding(vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        RadioButton(selected = selected, onClick = onClick)
        Spacer(Modifier.width(8.dp))
        Text(label, fontSize = 15.sp, color = MaterialTheme.colorScheme.onSurface)
    }
}
