package com.fylax.android

import android.content.Context
import androidx.work.Worker
import androidx.work.WorkerParameters

class UpdateWorker(context: Context, params: WorkerParameters) : Worker(context, params) {
    override fun doWork(): Result {
        if (!DnsVpnService.isRunning) return Result.success()
        val urls = Settings.enabledUrls(applicationContext)
        if (urls.isNotEmpty()) Blocklist.load(urls)
        return Result.success()
    }
}
