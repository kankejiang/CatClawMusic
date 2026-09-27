package com.catclaw.music.kt.core.plugin

import java.io.IOException
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

/** 进程式传输的往返、半行、重开与超时。用内存假进程，不起真程序。 */
class ProcessTransportTest {

    private class Fake(
        private val replies: List<String?>,
        var alive: Boolean = true,
    ) : PluginProcess {
        val written = mutableListOf<String>()
        val budgets = mutableListOf<Long>()
        var destroys = 0
        private var index = 0

        override fun writeLine(line: String) { written += line }

        override fun readLine(budgetMs: Long): String? {
            budgets += budgetMs
            val reply = replies.getOrElse(index) { null }
            if (index < replies.size) index++
            return reply
        }

        override fun alive() = alive
        override fun destroy() { destroys++; alive = false }
    }

    private class Harness(timeoutMs: Int = 8_000) {
        var spawns = 0
        val made = mutableListOf<Fake>()
        var next: (Int) -> Fake = { Fake(listOf()) }
        val transport = ProcessPluginTransport(spawn = { spawns++.let { next(it) }.also { made += it } }, timeoutMs = timeoutMs)
    }

    private fun request(id: Int = 1) = PluginProtocol.encodeRequest(id, PluginProtocol.METHOD_SEARCH)

    private fun replyFor(id: Int = 1) = PluginProtocol.encodeResult(id, kotlinx.serialization.json.buildJsonObject { })

    @Test
    fun oneRoundTripSpawnsOnceAndWritesOneLine() = runTest {
        val h = Harness()
        h.next = { Fake(listOf(replyFor())) }
        val line = h.transport.exchange(request())
        assertEquals(replyFor(), line)
        assertEquals(1, h.spawns)
        // 交给进程的是「一行不带结尾」，补 LF 是 PluginProcess 实现的职责（见其 KDoc）
        val written = h.made.single().written.single()
        assertEquals(PluginProtocol.METHOD_SEARCH, PluginProtocol.decodeRequest(written)?.method)
    }

    /** 一行就是一条完整报文：第二次调用复用同一个进程，不再 spawn */
    @Test
    fun twoCallsReuseOneProcessAndConsumeOneLineEach() = runTest {
        val h = Harness()
        h.next = { Fake(listOf(replyFor(1), replyFor(2))) }
        assertEquals(replyFor(1), h.transport.exchange(request(1)))
        assertEquals(replyFor(2), h.transport.exchange(request(2)))
        assertEquals("两条回答只该起一个进程", 1, h.spawns)
    }

    @Test
    fun deadPluginIsRespawnedAndTheCallStillSucceeds() = runTest {
        val h = Harness()
        h.next = { attempt -> if (attempt == 0) Fake(listOf(null), alive = false) else Fake(listOf(replyFor())) }
        assertEquals(replyFor(), h.transport.exchange(request()))
        assertEquals(2, h.spawns)
        assertEquals("死掉的那个要被 destroy 一次", 1, h.made[0].destroys)
    }

    @Test
    fun twoFailuresSurfaceAsOneIoError() = runTest {
        val h = Harness()
        h.next = { Fake(listOf(null), alive = false) }
        val error = assertThrows(IOException::class.java) {
            kotlinx.coroutines.runBlocking { h.transport.exchange(request()) }
        }
        assertTrue(error.message!!.contains("2 次"))
        assertEquals(2, h.spawns)
    }

    @Test
    fun silentPluginIsCutOffWithItsOwnReason() = runTest {
        val h = Harness(timeoutMs = 1)
        h.next = { Fake(listOf(null)) } // 一直有响应能力，但读不到行
        val error = assertThrows(IOException::class.java) {
            kotlinx.coroutines.runBlocking { h.transport.exchange(request()) }
        }
        assertTrue(error.message!!.contains("超时未回话"))
    }

    @Test
    fun readBudgetNeverExceedsTheConfiguredTimeout() = runTest {
        val h = Harness(timeoutMs = 8_000)
        h.next = { Fake(listOf(replyFor())) }
        h.transport.exchange(request())
        val budget = h.made.single().budgets.single()
        assertTrue("预算必须 >0 且 <=timeout", budget in 1..8_000)
    }

    @Test
    fun closeDestroysAndTheNextCallStartsFresh() = runTest {
        val h = Harness()
        h.next = { Fake(listOf(replyFor(), replyFor())) }
        h.transport.exchange(request())
        h.transport.close()
        assertEquals(1, h.made.single().destroys)
        h.transport.exchange(request())
        assertEquals(2, h.spawns)
    }
}
