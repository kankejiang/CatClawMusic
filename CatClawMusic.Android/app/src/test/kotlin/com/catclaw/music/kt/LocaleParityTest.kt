package com.catclaw.music.kt

import com.catclaw.music.kt.ui.settings.AppLocale
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 语言菜单与 res/values-* 的保真闸门：
 * 菜单里列出的每个语言都必须有真实资源目录，反过来每个目录也都要在菜单里 ——
 * 之前手写清单塞进过 pa/or/as/si/am/kk 等 12 个「有名字没资源」的语言，选了就露出中文。
 */
class LocaleParityTest {

    private val resDir by lazy {
        File("src/main/res").takeIf { it.isDirectory }
            ?: File("app/src/main/res").takeIf { it.isDirectory }
            ?: error("找不到 res 目录（测试工作目录应为 :app 模块根）")
    }

    @Test
    fun `每个 values 语言目录都能在菜单里找到`() {
        val dirs = localeDirs()
        val menuLangs = AppLocale.SUPPORTED.map { it.first.language() }.toSet()
        val orphans = dirs.keys.filter { it !in menuLangs }
        assertTrue("资源目录没有对应菜单项: $orphans", orphans.isEmpty())
    }

    @Test
    fun `菜单里每个语言都有资源目录`() {
        val dirs = localeDirs()
        // zh-CN 落在默认 values/（中性资源本身就是简体中文），没有 values-zh-rCN
        val missing = AppLocale.SUPPORTED.map { it.first.language() }
            .distinct()
            .filter { it != "zh" }
            .filter { it !in dirs }
        assertTrue("菜单列了但包里没资源: $missing", missing.isEmpty())
    }

    @Test
    fun `语言目录数量与迁移产物一致`() {
        // 51 = 49 外语 + zh-TW + 默认 values(zh-CN)
        assertEquals(50, localeDirs().size)
    }

    /** `values-ja` / `values-pt-rPT` → 语言码；`values`、`values-night` 这类非语言目录剔除 */
    private fun localeDirs(): Map<String, String> =
        resDir.listFiles()
            ?.mapNotNull { dir ->
                val name = dir.name
                if (!name.startsWith("values-")) return@mapNotNull null
                val parts = name.removePrefix("values-").split("-")
                val language = parts[0]
                val languageLike = language.length in 2..3 && language.all { it.isLetter() }
                val region = parts.getOrNull(1)
                val regionLike = region == null ||
                    (region.length == 3 && region.startsWith("r") && region.all { it.isLetter() })
                if (!languageLike || !regionLike || parts.size > 2) {
                    null
                } else {
                    language to name
                }
            }
            ?.toMap()
            ?: emptyMap()

    private fun String.language(): String = substringBefore('-')
}
