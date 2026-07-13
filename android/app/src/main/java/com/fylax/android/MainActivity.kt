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
import kotlinx.coroutines.launch
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
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.work.Constraints
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
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
    error = Color(0xFFD93025),
    errorContainer = Color(0xFF292929),
    onErrorContainer = Color(0xFFFAFAFA),
    outline = Color(0xFF919191),
    outlineVariant = Color(0xFF292929),
    surfaceTint = Color(0xFFE2E2E2),
    inversePrimary = Color(0xFF5E5E5E),
    inverseSurface = Color(0xFFE2E2E2),
    inverseOnSurface = Color(0xFF292929)
)

private val FylaxLightColors = lightColorScheme(
    background = Color(0xFFFAFAFA),
    surface = Color(0xFFF0F0F0),
    surfaceVariant = Color(0xFFD4D4D4),
    surfaceContainer = Color(0xFFF0F0F0),
    surfaceContainerLow = Color(0xFFFAFAFA),
    surfaceContainerHigh = Color(0xFFE4E4E4),
    surfaceContainerHighest = Color(0xFFD4D4D4),
    onSurface = Color(0xFF121212),
    onSurfaceVariant = Color(0xFF5E5E5E),
    primary = Color(0xFF1B1B1B),
    onPrimary = Color(0xFFFAFAFA),
    primaryContainer = Color(0xFFE4E4E4),
    onPrimaryContainer = Color(0xFF1B1B1B),
    secondaryContainer = Color(0xFFE4E4E4),
    onSecondaryContainer = Color(0xFF1B1B1B),
    tertiary = Color(0xFF1B1B1B),
    error = Color(0xFFD93025),
    errorContainer = Color(0xFFE4E4E4),
    onErrorContainer = Color(0xFF1B1B1B),
    outline = Color(0xFF767676),
    outlineVariant = Color(0xFFE4E4E4),
    surfaceTint = Color(0xFF1B1B1B),
    inversePrimary = Color(0xFFC6C6C6),
    inverseSurface = Color(0xFF292929),
    inverseOnSurface = Color(0xFFF1F1F1)
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
                    onRestart = { restartVpn() },
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

    private fun restartVpn() {
        if (!DnsVpnService.isRunning) return
        stopVpn()
        mainHandler.postDelayed({
            if (VpnService.prepare(this) == null) startVpn()
        }, 700)
    }

    private fun reloadLists(onResult: (Int) -> Unit) {
        if (DnsVpnService.isRunning) {
            Blocklist.refreshAsync(applicationContext, Settings.enabledUrls(applicationContext)) { count ->
                mainHandler.post { onResult(count) }
            }
        } else {
            onResult(-1)
        }
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

private val LocalShowBanner = staticCompositionLocalOf<(String) -> Unit> { {} }

@Composable
private fun App(
    active: Boolean,
    privateDns: Boolean,
    onToggle: () -> Unit,
    onReload: ((Int) -> Unit) -> Unit,
    onRestart: () -> Unit,
    themeMode: Int,
    onThemeChange: (Int) -> Unit
) {
    var tab by remember { mutableStateOf(0) }
    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    Scaffold(
        containerColor = MaterialTheme.colorScheme.background,
        contentWindowInsets = WindowInsets(0, 0, 0, 0),
        snackbarHost = {
            SnackbarHost(snackbarHostState) { data ->
                Surface(
                    modifier = Modifier.padding(12.dp),
                    shape = RoundedCornerShape(4.dp),
                    color = MaterialTheme.colorScheme.surfaceContainerHighest,
                    contentColor = MaterialTheme.colorScheme.onSurface,
                    shadowElevation = 6.dp
                ) {
                    Text(
                        text = data.visuals.message,
                        modifier = Modifier.padding(horizontal = 16.dp, vertical = 12.dp),
                        style = MaterialTheme.typography.bodyMedium,
                        textAlign = TextAlign.Center
                    )
                }
            }
        },
        bottomBar = {
            NavigationBar(
                modifier = Modifier
                    .navigationBarsPadding()
                    .height(56.dp),
                windowInsets = WindowInsets(0, 0, 0, 0),
                containerColor = MaterialTheme.colorScheme.surfaceContainer
            ) {
                NavigationBarItem(
                    selected = tab == 0,
                    onClick = { tab = 0 },
                    icon = { Icon(painterResource(R.drawable.ic_home), contentDescription = null) }
                )
                NavigationBarItem(
                    selected = tab == 1,
                    onClick = { tab = 1 },
                    icon = { Icon(painterResource(R.drawable.ic_search_activity), contentDescription = null) }
                )
                NavigationBarItem(
                    selected = tab == 2,
                    onClick = { tab = 2 },
                    icon = { Icon(painterResource(R.drawable.ic_shield), contentDescription = null) }
                )
                NavigationBarItem(
                    selected = tab == 3,
                    onClick = { tab = 3 },
                    icon = { Icon(painterResource(R.drawable.ic_settings), contentDescription = null) }
                )
            }
        }
    ) { innerPadding ->
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
        ) {
            CompositionLocalProvider(LocalShowBanner provides { message ->
                scope.launch {
                    snackbarHostState.currentSnackbarData?.dismiss()
                    snackbarHostState.showSnackbar(message)
                }
            }) {
                when (tab) {
                    0 -> HomeScreen(active = active, privateDns = privateDns, onToggle = onToggle, onReload = onReload, onRestart = onRestart)
                    1 -> ActivityScreen(onRestart = onRestart)
                    2 -> ShieldScreen(onRestart = onRestart)
                    else -> SettingsScreen(themeMode = themeMode, onThemeChange = onThemeChange)
                }
            }
        }
    }
}

@Composable
private fun ActivityScreen(onRestart: () -> Unit) {
    val context = LocalContext.current
    val showBanner = LocalShowBanner.current
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
        Text(
            "Activity",
            style = MaterialTheme.typography.titleLarge,
            color = MaterialTheme.colorScheme.onSurface,
            modifier = Modifier.fillMaxWidth(),
            textAlign = TextAlign.Center
        )
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.End,
            verticalAlignment = Alignment.CenterVertically
        ) {
            TextButton(onClick = {
                QueryLog.clear()
                entries = QueryLog.snapshot()
                counts = QueryLog.counts()
            }) {
                Text("Clear", style = MaterialTheme.typography.bodyMedium)
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
                Text("${counts.first}", style = MaterialTheme.typography.headlineSmall, color = MaterialTheme.colorScheme.onSurface)
                Text("Requests", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Column(modifier = Modifier.weight(1f)) {
                Text("${counts.second}", style = MaterialTheme.typography.headlineSmall, color = MaterialTheme.colorScheme.error)
                Text("Blocked", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
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
            Text("Blocked only", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface, modifier = Modifier.weight(1f))
            Switch(checked = onlyBlocked, onCheckedChange = { onlyBlocked = it })
        }
        Spacer(Modifier.height(8.dp))
        if (filtered.isEmpty()) {
            Text(
                "No activity yet",
                style = MaterialTheme.typography.bodyMedium,
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
                                style = MaterialTheme.typography.bodyMedium,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                                color = if (entry.blocked) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurface
                            )
                            Text(
                                text = "${entry.type}  \u00B7  ${timeFmt.format(java.util.Date(entry.time))}",
                                style = MaterialTheme.typography.bodySmall,
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
                                    showBanner("Allowed ${entry.host}")
                                    onRestart()
                                } else {
                                    showBanner("Already allowed")
                                }
                            }) {
                                Text("Allow", style = MaterialTheme.typography.bodyMedium)
                            }
                        } else {
                            Button(onClick = {
                                val current = Settings.blockedManual(context).toMutableList()
                                if (current.none { it == entry.host }) {
                                    current.add(entry.host)
                                    Settings.saveBlockedManual(context, current)
                                    Blocklist.setManualBlocked(current)
                                    showBanner("Blocked ${entry.host}")
                                    onRestart()
                                } else {
                                    showBanner("Already blocked")
                                }
                            }) {
                                Text("Block", style = MaterialTheme.typography.bodyMedium)
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun HomeScreen(active: Boolean, privateDns: Boolean, onToggle: () -> Unit, onReload: ((Int) -> Unit) -> Unit, onRestart: () -> Unit) {
    val context = LocalContext.current
    val showBanner = LocalShowBanner.current
    val filtersActive = active && (Settings.lists(context).any { it.enabled } || Settings.blockedManual(context).isNotEmpty())
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
                style = MaterialTheme.typography.headlineMedium,
                color = MaterialTheme.colorScheme.onSurface
            )
            Spacer(Modifier.height(8.dp))
            Text(
                text = if (filtersActive) "DNS filtering is enabled" else "DNS filtering is disabled",
                style = MaterialTheme.typography.bodyLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Spacer(Modifier.height(24.dp))
            Box(
                modifier = Modifier.size(width = 115.dp, height = 71.dp),
                contentAlignment = Alignment.Center
            ) {
                Switch(
                    checked = active,
                    onCheckedChange = { onToggle() },
                    modifier = Modifier.scale(2.2f)
                )
            }
            if (privateDns) {
                Spacer(Modifier.height(28.dp))
                Surface(
                    shape = RoundedCornerShape(12.dp),
                    color = MaterialTheme.colorScheme.errorContainer
                ) {
                    Text(
                        text = "Private DNS is on. Turn it off in Android settings — otherwise apps bypass Fylax and nothing is filtered.",
                        modifier = Modifier.padding(14.dp),
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onErrorContainer
                    )
                }
            }
        }
        Text(
            "Fylax",
            style = MaterialTheme.typography.titleLarge,
            color = MaterialTheme.colorScheme.onSurface,
            modifier = Modifier
                .align(Alignment.TopCenter)
                .statusBarsPadding()
                .padding(16.dp)
        )
        IconButton(
            onClick = {
                onReload { count ->
                    showBanner(if (count < 0) "Start protection first" else "Updated $count rules")
                    onRestart()
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
private fun ShieldScreen(onRestart: () -> Unit) {
    val context = LocalContext.current
    val showBanner = LocalShowBanner.current
    val uriHandler = LocalUriHandler.current
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
    var expanded by remember { mutableStateOf<Int?>(null) }
    var pendingUrl by remember { mutableStateOf<String?>(null) }
    var pendingName by remember { mutableStateOf("") }

    fun commitLists(updated: List<Settings.BlockList>) {
        lists = updated
        Settings.saveLists(context, updated)
        onRestart()
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .statusBarsPadding()
            .padding(16.dp)
    ) {
        pendingUrl?.let { url ->
            AlertDialog(
                onDismissRequest = { pendingUrl = null },
                title = { Text("Name this list") },
                text = {
                    OutlinedTextField(
                        value = pendingName,
                        onValueChange = { pendingName = it },
                        label = { Text("Name") },
                        singleLine = true
                    )
                },
                confirmButton = {
                    TextButton(onClick = {
                        val name = pendingName.trim()
                        if (name.isNotEmpty()) {
                            commitLists(lists + Settings.BlockList(url, true, name))
                            newUrl = ""
                            pendingName = ""
                            pendingUrl = null
                        }
                    }) { Text("Add") }
                },
                dismissButton = {
                    TextButton(onClick = { pendingUrl = null }) { Text("Cancel") }
                }
            )
        }

        Text(
            "Protection",
            style = MaterialTheme.typography.titleLarge,
            color = MaterialTheme.colorScheme.onSurface,
            modifier = Modifier.fillMaxWidth(),
            textAlign = TextAlign.Center
        )

        Spacer(Modifier.height(24.dp))

        Text("DNS Filters", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface)
        Spacer(Modifier.height(12.dp))
        if (lists.isNotEmpty()) {
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
                                text = item.name,
                                style = MaterialTheme.typography.bodyMedium,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                                color = MaterialTheme.colorScheme.onSurface,
                                modifier = Modifier
                                    .weight(1f)
                                    .clip(RoundedCornerShape(8.dp))
                                    .clickable(
                                        interactionSource = remember { MutableInteractionSource() },
                                        indication = null
                                    ) { expanded = if (expanded == index) null else index }
                                    .padding(vertical = 10.dp)
                            )
                            Switch(
                                checked = item.enabled,
                                onCheckedChange = { value ->
                                    commitLists(lists.toMutableList().also { it[index] = item.copy(enabled = value) })
                                }
                            )
                            IconButton(onClick = {
                                expanded = null
                                commitLists(lists.toMutableList().also { if (index < it.size) it.removeAt(index) })
                            }) {
                                Icon(painterResource(R.drawable.ic_delete), "Delete", Modifier.size(22.dp))
                            }
                        }
                        if (expanded == index) {
                            Surface(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(bottom = 8.dp),
                                shape = RoundedCornerShape(8.dp),
                                color = MaterialTheme.colorScheme.surfaceContainerHigh
                            ) {
                                Text(
                                    text = item.url,
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    modifier = Modifier
                                        .fillMaxWidth()
                                        .clickable(
                                            interactionSource = remember { MutableInteractionSource() },
                                            indication = null
                                        ) { uriHandler.openUri(item.url) }
                                        .padding(12.dp)
                                )
                            }
                        }
                    }
                }
            }
            Spacer(Modifier.height(12.dp))
        }
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
                    val known = Settings.knownName(url)
                    if (known != null) {
                        commitLists(lists + Settings.BlockList(url, true, known))
                        newUrl = ""
                    } else {
                        pendingName = ""
                        pendingUrl = url
                    }
                }
            }) {
                Icon(painterResource(R.drawable.ic_add), "Add", Modifier.size(26.dp))
            }
        }

        Spacer(Modifier.height(28.dp))

        Text("Allowed", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface)
        Spacer(Modifier.height(12.dp))
        if (allowed.isNotEmpty()) {
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
                                style = MaterialTheme.typography.bodyMedium,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                                color = MaterialTheme.colorScheme.onSurface,
                                modifier = Modifier.weight(1f)
                            )
                            IconButton(onClick = {
                                val updated = allowed.toMutableList().also { it.removeAt(index) }
                                allowed = updated
                                Settings.saveAllowed(context, updated)
                                Blocklist.setAllowed(updated)
                                onRestart()
                            }) {
                                Icon(painterResource(R.drawable.ic_delete), "Delete", Modifier.size(22.dp))
                            }
                        }
                    }
                }
            }
            Spacer(Modifier.height(12.dp))
        }
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
                    val updated = allowed + domain
                    allowed = updated
                    Settings.saveAllowed(context, updated)
                    Blocklist.setAllowed(updated)
                    newAllowed = ""
                    onRestart()
                }
            }) {
                Icon(painterResource(R.drawable.ic_add), "Add", Modifier.size(26.dp))
            }
        }

        Spacer(Modifier.height(28.dp))

        Text("Blocked", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface)
        Spacer(Modifier.height(12.dp))
        if (blockedManual.isNotEmpty()) {
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
                                style = MaterialTheme.typography.bodyMedium,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                                color = MaterialTheme.colorScheme.onSurface,
                                modifier = Modifier.weight(1f)
                            )
                            IconButton(onClick = {
                                val updated = blockedManual.toMutableList().also { it.removeAt(index) }
                                blockedManual = updated
                                Settings.saveBlockedManual(context, updated)
                                Blocklist.setManualBlocked(updated)
                                onRestart()
                            }) {
                                Icon(painterResource(R.drawable.ic_delete), "Delete", Modifier.size(22.dp))
                            }
                        }
                    }
                }
            }
            Spacer(Modifier.height(12.dp))
        }
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
                    val updated = blockedManual + domain
                    blockedManual = updated
                    Settings.saveBlockedManual(context, updated)
                    Blocklist.setManualBlocked(updated)
                    newBlocked = ""
                    onRestart()
                }
            }) {
                Icon(painterResource(R.drawable.ic_add), "Add", Modifier.size(26.dp))
            }
        }

        Spacer(Modifier.height(28.dp))

        Text("DNS Server", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface)
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
                    style = MaterialTheme.typography.bodyLarge,
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
                onRestart()
                showBanner("Applied")
            },
            modifier = Modifier.fillMaxWidth()
        ) {
            Text("Apply")
        }
        Spacer(Modifier.height(24.dp))
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
            Text(title, style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurface)
            Text(subtitle, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
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
        Text(
            "Settings",
            style = MaterialTheme.typography.titleLarge,
            color = MaterialTheme.colorScheme.onSurface,
            modifier = Modifier.fillMaxWidth(),
            textAlign = TextAlign.Center
        )
        Spacer(Modifier.height(24.dp))
        Text("Theme", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface)
        Spacer(Modifier.height(8.dp))
        ThemeOption("System", themeMode == Settings.THEME_SYSTEM) { onThemeChange(Settings.THEME_SYSTEM) }
        ThemeOption("Light", themeMode == Settings.THEME_LIGHT) { onThemeChange(Settings.THEME_LIGHT) }
        ThemeOption("Dark", themeMode == Settings.THEME_DARK) { onThemeChange(Settings.THEME_DARK) }

        Spacer(Modifier.weight(1f))

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(bottom = 24.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Icon(
                painter = painterResource(R.drawable.ic_shield),
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(48.dp)
            )
            Spacer(Modifier.height(12.dp))
            Text("Fylax", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface)
            Spacer(Modifier.height(4.dp))
            Text(
                "Version ${BuildConfig.VERSION_NAME}",
                style = MaterialTheme.typography.bodyMedium,
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
        Text(label, style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurface)
    }
}
