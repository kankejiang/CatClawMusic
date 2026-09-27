package com.catclaw.music.kt.core.lyrics

/** 当前行 + 逐字填充进度，播放页/全屏页/桌面歌词共用 */
data class LyricPresentation(
    val index: Int,
    val line: LyricLine,
    val fillProgress: Double,
    val nextLine: LyricLine?,
)

object LyricsState {

    /** 二分找最后一条 startMs <= position 的行；没有则 -1 */
    fun findLineIndex(lines: List<LyricLine>, positionMs: Long): Int {
        if (lines.isEmpty()) return -1
        var low = 0
        var high = lines.lastIndex
        var result = -1
        while (low <= high) {
            val mid = (low + high) ushr 1
            if (lines[mid].startMs <= positionMs) {
                result = mid
                low = mid + 1
            } else {
                high = mid - 1
            }
        }
        return result
    }

    fun present(
        lyrics: Lyrics?,
        positionMs: Long,
        lineMode: Boolean = false,
    ): LyricPresentation? {
        val lines = lyrics?.lines ?: return null
        val index = findLineIndex(lines, positionMs)
        if (index < 0) return null
        return LyricPresentation(
            index = index,
            line = lines[index],
            fillProgress = LyricFillCalculator.computeFillProgress(
                line = lines[index],
                lineIndex = index,
                allLines = lines,
                positionMs = positionMs,
                lineMode = lineMode,
            ),
            nextLine = lines.getOrNull(index + 1),
        )
    }
}
