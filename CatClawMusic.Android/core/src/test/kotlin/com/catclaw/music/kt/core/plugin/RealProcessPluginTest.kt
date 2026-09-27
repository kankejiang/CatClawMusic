package com.catclaw.music.kt.core.plugin

import java.io.File
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test

/**
 * 拿一个**用 Node 写的真子进程**跑完整往返 —— 这是"双端通用"这句话唯一算数的证据：
 * Kotlin 侧自己跟自己说话永远说得通，换一门语言才会暴露字段名、编码、行边界上的想当然。
 *
 * 用 `runBlocking` 而不是 `runTest`：这里要等的是真实进程，虚拟时间调度器会把等待误判成卡死。
 * 本机没有 node 时整类跳过（`assumeTrue`），不伪装成通过。
 */
class RealProcessPluginTest {

    private val node: String? by lazy {
        sequenceOf("node", "node.exe").firstOrNull { exe ->
            runCatching { ProcessBuilder(exe, "--version").start().waitFor() == 0 }.getOrDefault(false)
        }
    }

    private fun script(name: String): String =
        File(javaClass.getResource("/plugin/$name")!!.toURI()).absolutePath

    @Before
    fun requireNode() {
        assumeTrue("本机没有 node，跨语言往返用例跳过", node != null)
    }

    private inner class Counting(private val scriptName: String) {
        var spawns = 0
        val transport = ProcessPluginTransport(
            spawn = {
                spawns++
                ProcessBuilderPluginProcess(listOf(node!!, script(scriptName)))
            },
            timeoutMs = 20_000,
        )

        fun client() = PluginRpcClient(transport)
    }

    @Test
    fun foreignProcessAnswersASearchInTheSameShape() = runBlocking {
        val host = Counting("echo_plugin.js")
        val response = host.client().call(
            PluginProtocol.METHOD_SEARCH,
            buildJsonObject { put("keyword", "周杰伦") },
        )
        assertFalse("跨语言第一次握手就报错，说明报文形状有假设没对齐：${response.errorMessage}", response.isError)

        val songs = RpcSong.listFrom((response.result as JsonObject)["songs"])
        assertEquals(1, songs.size)
        assertEquals("稻香", songs[0].title)
        assertEquals("周杰伦", songs[0].artist)
        assertEquals(227_000L, songs[0].durationMs)
        assertEquals("node:n1", songs[0].remoteId)
        assertEquals(1, host.spawns)
    }

    @Test
    fun handshakeCarriesAProtocolVersionThatNegotiationCanUse() = runBlocking {
        val response = Counting("echo_plugin.js").client().call(PluginProtocol.METHOD_INITIALIZE)
        val found = (response.result as JsonObject)["protocolVersion"]?.jsonPrimitive?.intOrNull
        assertEquals(PluginProtocol.VERSION, found)
        assertTrue(PluginProtocol.negotiate(found ?: -1))
    }

    @Test
    fun unknownMethodComesBackAsTheContractedErrorCode() = runBlocking {
        val response = Counting("echo_plugin.js").client().call("browser_open")
        assertTrue(response.isError)
        assertEquals(PluginProtocol.ERR_METHOD_NOT_FOUND, response.errorCode)
    }

    /** 真进程退出后，下一次调用要能把进程重新拉起来并照常应答 */
    @Test
    fun realDeathIsFollowedByARealRestart() = runBlocking {
        val host = Counting("dying_plugin.js")
        val client = host.client()
        val first = client.call(PluginProtocol.METHOD_INITIALIZE)
        assertFalse(first.isError)
        assertEquals(1, host.spawns)

        // 上面的 initialize 让这个进程自己 exit(7) 了
        val second = client.call(PluginProtocol.METHOD_SEARCH, buildJsonObject { put("keyword", "x") })
        assertFalse("重开之后应该照常能答：" + second.errorMessage, second.isError)
        assertEquals(2, host.spawns)
    }
}
