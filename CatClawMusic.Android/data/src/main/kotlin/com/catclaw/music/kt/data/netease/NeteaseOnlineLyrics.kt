package com.catclaw.music.kt.data.netease

import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.data.lyrics.OnlineLyricsSource
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 网易云在线歌词：移植插件的 `GetLyricsWithRomaAsync`（LRC + 翻译 + 罗马音三流）。
 *
 * 输出格式刻意做成「原文 LRC + 翻译 LRC 直接拼接」：`LrcParser` 会把同时间戳的两行
 * 认成原文/译文并合并（`mergeTranslationLines` 的 300ms 容差配对），
 * 于是这份文本既能立刻解析显示，也能原样写进 `Lyrics.content` 作为离线缓存。
 */
@Singleton
class NeteaseOnlineLyrics @Inject constructor(
    private val api: NeteaseApiClient,
) : OnlineLyricsSource {

    override suspend fun lyricsFor(song: Song): String? {
        val remoteId = song.remoteId ?: return null
        if (!remoteId.startsWith(NeteasePlaylistClient.REMOTE_PREFIX)) return null
        val songId = remoteId.removePrefix(NeteasePlaylistClient.REMOTE_PREFIX)
        val lyrics = runCatching { api.lyrics(songId) }.getOrNull()
        AppLog.d("NeteaseLyrics", "song=${song.title} id=$songId lrcLen=${lyrics?.lrc?.length ?: -1}")
        if (lyrics == null) return null

        return buildString {
            append(lyrics.lrc.trimEnd())
            // 翻译单独成段：同时间戳 → LrcParser 自动配对成 translation
            lyrics.translation?.takeIf { it.isNotBlank() }?.let {
                append('\n')
                append(it.trimEnd())
            }
        }.takeIf { it.isNotBlank() }
    }
}
