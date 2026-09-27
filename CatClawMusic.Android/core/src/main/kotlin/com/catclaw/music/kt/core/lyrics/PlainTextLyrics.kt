package com.catclaw.music.kt.core.lyrics

/**
 * 没有时间戳的歌词怎么显示 —— 对应 C# `LyricsService.Lrc.cs:424-458` 的 `ParsePlainTextLyrics`。
 *
 * 关键是**每行合成 5 秒**：一篇纯文本歌词也得跟着播放往下走，而不是整篇糊在 00:00。
 * 这个"看起来在动"的效果是 MAUI 侧的行为，本端此前根本没有这一档兜底
 * （`LrcParser` 找不到时间戳就返回 null），所以无时间戳的侧车 / 内嵌 / 远程歌词一律不显示。
 */
private val LINE_STEP_MS = 5_000L

/** C# 用未锚定的 `TagRegex.Match(line)`，这里同样按"行内出现即算标签行"处理 */
private val TAG_LINE = Regex("""\[(ti|ar|al|by|re|ve):(.+)\]""", RegexOption.IGNORE_CASE)

/** 与 C# `NoLyricsPlaceholderText` 及它顺手过滤掉的两种中文占位一致 */
private val PLACEHOLDERS = listOf("纯音乐", "暂无歌词", "还没有歌词")

fun plainTextLyrics(content: String): Lyrics? {
    if (looksLikeBinaryText(content)) return null
    val lyrics = Lyrics()
    var lineIndex = 0
    val normalized = content.replace("\r\n", "\n").replace("\r", "\n")
    for (raw in normalized.split('\n')) {
        val line = raw.trim()
        if (line.isEmpty()) continue
        if (line.startsWith("//")) continue
        if (TAG_LINE.containsMatchIn(line)) {
            lineIndex++ // 标签行也占一格时间轴，与 C# 同一个 `lineIndex++` 后 continue
            continue
        }
        if (PLACEHOLDERS.any { line.contains(it) }) continue
        lyrics.lines.add(LyricLine(startMs = lineIndex * LINE_STEP_MS, text = line))
        lineIndex++
    }
    return lyrics.lines.ifEmpty { null }?.let { lyrics }
}

/**
 * 二进制内容被编码回退链"侥幸解出"一个字符串时的最后一道闸。
 *
 * C# 那边不需要这条：它的外挂歌词只从文本流里读。本端多一个远程字节流入口
 * （`OkHttpBytesFetcher` 拿的是字节，服务器返什么都有可能），
 * 所以"解得开"不等于"是歌词" —— 控制字符占了两成就直接把整篇当垃圾。
 */
internal fun looksLikeBinaryText(content: String): Boolean {
    if (content.isEmpty()) return false
    val junk = content.count { it.isISOControl() && it != '\t' && it != '\n' && it != '\r' }
    return junk * 4 > content.length
}

/**
 * 非歌词内容防御 —— 对应 C# `LyricsService.Lrc.cs:499-509`。
 * 抓回来的常常是错误页或 JSON 响应，没有这一道就会被当成"纯文本歌词"整篇刷在屏幕上。
 */
internal fun looksLikeMarkup(content: String): Boolean {
    val trimmed = content.trim()
    return (trimmed.startsWith("<") && trimmed.contains(">")) ||
        (trimmed.startsWith("{") && trimmed.endsWith("}")) ||
        trimmed.startsWith("<!DOCTYPE", ignoreCase = true) ||
        trimmed.contains("<html", ignoreCase = true) ||
        trimmed.contains("<body", ignoreCase = true)
}
