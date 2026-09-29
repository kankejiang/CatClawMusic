package com.catclaw.music.kt.data.settings

import android.content.Context
import android.content.Intent
import android.net.Uri
import com.catclaw.music.kt.core.library.MediaScanRules
import com.catclaw.music.kt.core.diagnostic.AppLog
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 本地扫描设置：两种扫描方式的开关 + SAF 音乐文件夹清单。
 *
 * 键名与 C# 侧对齐（`use_media_store` / `use_saf_scan` / `music_folder_uris`），
 * 因为这三样在 MAUI 里是**真有消费者**的（`LocalScanService` 的两个 stage 与 `FolderPicker`）。
 * C# 那边另外几个（`auto_scan`、`ScanSubfolders`）查证过没有任何读取方（自动扫描从没被触发过、
 * 递归是写死的），所以不 port —— 摆一个不工作的开关就是假控件。
 *
 * 与 C# 不同的一处：`use_media_store` 这里默认 **true**。
 * C# 两个开关都默认 false，等于"新装完不勾就没得扫"；本端 MediaStore 扫描已经在用
 * （真机那 48 首就是这么进来的），把默认改成 false 是倒退，不是对齐。
 */
@Singleton
class LocalScanSettings @Inject constructor(
    @ApplicationContext private val context: Context,
) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    var useMediaStore: Boolean
        get() = prefs.getBoolean(KEY_MEDIA_STORE, true)
        set(value) {
            prefs.edit().putBoolean(KEY_MEDIA_STORE, value).apply()
        }

    var useSafScan: Boolean
        get() = prefs.getBoolean(KEY_SAF_SCAN, false)
        set(value) {
            prefs.edit().putBoolean(KEY_SAF_SCAN, value).apply()
        }

    /** 已授权的 SAF 音乐文件夹（树 URI），顺序就是列表页显示顺序 */
    fun folders(): List<String> = MediaScanRules.decodeFolders(prefs.getString(KEY_FOLDERS, "").orEmpty())

    /**
     * 记住一个 SAF 文件夹，并把它**持久化授权**要到手。
     *
     * 授权必须在拿到结果的这一刻做：`FLAG_GRANT_PERSISTABLE` 只在返回的 Intent 上带着，
     * 之后从字符串重新 parse 出来的 URI 已经没有这个权利了 —— 只存字符串的结果是
     * "列表里看着有，扫描时读不到"。
     *
     * @return false 表示这个文件夹已经在清单里
     */
    fun addFolder(uri: Uri): Boolean {
        val value = uri.toString()
        val current = folders()
        if (value in current) return false
        runCatching {
            context.contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }.onFailure {
            // 拿不到持久授权也要存：可能是这个 URI 本来就可读（同签名的 DocumentsUI），
            // 也可能是用户撤销过授权 —— 扫描那侧读不到会自己报数，这里不替它下结论
            AppLog.w(TAG, "takePersistableUriPermission failed for $value: ${it.message}")
        }
        prefs.edit().putString(KEY_FOLDERS, MediaScanRules.encodeFolders(current + value)).apply()
        return true
    }

    /** 移除文件夹并放开它的持久授权；曲库里它带进来的歌留给下一次扫描去裁剪 */
    fun removeFolder(uri: String) {
        val kept = folders().filter { it != uri }
        runCatching {
            context.contentResolver.releasePersistableUriPermission(Uri.parse(uri), Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        prefs.edit().putString(KEY_FOLDERS, MediaScanRules.encodeFolders(kept)).apply()
    }

    /** 两种开关全关时扫描什么都不做 —— 设置页要用它给一句提醒，而不是给一个"扫完 0 首"的困惑 */
    fun anyMethodEnabled(): Boolean = useMediaStore || useSafScan

    private companion object {
        const val PREFS = "catclaw_state"
        const val KEY_MEDIA_STORE = "use_media_store"
        const val KEY_SAF_SCAN = "use_saf_scan"
        const val KEY_FOLDERS = "music_folder_uris"
        const val TAG = "CatClawScanSettings"
    }
}
