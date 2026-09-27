package com.catclaw.music.kt.data.ai

import android.content.Context
import com.catclaw.music.kt.core.ai.SourceStorage

/**
 * 音乐源配置的落盘位置。
 *
 * 用**独立**的 prefs 文件而不是 `catclaw_state`：源配置是一份会被 `update_music_source`
 * 整段覆盖的 JSON 文本，混在全局状态里会让两边的读写互相牵连（尤其 catclaw_state 还存着模型配置）。
 */
class PrefsSourceStorage(context: Context) : SourceStorage {
    private val prefs = context.applicationContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    override fun read(): String? = prefs.getString(KEY, null)

    override fun write(value: String) {
        prefs.edit().putString(KEY, value).apply()
    }

    private companion object {
        const val PREFS = "catclaw_music_sources"
        /** 键名沿用 C# 的 StoreKey，配置在两端迁移时不用重命名 */
        const val KEY = "music_sources_v1"
    }
}
