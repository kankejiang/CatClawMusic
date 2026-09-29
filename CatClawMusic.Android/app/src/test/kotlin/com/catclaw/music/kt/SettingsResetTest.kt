package com.catclaw.music.kt

import com.catclaw.music.kt.data.settings.SettingsReset
import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 「重置所有设置」的名单闸门。
 *
 * 这条不变量没法用行为测试抓：漏掉一个 prefs 文件时，重置**照样成功**，
 * 只是留下一半旧设置——用户看到的就是"重置了但没完全重置"，最难查。
 * 所以直接扫源码：凡是取过 SharedPreferences 的文件名，都必须登记在 [SettingsReset.STORES] 里，
 * 反过来登记表里也不许留着源码已经不再使用的名字。
 */
class SettingsResetTest {

    private val moduleRoots by lazy {
        listOf(
            File("src/main/kotlin").takeIf { it.isDirectory } ?: File("app/src/main/kotlin"),
            File("../data/src/main/kotlin"),
        ).also {
            assertTrue("扫不到源码目录，八成工作目录不对: ${it.map { f -> f.absolutePath }}", it.all { f -> f.isDirectory })
        }
    }

    /** `getSharedPreferences("x"…)` 的字面量，和 `const val PREFS = "x"` 这类文件名常量 */
    private val directName = Regex("""getSharedPreferences\(\s*"([^"]+)"""")
    private val prefsConst = Regex("""const val \w*PREF\w*\s*=\s*"([^"]+)"""")

    private fun usedNames(): Set<String> = moduleRoots.flatMap { root ->
        root.walkTopDown().filter { it.extension == "kt" }.flatMap { file ->
            val text = file.readText()
            if (!text.contains("getSharedPreferences(")) return@flatMap emptySequence()
            (directName.findAll(text).map { it.groupValues[1] } + prefsConst.findAll(text).map { it.groupValues[1] })
                .toList().asSequence()
        }
    }.toSet()

    @Test
    fun everyPreferenceStoreIsCoveredByTheReset() {
        val used = usedNames()
        assertTrue("源码里一个 prefs 名字都没扫到，检测器本身失效了", used.isNotEmpty())
        val missing = (used - SettingsReset.STORES.toSet()).sorted()
        assertTrue(
            "这些存储文件没登记进重置名单，重置后会留下旧设置: $missing（用到的全集=$used）",
            missing.isEmpty(),
        )
    }

    @Test
    fun resetListHasNoStaleStore() {
        val stale = (SettingsReset.STORES.toSet() - usedNames()).sorted()
        assertTrue("重置名单里有源码已不再使用的存储名: $stale", stale.isEmpty())
    }
}
