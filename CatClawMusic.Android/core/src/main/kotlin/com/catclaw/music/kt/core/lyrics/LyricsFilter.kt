package com.catclaw.music.kt.core.lyrics

/**
 * 全屏歌词「删除空行」这一档的过滤规则。从 `LyricsViewModel.visibleLyrics()` 提出来，
 * 是为了让这条默认开启的行为能在 JVM 上被测（VM 侧要 Context + SharedPreferences）。
 *
 * **同实例直返**是这条函数的另一半语义，别当成微优化删掉：
 * 过滤后行数没变时如果重建 `Lyrics`，上层会把它当成"歌词换了"而重新滚动/重算当前行，
 * 用户看到的就是歌词列表在滑动中自己跳一下。
 */
fun Lyrics.withoutEmptyLines(): Lyrics {
    val kept = lines.filter { it.text.isNotBlank() }
    if (kept.size == lines.size) return this
    return Lyrics().apply {
        metadata = this@withoutEmptyLines.metadata
        lines = kept.toMutableList()
        romaLines = this@withoutEmptyLines.romaLines
        translationLines = this@withoutEmptyLines.translationLines
        // 漏了这一条的话：一旦真删掉空行，TTML/AMLL 按 role 定的逐行对齐就退回全局对齐，
        // 表现为"歌词本来左右错落，删空行一开就全挤到一边"。
        hasPerLineAlignment = this@withoutEmptyLines.hasPerLineAlignment
    }
}
