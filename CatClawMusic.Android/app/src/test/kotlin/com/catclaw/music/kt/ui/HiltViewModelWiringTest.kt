package com.catclaw.music.kt.ui

import java.io.File
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 凡是 `@Inject constructor` 的 ViewModel 都必须挂 `@HiltViewModel`。
 *
 * 漏注解的表现是**运行期崩溃**而不是编译错误：`hiltViewModel()` 找不到 Hilt 工厂就回落默认工厂，
 * 抛 `Cannot create an instance of class …`，而单测与构建全绿。本轮的权限管理页第一次点进去就崩，
 * 是真机 logcat 抓出来的，所以补这条闸门。
 */
class HiltViewModelWiringTest {

    private val sources: List<File> by lazy {
        val root = File("src/main/kotlin").takeIf { it.isDirectory }
            ?: File("app/src/main/kotlin").takeIf { it.isDirectory }
            ?: error("找不到 :app 源码目录（测试工作目录应为 app 模块根）")
        root.walkTopDown().filter { it.extension == "kt" }.toList().also {
            assertTrue("只扫到 ${it.size} 个文件，八成路径错了", it.size >= 20)
        }
    }

    /** 一条声明行是否带了 @HiltViewModel（注解可能在同一行，也可能在它上面一两行） */
    private fun annotated(lines: List<String>, index: Int): Boolean =
        index >= 0 && (index - 2).coerceAtLeast(0).let { from -> lines.subList(from, index + 1) }.any { "@HiltViewModel" in it }

    private fun missingAnnotations(text: String): List<Int> {
        val lines = text.lines()
        val declaration = Regex("""^class (\w*ViewModel)\b""")
        return lines.mapIndexedNotNull { i, raw ->
            val trimmed = raw.trim()
            if (declaration.find(trimmed) == null) return@mapIndexedNotNull null
            val injected = lines.drop(i).take(3).joinToString(" ").contains("@Inject constructor")
            if (injected && !annotated(lines, i)) i + 1 else null
        }
    }

    @Test
    fun `注入式 ViewModel 全部挂了 HiltViewModel`() {
        val offenders = sources.flatMap { f -> missingAnnotations(f.readText()).map { "${f.name}:$it" } }
        assertTrue("这些 ViewModel 有 @Inject constructor 却没挂 @HiltViewModel，点进去必崩: $offenders", offenders.isEmpty())
    }

    @Test
    fun `闸门要真抓得住漏注解的写法`() {
        val bad = "class FooViewModel @Inject constructor(val repo: Any) : ViewModel()"
        val good = "@HiltViewModel\nclass FooViewModel @Inject constructor(val repo: Any) : ViewModel()"
        val notAViewModel = "class FooRepository @Inject constructor(val repo: Any)"
        assertTrue("漏注解的写法没被抓到", missingAnnotations(bad).isNotEmpty())
        assertFalse("正确写法被误报", missingAnnotations(good).isNotEmpty())
        assertFalse("非 ViewModel 类不该管", missingAnnotations(notAViewModel).isNotEmpty())
    }
}
