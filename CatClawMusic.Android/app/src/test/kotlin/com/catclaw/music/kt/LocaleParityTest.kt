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

    @Test
    fun `默认与英文资源的键集必须一致`() {
        // 少一条英文串，英文界面就会整条露出中文 —— 而只截中文图根本看不出来
        val zh = stringKeys("values")
        val en = stringKeys("values-en")
        assertEquals("只在 values 有", emptySet<String>(), (zh - en))
        assertEquals("只在 values-en 有", emptySet<String>(), (en - zh))
        assertTrue("键集为空说明路径错了", zh.isNotEmpty())
    }

    @Test
    fun `代码里引用的每条 R string 都在默认与英文资源里`() {
        val used = referencedStringKeys()
        assertTrue("一个 R.string 都没解析到，八成是源码路径错了", used.isNotEmpty())
        val zh = stringKeys("values")
        val en = stringKeys("values-en")
        assertEquals("引用了但默认资源没有", emptySet<String>(), used - zh)
        assertEquals("引用了但英文资源没有", emptySet<String>(), used - en)
    }

    /**
     * snake_case 键 = 本端自己写的文案，必须真的有人用；本轮审计就是靠它挖出 `disc_empty`
     * 这种「写了提示却没接界面」的死角。
     * PascalCase 键是 MAUI 资源名的**移植对照表**（About / BackupData / DecodeSettings…），
     * 对应的功能页在安卓侧还没做，暂时没人引用是事实，不报错也不删——它们是待移植清单。
     */
    @Test
    fun `每条本端文案都必须真的有人用`() {
        val zh = stringKeys("values")
        assertTrue("一个键都没读到，八成路径错了", zh.isNotEmpty())
        val used = referencedStringKeys() + xmlReferencedStringKeys()
        val own = zh.filter { it.matches(Regex("^[a-z0-9_]+$")) }
        val dead = own - used
        assertTrue("没有对应 UI 的死文案（接上界面或删掉）: $dead", dead.isEmpty())
    }

    /** 布局/Manifest 里的 @string/x 引用也算有人用 */
    private fun xmlReferencedStringKeys(): Set<String> {
        val files = resDir.parentFile?.walkTopDown()?.filter { it.extension == "xml" }.orEmpty().toList()
        return files.flatMap { f ->
            Regex("""@string/([A-Za-z0-9_]+)""").findAll(f.readText()).map { it.groupValues[1] }
        }.toSet()
    }

    private val srcRoot by lazy {
        File("src/main/kotlin").takeIf { it.isDirectory }
            ?: File("app/src/main/kotlin").takeIf { it.isDirectory }
            ?: error("找不到 kotlin 源码目录（测试工作目录应为 :app 模块根）")
    }

    private fun stringKeys(dir: String): Set<String> {
        val d = File(resDir, dir)
        val files = d.listFiles { f -> f.extension == "xml" } ?: error("没有 $dir 目录")
        return files.flatMap { Regex("""<string name="([^"]+)"""").findAll(it.readText()).map { m -> m.groupValues[1] } }.toSet()
    }

    private fun referencedStringKeys(): Set<String> =
        srcRoot.walkTopDown().filter { it.extension == "kt" }
            .flatMap { Regex("""R\.string\.([A-Za-z0-9_]+)""").findAll(it.readText()).map { m -> m.groupValues[1] } }
            .toSet()

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
