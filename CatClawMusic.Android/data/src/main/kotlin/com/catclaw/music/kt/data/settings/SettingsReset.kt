package com.catclaw.music.kt.data.settings

import android.content.Context
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 「重置所有设置」= 把这些 SharedPreferences 文件整份清空，对应 MAUI `GeneralSettingsViewModel.ResetSettingsAsync`
 * 里的 `Preferences.Clear()` + 语言退回"跟随系统"。
 *
 * 名单必须与代码里真实用过的存储文件名一致——`SettingsResetTest` 会**扫源码**钉住这条：
 * 新加一个 prefs 文件却忘了登记，重置后就会留下一半旧设置，那比完全不清更难查。
 *
 * 注意：各设置类是 `@Singleton`，内存里的那份值不会跟着文件消失。
 * 所以页面把话说明白：要全部生效得重启应用（MAUI 同一约束，它没说出来而已）。
 */
@Singleton
class SettingsReset @Inject constructor(
    @ApplicationContext private val context: Context,
) {

    /** @return 被清空的存储文件名，页面用它报"重置了 N 项" */
    fun resetNow(): List<String> {
        STORES.forEach { name ->
            context.getSharedPreferences(name, Context.MODE_PRIVATE).edit().clear().apply()
        }
        return STORES
    }

    companion object {
        /**
         * appearance / lyrics / desktop_lyric / equalizer 各是一个功能的文件；
         * `catclaw_state` 是公共文件（LLM 配置、缓存上限、本地扫描设置、诊断日志、播放状态都在里面），
         * 清一份等于清五处——这与 MAUI 的 `Preferences.Clear()` 语义一致（它也只有一份）。
         */
        val STORES = listOf(
            "appearance",
            "lyrics",
            "desktop_lyric",
            "equalizer",
            "catclaw_state",
            "catclaw_music_sources",
            "app_locale",
        )
    }
}
