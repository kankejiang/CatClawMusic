package com.catclaw.music.kt.data.netease

import android.content.Context
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 网易云播放音质偏好（标准 128k / 极高 320k / 无损 FLAC）。
 *
 * 与插件一致：无损需要登录，未登录请求会被风控降到 320k —— 这一层只记偏好，
 * 真正的降级由 [NeteasePlayUrlResolver] 按当前登录态决定。
 */
@Singleton
class NeteaseQualitySettings @Inject constructor(
    @ApplicationContext context: Context,
) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    var quality: NeteasePlaylistClient.Quality
        get() = prefs.getString(KEY, null)
            ?.let { runCatching { NeteasePlaylistClient.Quality.valueOf(it) }.getOrNull() }
            ?: NeteasePlaylistClient.Quality.Exhigh
        set(value) {
            prefs.edit().putString(KEY, value.name).apply()
        }

    /** 顶部胶囊的循环顺序：极高 → 无损 → 标准 → 极高 */
    fun cycle(): NeteasePlaylistClient.Quality {
        val next = when (quality) {
            NeteasePlaylistClient.Quality.Exhigh -> NeteasePlaylistClient.Quality.Lossless
            NeteasePlaylistClient.Quality.Lossless -> NeteasePlaylistClient.Quality.Standard
            NeteasePlaylistClient.Quality.Standard -> NeteasePlaylistClient.Quality.Exhigh
        }
        quality = next
        return next
    }

    companion object {
        const val PREFS = "netease_quality"
        private const val KEY = "quality"
    }
}
