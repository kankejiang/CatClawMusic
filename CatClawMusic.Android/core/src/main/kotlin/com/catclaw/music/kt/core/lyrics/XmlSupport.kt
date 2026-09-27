package com.catclaw.music.kt.core.lyrics

import java.io.ByteArrayInputStream
import javax.xml.parsers.DocumentBuilderFactory
import org.w3c.dom.Document

object XmlSupport {

    const val MAX_PARSE_BYTES = 1_000_000

    /**
     * 解析歌词里的 XML。三点注意：
     * - 关掉 DTD / 外部实体，歌词文件是不可信输入，防 XXE；
     * - `isNamespaceAware = false`，这样 `tt:p` / `ttm:agent` 这类带前缀的名字按字面匹配，
     *   与 C# 里"带命名空间和不带命名空间两条路都试"的做法等价且更简单；
     * - 只按 UTF-8 喂字节，声明里的 encoding 一律改写，避免与实际编码打架。
     */
    fun parse(xml: String): Document? {
        val sanitized = sanitizeForXml(xml)
        if (sanitized.isEmpty()) return null
        val normalized = sanitized.replaceFirst(Regex("encoding=\"[^\"]*\""), "encoding=\"UTF-8\"")
        val bytes = normalized.toByteArray(Charsets.UTF_8)
        if (bytes.size > MAX_PARSE_BYTES) return null

        return try {
            val factory = DocumentBuilderFactory.newInstance().apply {
                isNamespaceAware = false
                isValidating = false
                setFeatureQuietly("http://apache.org/xml/features/disallow-doctype-decl", true)
                setFeatureQuietly("http://xml.org/sax/features/external-general-entities", false)
                setFeatureQuietly("http://xml.org/sax/features/external-parameter-entities", false)
                setFeatureQuietly("http://apache.org/xml/features/nonvalidating/load-external-dtd", false)
            }
            factory.newDocumentBuilder().parse(ByteArrayInputStream(bytes))
        } catch (_: Exception) {
            null
        }
    }

    private fun DocumentBuilderFactory.setFeatureQuietly(name: String, value: Boolean) {
        runCatching { setFeature(name, value) }
    }

    /**
     * 清掉 XML 1.0 非法字符。**按码点遍历**，代理对（emoji / 生僻字）不能被拆坏——
     * C# 那版逐 UTF-16 char 过滤，会把增补平面字符静默删掉。
     */
    fun sanitizeForXml(text: String): String {
        if (text.isEmpty()) return text
        val out = StringBuilder(text.length)
        var i = 0
        while (i < text.length) {
            val codePoint = Character.codePointAt(text, i)
            i += Character.charCount(codePoint)
            if (isXmlValid(codePoint)) out.appendCodePoint(codePoint)
        }
        return out.toString()
    }

    private fun isXmlValid(cp: Int): Boolean =
        cp == 0x9 || cp == 0xA || cp == 0xD ||
            cp in 0x20..0xD7FF || cp in 0xE000..0xFFFD || cp in 0x10000..0x10FFFF
}
