package com.catclaw.music.kt.lyrico.runtime

/**
 * 引擎自检：验证 QuickJS 绑定在本机可用，且我们依赖的三件事都成立 ——
 *   1. 标量求值
 *   2. 跨 evaluate 保留上下文（定义函数后再次调用）
 *   3. Kotlin 接口能绑成 JS 全局，并被插件那样以对象方法的形式调用
 *
 * 用途：P0 阶段在真机确认引擎可用；也方便以后换引擎时回归。
 */
object LyricoEngineSmoke {

    /** 供 JS 调用的宿主接口（真实 Platform 的缩水版，只为验证调用链）。 */
    interface HostProbe {
        fun echo(text: String): String
        fun sum(a: Int, b: Int): Int
    }

    /** @return 自检报告（逐项 PASS/FAIL），调用方负责打印或断言 */
    fun run(): String {
        val lines = mutableListOf<String>()
        var engine: LyricoJsEngine? = null
        try {
            engine = LyricoJsEngine.create()

            // 1) 标量
            val two = engine.evaluate("1+1")
            lines += "scalar: eval(1+1)=$two => ${if (two is Number && two.toInt() == 2) "PASS" else "FAIL"}"

            // 2) 跨 evaluate 保留上下文 + JSON 往返
            engine.evaluate(
                """
                globalThis.__echo = function (s) { return 'echo:' + s; };
                globalThis.__json = function (payload) {
                  var o = JSON.parse(payload);
                  return JSON.stringify({ ok: true, got: o.title, n: o.n });
                };
                """.trimIndent(),
            )
            val echoed = engine.evaluate("__echo('hi')")
            lines += "ctx: __echo('hi')=$echoed => ${if (echoed == "echo:hi") "PASS" else "FAIL"}"

            val json = engine.evaluate("""__json('{"title":"晴天","n":3}')""")
            val jsonOk = json is String && json.contains("\"ok\":true") && json.contains("晴天")
            lines += "json: __json(...)=$json => ${if (jsonOk) "PASS" else "FAIL"}"

            // 3) Kotlin 接口 -> JS 全局
            engine.bindGlobal(
                "__host",
                HostProbe::class.java,
                object : HostProbe {
                    override fun echo(text: String): String = "host:$text"
                    override fun sum(a: Int, b: Int): Int = a + b
                },
            )
            val hostEcho = engine.evaluate("__host.echo('x')")
            val hostSum = engine.evaluate("__host.sum(2,3)")
            val hostOk = hostEcho == "host:x" && (hostSum as? Number)?.toInt() == 5
            lines += "host: echo=$hostEcho sum=$hostSum => ${if (hostOk) "PASS" else "FAIL"}"

            // 4) 插件风格：先定义 Platform 对象，再像 Lyrico 插件那样调用
            engine.evaluate(
                """
                var Platform = { app: { getUserAgent: function () { return 'lyrico-probe'; } } };
                """.trimIndent(),
            )
            val ua = engine.evaluate("Platform.app.getUserAgent()")
            lines += "nested: Platform.app.getUserAgent()=$ua => ${if (ua == "lyrico-probe") "PASS" else "FAIL"}"
        } catch (t: Throwable) {
            lines += "EXCEPTION ${t.javaClass.simpleName}: ${t.message}"
        } finally {
            engine?.close()
        }
        return lines.joinToString("\n")
    }
}
