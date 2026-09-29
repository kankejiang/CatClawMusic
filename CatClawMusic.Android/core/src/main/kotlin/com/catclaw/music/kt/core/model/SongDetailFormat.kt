package com.catclaw.music.kt.core.model

/**
 * 歌曲详情页的取值格式化 —— 对应 C# `SongDetailViewModel` 里那几个显示属性。
 *
 * 全部做成纯函数，是因为这一页最容易出的错是**空值显示成 0**：
 * 没轨号显示"第 0 轨"、没码率显示"0 kbps"、年份为 0 显示"0 年"，
 * 看着像数据，其实是缺数据被格式化成了数字。所以每个函数都要么给出真值、要么给出空串。
 */
object SongDetailFormat {

    /** 秒 → `m:ss`，超过一小时才加小时位 */
    fun duration(seconds: Int): String {
        if (seconds <= 0) return ""
        val h = seconds / 3600
        val m = (seconds % 3600) / 60
        val s = seconds % 60
        return if (h > 0) "$h:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}" else "$m:${s.toString().padStart(2, '0')}"
    }

    /** 字节 → 人类可读。0 字节是"没有这个信息"而不是"空文件"，所以回空串 */
    fun bytes(value: Long): String {
        if (value <= 0L) return ""
        val units = listOf("B", "KB", "MB", "GB")
        var v = value.toDouble()
        var i = 0
        while (v >= 1024.0 && i < units.lastIndex) {
            v /= 1024.0
            i += 1
        }
        return if (i == 0) "${value} B" else String.format("%.1f %s", v, units[i])
    }

    fun bitrate(kbps: Int): String = if (kbps > 0) "$kbps kbps" else ""

    /** 文件后缀当格式用：远程曲的路径可能是 URL，取最后一段再取后缀 */
    fun fileFormat(path: String?): String {
        val name = path?.substringBefore('?')?.substringAfterLast('/')?.substringAfterLast('\\') ?: return ""
        val ext = name.substringAfterLast('.', "")
        if (ext.isEmpty() || ext.length > 5 || !ext.all { it.isLetterOrDigit() }) return ""
        return ext.uppercase()
    }

    fun track(number: Int): String = if (number > 0) number.toString() else ""

    fun year(value: Int): String = if (value > 0) value.toString() else ""

    /** 歌词一栏：有侧车/内嵌路径就说有，否则空串（详情页据此显示"无"） */
    fun lyricsSource(lyricsPath: String?, hasEmbedded: Boolean = false): String = when {
        !lyricsPath.isNullOrBlank() -> "file"
        hasEmbedded -> "embedded"
        else -> ""
    }
}
