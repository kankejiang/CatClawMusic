package com.catclaw.music.kt.core.lyrics

import org.w3c.dom.Element
import org.w3c.dom.Node

/**
 * TTML 歌词解析，逐条对齐 C# `LyricsService.Ttml.cs`。
 *
 * 关键保真点：
 * - 时间格式按四种写法依次尝试，帧率**写死 30fps**（25fps 文件会偏 20%，C# 亦如此）；
 * - 毫秒字段按 `PadRight(3,'0').Substring(0,3)` 截断（`.2456` → 245，不四舍五入）；
 * - 网易云等中文 TTML 的 span begin/end 是**相对行首**的偏移，必须加行起始转绝对，
 *   否则逐字进度瞬间为 1；
 * - 角色 → 对齐/和声走共享的 [LyricMerging.inferRoleAlignment]；
 * - 收尾顺序固定：排序 → 对唱合并 → 逐行对齐标记 → 译文合并。
 */
object TtmlParser {

    private val COLON_MS = Regex("""^(?:(\d+):)?(\d+):(\d+)(?:\.(\d+))?$""")
    private val COLON_FRAMES = Regex("""^(?:(\d+):)?(\d+):(\d+):(\d+)$""")
    private val SECONDS = Regex("""^(\d+(?:\.\d+)?)s$""", RegexOption.IGNORE_CASE)
    private val ISO_DURATION = Regex("""PT(?:(\d+(?:\.\d+)?)H)?(?:(\d+(?:\.\d+)?)M)?(?:(\d+(?:\.\d+)?)S)?""")

    private const val FRAME_RATE = 30L

    fun looksLike(content: String): Boolean =
        content.contains("<tt") || content.contains("xmlns=\"http://www.w3.org/ns/ttml")

    fun parse(xml: String?): Lyrics? {
        if (xml.isNullOrBlank()) return null
        val doc = XmlSupport.parse(xml) ?: return null
        val body = doc.getElementsByTagName("body").item(0) as? Element ?: return null

        val agentNames = HashMap<String, String>()
        collectAgents(doc, agentNames)

        val lines = mutableListOf<LyricLine>()
        val paragraphs = body.getElementsByTagName("p")
        for (i in 0 until paragraphs.length) {
            val p = paragraphs.item(i) as? Element ?: continue
            val startMs = parseTimestamp(p.getAttribute("begin")) ?: continue
            val endMs = parseTimestamp(p.getAttribute("end"))
            val (text, words) = parseParagraph(p, startMs, endMs)

            val (alignment, backing, role, singerName) = resolveRole(p, agentNames)
            lines += LyricLine(
                startMs = startMs,
                text = text,
                wordTimestamps = words,
                alignment = alignment,
                isBackingVocal = backing,
                role = role,
                singerName = singerName,
            )
        }

        if (lines.isEmpty()) return null
        return Lyrics().apply {
            this.lines = lines
            LyricMerging.finalize(this)
        }
    }

    private data class RoleInfo(val alignment: Int, val backing: Boolean, val role: String?, val singerName: String?)

    /** ttm:agent → itunes:role/role → tts:textAlign → 父 div 的同名属性 → 居中 */
    private fun resolveRole(p: Element, agentNames: Map<String, String>): RoleInfo {
        p.attrOrNull("ttm:agent", "agent")?.let { agent ->
            val name = agentNames[agent.lowercase()] ?: agent
            val inferred = LyricMerging.inferRoleAlignment(agent)
            return RoleInfo(inferred.alignment, inferred.isBackingVocal, agent, name)
        }
        p.attrOrNull("itunes:role", "role", "ttml+role")?.let { raw ->
            val inferred = LyricMerging.inferRoleAlignment(raw)
            return RoleInfo(inferred.alignment, inferred.isBackingVocal, null, raw)
        }
        p.attrOrNull("tts:textAlign", "textAlign")?.let { align ->
            return RoleInfo(textAlignOf(align), false, null, null)
        }
        (p.parentNode as? Element)?.let { parent ->
            parent.attrOrNull("ttm:agent", "agent")?.let { agent ->
                val inferred = LyricMerging.inferRoleAlignment(agent)
                return RoleInfo(inferred.alignment, inferred.isBackingVocal, null, agentNames[agent.lowercase()])
            }
            parent.attrOrNull("itunes:role", "role")?.let { raw ->
                val inferred = LyricMerging.inferRoleAlignment(raw)
                return RoleInfo(inferred.alignment, inferred.isBackingVocal, null, raw)
            }
            parent.attrOrNull("tts:textAlign", "textAlign")?.let { align ->
                return RoleInfo(textAlignOf(align), false, null, null)
            }
        }
        return RoleInfo(1, false, null, null)
    }

    private fun textAlignOf(value: String): Int = when (value.trim().lowercase()) {
        "left", "start" -> 0
        "right", "end" -> 2
        else -> 1
    }

    /** 按 local name 取元素：命名空间前缀（`ttm:agent`）在 isNamespaceAware=false 下会留在 tagName 里 */
    private fun elementsByLocalName(root: Node, localName: String): List<Element> {
        val out = ArrayList<Element>()
        val all = (root as? Element)?.getElementsByTagName("*")
            ?: (root as? org.w3c.dom.Document)?.getElementsByTagName("*")
            ?: return out
        for (i in 0 until all.length) {
            val el = all.item(i) as? Element ?: continue
            if (el.tagName.substringAfterLast(':') == localName) out += el
        }
        return out
    }

    private fun collectAgents(doc: org.w3c.dom.Document, out: MutableMap<String, String>) {
        for (el in elementsByLocalName(doc, "agent")) {
            val id = el.attrOrNull("xml:id", "id") ?: continue
            val collected = elementsByLocalName(el, "name")
                .mapNotNull { it.textContent?.trim() }
                .filter { it.isNotEmpty() }
            out[id.lowercase()] = collected.distinct().joinToString(" / ").ifEmpty { id }
        }
    }

    /** 段落文本 + 逐字：直接子节点按原顺序拼，`<br>` 转换行，`<span>` 出字词时间戳 */
    private fun parseParagraph(p: Element, lineStartMs: Long, lineEndMs: Long?): Pair<String, List<WordTimestamp>?> {
        val text = StringBuilder()
        val words = mutableListOf<WordTimestamp>()
        var hasSpan = false
        val lineEnd = lineEndMs ?: (lineStartMs + 5_000)

        var node: Node? = p.firstChild
        while (node != null) {
            when (node.nodeType) {
                Node.TEXT_NODE -> text.append(node.textContent)
                Node.ELEMENT_NODE -> {
                    val el = node as Element
                    when (el.tagName.substringAfterLast(':')) {
                        "br" -> text.append('\n')
                        "span" -> {
                            hasSpan = true
                            val spanBegin = parseTimestamp(el.getAttribute("begin")) ?: lineStartMs
                            val spanEnd = parseTimestamp(el.getAttribute("end")) ?: lineEnd
                            // 相对行首的偏移 → 绝对时间（网易云等中文 TTML 常见）
                            val (absStart, absEnd) =
                                if (spanBegin < lineStartMs && spanEnd <= lineEnd) {
                                    spanBegin + lineStartMs to spanEnd + lineStartMs
                                } else {
                                    spanBegin to spanEnd
                                }
                            val word = elementText(el)
                            if (word.isNotEmpty()) {
                                text.append(word)
                                words += WordTimestamp(word, absStart, (absEnd - absStart).coerceAtLeast(0))
                            }
                        }
                        else -> text.append(el.textContent)
                    }
                }
            }
            node = node.nextSibling
        }

        return text.toString() to words.takeIf { hasSpan && it.isNotEmpty() }
    }

    private fun elementText(el: Element): String {
        val sb = StringBuilder()
        var child: Node? = el.firstChild
        while (child != null) {
            when (child.nodeType) {
                Node.TEXT_NODE -> sb.append(child.textContent)
                Node.ELEMENT_NODE -> {
                    val tag = (child as Element).tagName.substringAfterLast(':')
                    if (tag == "br") sb.append('\n') else sb.append(child.textContent)
                }
            }
            child = child.nextSibling
        }
        return sb.toString()
    }

    fun parseTimestamp(value: String?): Long? {
        val v = value?.trim()?.takeIf { it.isNotEmpty() } ?: return null

        COLON_MS.matchEntire(v)?.let { m ->
            val hours = m.groupValues[1].toLongOrNull() ?: 0L
            val minutes = m.groupValues[2].toLong()
            val seconds = m.groupValues[3].toLong()
            return hours * 3_600_000 + minutes * 60_000 + seconds * 1_000 + padMillis(m.groupValues[4])
        }
        COLON_FRAMES.matchEntire(v)?.let { m ->
            val hours = m.groupValues[1].toLongOrNull() ?: 0L
            val minutes = m.groupValues[2].toLong()
            val seconds = m.groupValues[3].toLong()
            val frames = m.groupValues[4].toLong()
            return hours * 3_600_000 + minutes * 60_000 + seconds * 1_000 + frames * 1_000 / FRAME_RATE
        }
        SECONDS.matchEntire(v)?.let { m ->
            return (m.groupValues[1].toDoubleOrNull() ?: return@let)?.toLong()?.times(1000)
        }
        v.toDoubleOrNull()?.let { return (it * 1000).toLong() }
        ISO_DURATION.matchEntire(v)?.let { m ->
            val h = m.groupValues[1].toDoubleOrNull() ?: 0.0
            val min = m.groupValues[2].toDoubleOrNull() ?: 0.0
            val s = m.groupValues[3].toDoubleOrNull() ?: 0.0
            return ((h * 3600 + min * 60 + s) * 1000).toLong()
        }
        return null
    }

    private fun padMillis(field: String): Long =
        if (field.isEmpty()) 0L else field.padEnd(3, '0').take(3).toLongOrNull() ?: 0L

    private fun Element.attrOrNull(vararg names: String): String? {
        for (name in names) {
            if (hasAttribute(name)) getAttribute(name).trim().takeIf { it.isNotEmpty() }?.let { return it }
        }
        return null
    }
}
