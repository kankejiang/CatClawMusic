package com.catclaw.music.kt.ui

import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 字符串资源里的格式化占位符必须是合法的。
 *
 * ## 为什么需要这条测试
 *
 * 写坏的占位符（例如 `%1 首` 而不是 `%1$d 首`）**编译期完全正常**，
 * 一运行到 `stringResource()` 就抛 `UnknownFormatConversionException` 导致崩溃。
 *
 * 真机验收时就踩到了：批量处理页一打开就闪退，堆栈指向
 * `BatchScreen.kt:94` 的 `stringResource(R.string.batch_selected, ...)`，
 * 根因是这条字符串在生成时 `$d` 被 shell 变量展开吃掉，留下了 `%1 首`。
 *
 * 这类问题只能靠测试守：遍历所有 strings.xml，把含 `%` 的条目按 Android 的真实
 * 格式化规则校验一遍。
 */
class StringFormatResourcesTest {

    private fun stringDirs(): List<File> =
        listOf(
            File("src/main/res"),
            File("app/src/main/res"),
        ).filter { it.isDirectory }

    /** 收集某个 res 目录下所有 values-xx 子目录里 strings.xml 的 (name, value) */
    private fun collectStrings(resDir: File): List<Triple<File, String, String>> {
        val out = mutableListOf<Triple<File, String, String>>()
        val valuesDirs = resDir.listFiles()?.filter {
            it.isDirectory && it.name.startsWith("values")
        } ?: return out
        valuesDirs.forEach { dir ->
            val file = File(dir, "strings.xml")
            if (!file.isFile) return@forEach
            val text = file.readText()
            Regex("""<string\s+name="([^"]+)"[^>]*>(.*?)</string>""", RegexOption.DOT_MATCHES_ALL)
                .findAll(text)
                .forEach { m ->
                    out += Triple(file, m.groupValues[1], m.groupValues[2])
                }
        }
        return out
    }

    @Test
    fun allFormatPlaceholdersAreValid() {
        val dirs = stringDirs()
        assertTrue("找不到 res 目录", dirs.isNotEmpty())

        val offenders = mutableListOf<String>()
        dirs.forEach { resDir ->
            collectStrings(resDir).forEach { (file, name, raw) ->
                val value = unescape(raw)
                if (!value.contains('%')) return@forEach
                val problem = validateFormat(value)
                if (problem != null) {
                    offenders += "${file.name}:$name -> $problem  (值: $value)"
                }
            }
        }
        assertTrue(
            "字符串格式化占位符非法（运行时会抛 UnknownFormatConversionException）:\n" +
                offenders.joinToString("\n"),
            offenders.isEmpty(),
        )
    }

    /**
     * 校验格式串。规则与 [java.util.Formatter] 一致的最小集合：
     * `%` 后面必须跟 `[参数索引$]` + 转换符（s/d/f/x/…），`%%` 是转义。
     *
     * @return null 表示合法，否则返回问题描述
     */
    private fun validateFormat(value: String): String? {
        var i = 0
        while (i < value.length) {
            if (value[i] != '%') {
                i++
                continue
            }
            i++ // 跳过 %
            if (i >= value.length) return "% 在结尾"
            if (value[i] == '%') {
                i++ // %% 转义
                continue
            }
            // 可选：参数索引 N$
            val idxStart = i
            while (i < value.length && value[i].isDigit()) i++
            if (i < value.length && value[i] == '$') {
                if (i == idxStart) return "参数索引 $ 前没有数字"
                i++ // 跳过 $
            } else {
                // 没有 $：这些数字属于宽度/精度，但我们的字符串不该出现这种用法，
                // 因为「%1 首」正是这样写错的
                if (i > idxStart) {
                    return "「%${value.substring(idxStart, i)}」缺少 \$d/\$s 转换符"
                }
            }
            // 可选：标志/宽度/精度（简化处理，遇到转换符为止）
            while (i < value.length && (value[i] in "-+#0 ,(" || value[i].isDigit() || value[i] == '.')) i++
            if (i >= value.length) return "占位符没有转换符"
            val conv = value[i]
            if (conv !in "sdfxXoeEgGcCbBhHn") {
                return "非法转换符 '$conv'"
            }
            i++
        }
        return null
    }

    /** XML 里 `&amp;` 等实体还原，避免影响判断 */
    private fun unescape(raw: String): String = raw
        .replace("&amp;", "&")
        .replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&quot;", "\"")
        .replace("&apos;", "'")
        .replace("\\'", "'")
        .replace("\\\"", "\"")
        .trim()
    @Test
    fun validateFormatCatchesKnownBadPatterns() {
        // 这几种都是真机上真实会崩的写法
        assertTrue("「%1 首」应被判非法", validateFormat("已选 %1 首") != null)
        assertTrue("「%1 lines」应被判非法", validateFormat("%1 lines") != null)
        assertTrue("「%」结尾应被判非法", validateFormat("abc %") != null)
        assertTrue("「%z」非法转换符", validateFormat("%z") != null)

        // 合法写法不能被误判
        assertTrue("「%1\$d 首」应合法", validateFormat("已选 %1\$d 首") == null)
        assertTrue("「%1\$s」应合法", validateFormat("%1\$s") == null)
        assertTrue("「%%」转义应合法", validateFormat("100%%") == null)
        assertTrue("无占位符应合法", validateFormat("普通文本") == null)
    }}
