package com.catclaw.music.kt.data.update

import android.content.Context
import android.content.pm.PackageManager
import com.catclaw.music.kt.core.update.UpdateRules
import dagger.hilt.android.qualifiers.ApplicationContext
import java.net.HttpURLConnection
import java.net.URL
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * GitHub Release 更新检查，语义照 C# `UpdateService`：
 * 拉 `releases/latest` → 取 `tag_name` → 与本机版本比前三段 → 有更新才返回版本号。
 *
 * 任何网络/解析失败一律回 null（= "没有新版本"），与 C# 的 `catch { return null; }` 一致：
 * 检查更新是顺手做的事，不该在关于页弹一个红色报错。
 */
/** 检查更新的结果。Unreachable 专指"没问到"，与"问到了但没有更新"必须能区分。 */
sealed interface UpdateOutcome {
    data class Available(val latest: String) : UpdateOutcome
    data object UpToDate : UpdateOutcome
    data object Unreachable : UpdateOutcome
}

@Singleton
class UpdateService @Inject constructor(
    @ApplicationContext private val context: Context,
) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /**
     * 三种结果分开回：C# 那边失败与"已最新"都 return null，界面因此会把断网说成"已是最新版本"。
     * 这里不照抄那个含糊 —— 用户看到"已是最新"就会停止找更新。
     */
    suspend fun check(): UpdateOutcome = withContext(Dispatchers.IO) {
        val tag = runCatching { fetchLatestTag() }.getOrNull()
            ?: return@withContext UpdateOutcome.Unreachable
        if (UpdateRules.compareVersion(tag, currentVersion()) > 0) UpdateOutcome.Available(tag) else UpdateOutcome.UpToDate
    }

    private fun fetchLatestTag(): String? {
        val connection = (URL(API).openConnection() as HttpURLConnection).apply {
            connectTimeout = TIMEOUT_MS
            readTimeout = TIMEOUT_MS
            setRequestProperty("User-Agent", USER_AGENT)
            setRequestProperty("Accept", "application/vnd.github+json")
        }
        return try {
            if (connection.responseCode != HttpURLConnection.HTTP_OK) return null
            UpdateRules.latestVersionFrom(connection.inputStream.bufferedReader().use { it.readText() })
        } finally {
            connection.disconnect()
        }
    }

    /** 本机版本：取 PackageManager 的 versionName（与 C# 用 AppInfo.VersionString 同源） */
    fun currentVersion(): String = runCatching {
        val info = context.packageManager.getPackageInfo(context.packageName, 0)
        info.versionName ?: "0.0.0"
    }.getOrDefault("0.0.0")

    fun markVersionNotified(version: String) {
        prefs.edit().putString(KEY_IGNORED, version).putString(KEY_PENDING, "").apply()
    }

    fun ignoredVersion(): String = prefs.getString(KEY_IGNORED, "").orEmpty()

    fun setPendingVersion(version: String) {
        prefs.edit().putString(KEY_PENDING, version).apply()
    }

    fun pendingVersion(): String = prefs.getString(KEY_PENDING, "").orEmpty()

    private companion object {
        const val API = "https://api.github.com/repos/kankejiang/CatClawMusic/releases/latest"
        const val USER_AGENT = "CatClawMusic"
        const val TIMEOUT_MS = 10_000
        const val PREFS = "catclaw_state"
        const val KEY_IGNORED = "update_ignored_version"
        const val KEY_PENDING = "update_pending_version"
    }
}
