package com.catclaw.music.kt.core.diagnostic

/** 日志级别。`code` 是落盘与解析用的单字母，与 C# `LogService` 的 `[D]/[I]/[W]/[E]` 一致 */
enum class LogLevel(val code: String) {
    Debug("D"),
    Info("I"),
    Warn("W"),
    Error("E"),
    ;

    companion object {
        fun of(code: String): LogLevel? = entries.firstOrNull { it.code == code.uppercase() }
    }
}

/** 一条已解析的日志 */
data class LogEntry(
    val timestamp: String,
    val level: LogLevel,
    val tag: String,
    val message: String,
)

/**
 * 诊断日志的纯规则：格式、解析、脱敏、筛选、裁剪。
 *
 * 放 `:core` 且做成纯函数，是因为这几条都"错起来不报错"：
 * 格式写歪 → 查看页整页空白；脱敏漏一条 → 用户把日志发给别人时把自己的音源服务器密码交出去。
 * 两者都只能在单测里钉，运行时都不会有任何异常。
 */
object DiagnosticLogRules {

    /** 与 C# 同一形状：`HH:mm:ss.fff\t[{L}][{tag}] {msg}` */
    fun format(timestamp: String, level: LogLevel, tag: String, message: String): String =
        "$timestamp\t[${level.code}][$tag] $message"

    /**
     * 时间戳段用 `\s+` 而不是 `\t`：文件被复制进微信/邮件后制表符常被折成空格，
     * 严格匹配会让整页解析不出来，而"日志打不开"看起来像功能坏了。
     */
    private val LINE = Regex(
        """^(\d{1,2}:\d{2}:\d{2}\.\d{1,3})\s+\[([DIWE])]\[([^]\r\n]*)]\s?([\s\S]*)$""",
    )

    /** 解不出来就回 null —— 调用方按"续行"处理，绝不因为一行脏数据丢掉整个文件 */
    fun parse(line: String): LogEntry? {
        val m = LINE.matchEntire(line) ?: return null
        val level = LogLevel.of(m.groupValues[2]) ?: return null
        return LogEntry(
            timestamp = m.groupValues[1],
            level = level,
            tag = m.groupValues[3],
            message = m.groupValues[4],
        )
    }

    /**
     * 落盘前必须过的四道脱敏，对应 C# `LogService.Sanitize` 的四条正则。
     *
     * 为什么值得单独写测试：日志页存在的意义就是"把文件发给别人看"，
     * 而音源 URL 长这样 `subsonic://user:pass@host:443/rest`、请求头长这样 `Authorization: Bearer xxx` ——
     * 少一道就把用户的服务器凭据发出去了。
     *
     * **与 C# 有意不同的一处**：C# 那条 `(authorization|bearer)\s*[=:]\s*\S+` 的 `\S+`
     * 只会吃到 `Bearer` 这个方案名就停，所以 `Authorization: Bearer <jwt>` 里的 **jwt 原样留在文件里**
     * （单测第一次就是从这里红的）。这里显式允许多个方案词，把真正的凭据也盖掉。
     */
    fun redact(message: String): String = message
        .replace(Regex("(?i)\"?(api[_-]?key|apikey|token|secret|access[_-]?token|password)\"?\\s*[=:]\\s*\"?[^\"&\\s,}]+"), "$1=***")
        .replace(Regex("(?i)(authorization|bearer)(\\s*[=:]\\s*)(?:(?:basic|bearer|digest|token)\\s+)*\\S+"), "$1$2***")
        .replace(Regex("://[^/@:\\s]+:[^/@:\\s]+@"), "://***@")
        .replace(Regex("sk-[A-Za-z0-9_-]{4,}"), "sk-***")

    /** 级别/标签/关键词三个条件同时成立才留下（与 C# 页面上的三个筛选器同语义） */
    fun filter(
        entries: List<LogEntry>,
        level: LogLevel? = null,
        tag: String? = null,
        query: String = "",
    ): List<LogEntry> {
        val needle = query.trim()
        return entries.filter { e ->
            (level == null || e.level == level) &&
                (tag.isNullOrBlank() || e.tag == tag) &&
                (needle.isEmpty() || e.message.contains(needle, true) || e.tag.contains(needle, true))
        }
    }

    /** 文件只留最后 maxLines 行：诊断日志是滚动窗口，不是档案 */
    fun trim(lines: List<String>, maxLines: Int): List<String> =
        if (maxLines <= 0 || lines.size <= maxLines) lines else lines.subList(lines.size - maxLines, lines.size)

    /** 页面顶部展示用的标签候选：按出现次数从多到少，同次数按名字稳定 */
    fun tagsOf(entries: List<LogEntry>): List<String> =
        entries.groupingBy { it.tag }.eachCount().entries
            .sortedWith(compareByDescending<Map.Entry<String, Int>> { it.value }.thenBy { it.key })
            .map { it.key }
}
