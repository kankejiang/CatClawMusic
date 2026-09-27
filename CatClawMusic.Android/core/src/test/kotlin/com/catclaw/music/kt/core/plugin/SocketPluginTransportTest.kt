package com.catclaw.music.kt.core.plugin

import java.io.BufferedReader
import java.io.InputStreamReader
import java.io.OutputStream
import java.net.ServerSocket
import java.net.Socket
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 打**真 socket**（本机 `ServerSocket`，端口 0 让系统分配），不是假 InputStream。
 * TCP 会把报文切成任意片段，这正是 [FrameDecoder] 存在的理由 —— 用内存假件永远测不出来。
 *
 * 用 `runBlocking` 而不是 `runTest`：等真实网络连接时虚拟时间调度器会把等待误判成卡死。
 */
class SocketPluginTransportTest {

    /** 起一个只回一条线的服务；返回端口与服务端实际收到的请求行。 */
    private class Peer(val replies: List<(String) -> String?>) : AutoCloseable {
        val server = ServerSocket(0)
        val port = server.localPort
        val received = mutableListOf<String>()
        var connections = 0
        private val thread: Thread

        init {
            thread = Thread {
                runCatching {
                    while (true) {
                        val sock = server.accept()
                        val index = synchronized(this) { connections++ }
                        Thread { handle(sock, index) }.apply { isDaemon = true }.start()
                    }
                }
            }
            thread.isDaemon = true
            thread.start()
        }

        private fun handle(sock: Socket, index: Int) {
            sock.use {
                val reader = BufferedReader(InputStreamReader(it.getInputStream(), Charsets.UTF_8))
                val out = it.getOutputStream()
                while (true) {
                    val line = reader.readLine() ?: return
                    synchronized(this) { received += line }
                    val reply = replies.getOrNull(minOf(index, replies.lastIndex))?.invoke(line) ?: return
                    out.write(FrameDecoder.encode(reply).toByteArray(Charsets.UTF_8))
                    out.flush()
                }
            }
        }

        override fun close() {
            runCatching { server.close() }
            runCatching { thread.interrupt() }
        }
    }

    private fun transport(port: Int, timeoutMs: Int = 4_000) = SocketPluginTransport(port = port, timeoutMs = timeoutMs)

    @Test
    fun roundTripOverARealSocket() = runBlocking {
        Peer(listOf { """{"protocol":2,"id":1,"result":{"songs":[]}}""" }).use { peer ->
            val answer = transport(peer.port).exchange("""{"protocol":2,"id":1,"method":"search"}""")
            assertTrue(answer.contains("\"songs\""))
            assertEquals(1, peer.connections)
            assertTrue("写侧必须补且只补一个 LF", peer.received.single().endsWith("}"))
        }
    }

    @Test
    fun replySplitAcrossTwoWritesIsReassembled() = runBlocking {
        val peer = ServerSocket(0)
        val whole = """{"protocol":2,"id":7,"result":{"ok":true}}"""
        val serve = Thread {
            runCatching {
                peer.accept().use { s ->
                    val out = s.getOutputStream()
                    val reader = BufferedReader(InputStreamReader(s.getInputStream(), Charsets.UTF_8))
                    reader.readLine()
                    out.write(whole.take(9).toByteArray(Charsets.UTF_8)) // 半个报文
                    out.flush()
                    Thread.sleep(120)
                    out.write((whole.substring(9) + "\n").toByteArray(Charsets.UTF_8))
                    out.flush()
                }
            }
        }.apply { isDaemon = true; start() }
        val answer = transport(peer.localPort).exchange("""{"protocol":2,"id":7,"method":"initialize"}""")
        serve.join(3_000)
        peer.close()
        assertEquals(whole, answer)
    }

    @Test
    fun twoExchangesReuseOneConnection() = runBlocking {
        var n = 0
        Peer(listOf { """{"protocol":2,"id":${++n},"result":{"seq":$n}}""" }).use { peer ->
            val t = transport(peer.port)
            assertTrue(t.exchange("{}").contains("\"seq\":1"))
            assertTrue(t.exchange("{}").contains("\"seq\":2"))
            assertEquals("Bound Service 是长连接，一次调用一个连接等于每次都重新绑定", 1, peer.connections)
        }
    }

    @Test
    fun silentPeerIsRetriedOnAFreshConnectionThenReported() = runBlocking {
        Peer(listOf { Thread.sleep(800); null }).use { peer -> // 收下请求但不回话，靠读超时收敛
            val failure = runCatching { transport(peer.port, timeoutMs = 250).exchange("{}") }.exceptionOrNull()
            assertTrue(failure is java.io.IOException)
            assertTrue(failure!!.message!!.contains("插件服务未能应答"))
            assertEquals("哑掉的连接必须被丢掉重开一次", 2, peer.connections)
        }
    }

    @Test
    fun peerClosingBeforeReplyTriggersReconnectAndSucceeds() = runBlocking {
        Peer(listOf({ null }, { """{"protocol":2,"id":1,"result":{"recovered":true}}""" })).use { peer ->
            val answer = transport(peer.port).exchange("{}")
            assertTrue("第一根连接被对端掐掉，第二根要把它接上", answer.contains("recovered"))
            assertEquals(2, peer.connections)
        }
    }

    @Test
    fun overlongFrameIsDiscardedAndTheNextGoodFrameStillArrives() = runBlocking {
        val good = """{"protocol":2,"id":1,"result":{"fine":true}}"""
        Peer(listOf {
            "x".repeat(FrameDecoder.MAX_LINE_CHARS + 50) + "\n" + good
        }).use { peer ->
            assertTrue(transport(peer.port).exchange("{}").contains("fine"))
        }
    }

    @Test
    fun closeMakesTheNextCallReconnect() = runBlocking {
        Peer(listOf { """{"protocol":2,"id":1,"result":{}}""" }).use { peer ->
            val t = transport(peer.port)
            t.exchange("{}")
            t.close()
            t.exchange("{}")
            assertEquals(2, peer.connections)
        }
    }

    @Test
    fun unreachableServiceSaysWhichEndpoint() = runBlocking {
        val dead = ServerSocket(0)
        val deadPort = dead.localPort
        dead.close() // 先关，端口才真的没人听
        val failure = runCatching {
            transport(deadPort, timeoutMs = 300).exchange("{}")
        }.exceptionOrNull()
        assertTrue(failure!!.message!!.contains("连不上插件服务"))
        assertTrue(failure.message!!.contains(deadPort.toString()))
    }

    /** 真消费者：整条 RPC 层（分帧 + id 回显 + 解码）跑在 socket 上，不是只测传输本身。 */
    @Test
    fun rpcClientWorksOnTopOfTheSocketTransport() = runBlocking {
        Peer(listOf { line ->
            val id = Regex("\"id\":(\\d+)").find(line)?.groupValues?.get(1) ?: "0"
            """{"protocol":2,"id":$id,"result":{"songs":[{"id":"n1","platform":"svc","title":"稻香","duration_ms":227000}]}}"""
        }).use { peer ->
            val client = PluginRpcClient(transport(peer.port))
            val response = client.call("search", buildJsonObject { put("keyword", "稻香") })
            assertTrue("不该收敛成错误：${response.errorMessage}", !response.isError)
            val songs = RpcSong.listFrom((response.result as JsonObject)["songs"])
            assertEquals("稻香", songs.single().title)
            assertEquals("svc:n1", songs.single().remoteId)
        }
    }
}
