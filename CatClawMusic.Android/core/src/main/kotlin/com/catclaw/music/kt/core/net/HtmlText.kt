package com.catclaw.music.kt.core.net

/**
 * 网页 → 给模型读的可理解文本。对齐 C# `AgentTools.WebFetch.cs` 的行为：
 * 取 `<title>`、剥掉标签、正文按字符数截断。
 *
 * 放在 `:core` 且写成纯函数，是因为**这段逻辑的坑全在细节里**（脚本内容混进正文、
 * `<br>` 丢了换行、`&nbsp;` 变成 `#160;`），而这些用真网络反而测不出来。
 */
object HtmlText {

    /** 与 C# 同一上限：模型上下文里一条网页摘要不值更多，6000 字符够它判断要不要继续深入 */
    const val MAX_CONTENT_CHARS = 6_000

    private val SCRIPT_STYLE = Regex("(?is)<(script|style|noscript|template)[^>]*>.*?</\\1>")
    private val COMMENTS = Regex("(?s)<!--.*?-->")
    private val TITLE = Regex("(?is)<title[^>]*>(.*?)</title>")
    private val BLOCK_END = Regex("(?i)</(p|div|li|tr|h[1-6]|section|article|blockquote|pre)>")
    private val LINE_BREAK = Regex("(?i)<br\\s*/?>")
    private val TAGS = Regex("(?s)<[^>]*>")
    private val BLANK_LINES = Regex("\n{3,}")
    private val MANY_SPACES = Regex("[ \t]{2,}")

    /**
     * 实体解码走**单趟**正则：先解 `&amp;` 再解 `&lt;` 的多趟写法会把 `&amp;lt;`
     * 误解成 `<`（本来它表示的就是字面量 `&lt;`）。
     * 只收录高频项 —— 全量 HTML 实体表要拖两千条映射进来，而模型看得懂没解开的 `&dagger;`。
     */
    private val ENTITY = Regex("&(nbsp|#160|amp|lt|gt|quot|#39|apos|mdash|ndash|hellip|#x?[0-9a-fA-F]+);")

    private val NAMED = mapOf(
        "nbsp" to " ", "#160" to " ", "amp" to "&", "lt" to "<", "gt" to ">",
        "quot" to "\"", "#39" to "'", "apos" to "'", "mdash" to "—", "ndash" to "–", "hellip" to "…",
    )

    /** 单趟实体解码。也供音乐源解析复用（酷我/网易云的歌手名里就带 HTML 实体） */
    fun decodeEntities(input: String): String = ENTITY.replace(input) { match ->
        val body = match.groupValues[1]
        NAMED[body] ?: numericEntity(body) ?: match.value
    }

    private fun numericEntity(body: String): String? {
        // 捕获组里已经不含 `&` 与 `;`，所以要剥的是 `#` / `#x`（写成剥 `&#` 永远剥不掉，
        // 结果就是十进制和十六进制数字实体全都原样留着 —— 这条是测试逼出来的）
        val hex = body.startsWith("#x") || body.startsWith("#X")
        val digits = body.removePrefix("#x").removePrefix("#X").removePrefix("#")
        val code = if (hex) digits.toIntOrNull(16) else digits.toIntOrNull()
        return code?.takeIf { it in 1..0x10FFFF }?.let { runCatching { String(Character.toChars(it)) }.getOrNull() }
    }

    fun title(html: String): String =
        TITLE.find(html)?.groupValues?.get(1)?.let { decodeEntities(it.replace(TAGS, " ")) }
            ?.trim()?.replace(MANY_SPACES, " ").orEmpty()

    fun text(html: String, maxChars: Int = MAX_CONTENT_CHARS): String {
        var s = COMMENTS.replace(html, " ")
        s = SCRIPT_STYLE.replace(s, " ")
        s = LINE_BREAK.replace(s, "\n")
        s = BLOCK_END.replace(s, "\n")
        s = TAGS.replace(s, " ")
        s = decodeEntities(s)
        s = s.lines().joinToString("\n") { it.trim() }
        s = BLANK_LINES.replace(s, "\n\n")
        s = s.trim()
        return if (s.length <= maxChars) s else s.substring(0, maxChars)
    }

    private fun decodeEntitiesLegacyRemoved(input: String): String = input
}
