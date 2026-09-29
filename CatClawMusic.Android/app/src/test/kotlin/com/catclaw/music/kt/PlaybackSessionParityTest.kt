package com.catclaw.music.kt

import java.io.File
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 「能出声的入口必须先把 MediaSession 服务拉起来」这条不变量的闸门。
 *
 * 起因是真机查出来的一个缺陷：冷启动会恢复队列，此时按播放/暂停只是就地 resume，
 * 而 `ensureSessionService()` 原先只挂在 playSongs/playAt/scanAndPlayAll 上 ——
 * 结果服务从没被创建过：没有媒体会话、没有通知栏，桌面歌词宿主（只在服务的 onCreate 里 start）
 * 也就永远不 show()，表现成「设置里标签是开、屏幕上什么都没有」。
 *
 * 这类 bug 用行为测试抓不住（要 fake 一整排 Android 依赖），但它的外形极好判定：
 * 入口函数体里要么自己调 ensure，要么调的那个本地函数最终会 ensure。
 */
class PlaybackSessionParityTest {

    private val srcRoot by lazy {
        File("src/main/kotlin").takeIf { it.isDirectory }
            ?: File("app/src/main/kotlin").takeIf { it.isDirectory }
            ?: error("找不到 kotlin 源码目录（测试工作目录应为 :app 模块根）")
    }

    /** 入口点清单：文件 → 这些函数都可能把声音放出去 */
    private val entryPoints = linkedMapOf(
        "com/catclaw/music/kt/ui/playback/PlayingViewModel.kt" to listOf(
            "toggle", "next", "previous", "playSongs", "playAt", "scanAndPlayAll",
        ),
        "com/catclaw/music/kt/ui/chat/ChatViewModel.kt" to listOf("play"),
        // 音乐库页的出声入口全是「拿一份列表去播」这一族：playAt 已经并给 playRows
        // （页面上有排序与搜索，可见列表才是播放的那一份，不能再用库里下标）
        "com/catclaw/music/kt/ui/library/LibraryViewModel.kt" to listOf(
            "playRows", "playFrom", "playFavoriteAt", "playRecentAt", "playShuffled", "playNext",
        ),
        "com/catclaw/music/kt/ui/playlist/PlaylistViewModel.kt" to listOf("playAt", "playFrom"),
        "com/catclaw/music/kt/ui/remote/RemoteViewModel.kt" to listOf("playAt"),
        "com/catclaw/music/kt/ui/search/SearchViewModel.kt" to listOf("play"),
    )

    private val funBodies = HashMap<String, Map<String, String>>()

    private fun bodiesOf(rel: String): Map<String, String> = funBodies.getOrPut(rel) {
        val text = File(srcRoot, rel).readText()
        parseFunctions(text).also { require(it.isNotEmpty()) { "$rel 里一个函数都没解析到，八成是路径错了" } }
    }

    @Test
    fun `每个能出声的入口都最终会拉起会话服务`() {
        val missing = buildString {
            entryPoints.forEach { (rel, names) ->
                val bodies = bodiesOf(rel)
                names.forEach { name ->
                    val body = bodies[name]
                    if (body == null) {
                        append("$rel 里找不到 fun $name\n")
                    } else if (!ensures(rel, bodies, name, HashSet())) {
                        append("$rel#$name 没有走到 ensure()\n")
                    }
                }
            }
        }
        assertTrue("播放入口漏拉会话服务：\n$missing", missing.isEmpty())
    }

    /** 顺着本地调用最多走两层（playAt → playFrom → ensure），确保路径断了就报错 */
    private fun ensures(rel: String, bodies: Map<String, String>, name: String, seen: MutableSet<String>): Boolean {
        if (!seen.add("$rel#$name")) return false
        val body = bodies[name] ?: return false
        if (body.contains("ensure()") || body.contains("ensureSessionService()")) return true
        return bodies.keys.any { callee ->
            callee != name && Regex("\\b${Regex.escape(callee)}\\s*\\(").containsMatchIn(body) &&
                ensures(rel, bodies, callee, seen)
        }
    }

    /** 按大括号配对切出顶层 `fun 名字(...) ... { ... }` 的函数体；表达式体（`fun x() = y()`）按缩进收集 */
    private fun parseFunctions(text: String): Map<String, String> {
        val out = LinkedHashMap<String, String>()
        val header = Regex("""^\s*(?:private |internal |public |override |suspend )*fun (?:<[^>]+> )?\w+([.\w]*)\s*\(""")
        val lines = text.split('\n')
        var i = 0
        while (i < lines.size) {
            val m = header.find(lines[i])
            if (m == null) { i++; continue }
            val name = Regex("""fun (?:<[^>]+> )?(\w+)""").find(m.value)!!.groupValues[1]
            val indent = lines[i].length - lines[i].trimStart().length
            var depth = 0
            var started = false
            val body = StringBuilder()
            var j = i
            while (j < lines.size) {
                for (ch in lines[j]) {
                    if (ch == '{') { depth++; started = true } else if (ch == '}') depth--
                }
                if (started) body.append(lines[j]).append('\n')
                if (started && depth == 0) break
                if (!started) {
                    if (j > i && (lines[j].isBlank() || (lines[j].length - lines[j].trimStart().length) <= indent)) break
                    body.append(lines[j]).append('\n')
                }
                j++
            }
            if (body.isNotBlank() && name !in out) out[name] = body.toString()
            i = if (j > i) j + 1 else i + 1
        }
        return out
    }
}
