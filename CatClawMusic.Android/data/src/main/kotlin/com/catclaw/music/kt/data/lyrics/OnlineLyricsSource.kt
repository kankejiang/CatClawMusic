package com.catclaw.music.kt.data.lyrics

import com.catclaw.music.kt.core.model.Song

/**
 * 在线歌词源：本地/侧车都取不到时，按歌曲来源去平台拉一次。
 *
 * 返回值是**可直接落库的 LRC 文本**（含翻译段：原文行 + 同时间戳的译文行），
 * 这样 `LyricsReader` 缓存到 Local 后，下次 `LrcParser.parse` 能按同一套规则把译文并回主行，
 * 不需要为在线歌词单独造一套缓存格式。
 */
interface OnlineLyricsSource {
    suspend fun lyricsFor(song: Song): String?
}
