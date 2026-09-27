package com.catclaw.music.kt.core.plugin

import kotlinx.coroutines.delay
import kotlinx.coroutines.joinAll
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** 分帧、RPC 客户端与插件目录筛选。全部走内存假件，不起进程也不碰 IO。 */
class PluginChannelTest {

    // ── 分帧 ────────────────────────────────────────────────────

    @Test
    fun oneChunkOneLineIsOneMessage() {
        assertEquals(listOf("""{"a":1}"""), FrameDecoder().feed("""{"a":1}""" + "\n"))
    }

    @Test
    fun halfPacketWaitsForTheRest() {
        val d = FrameDecoder()
        assertEquals(emptyList<String>(), d.feed("""{"id"""))
        assertEquals(listOf("""{"id":7}"""), d.feed("""":7}""" + "\n"))
    }

    @Test
    fun stuckPacketsSplitIntoTheirOwnMessages() {
        val d = FrameDecoder()
        assertEquals(listOf("""{"i":1}""", """{"i":2}""", """{"i":3}"""), d.feed("""{"i":1}
{"i":2}
{"i":3}
"""))
    }

    @Test
    fun blankLinesAreNotMessages() {
        val d = FrameDecoder()
        assertEquals(listOf("""{"i":1}"""), d.feed("\n\n" + """{"i":1}""" + "\n"))
    }

    @Test
    fun runawayLineIsDroppedWithoutPoisoningTheNextOne() {
        val d = FrameDecoder(maxLineChars = 32)
        assertEquals(emptyList<String>(), d.feed("x".repeat(40)))
        assertEquals(emptyList<String>(), d.feed("\n"))
        assertEquals(listOf("""{"ok":true}"""), d.feed("""{"ok":true}""" + "\n"))
    }

    @Test
    fun resetForgetsAPartialLine() {
        val d = FrameDecoder()
        d.feed("""{"half""")
        d.reset()
        assertEquals(listOf("""{"fresh":1}"""), d.feed("""{"fresh":1}""" + "\n"))
    }

    @Test
    fun encoderAlwaysEndsWithExactlyOneLf() {
        assertEquals("{}\n", FrameDecoder.encode("{}"))
        assertEquals("{}\n", FrameDecoder.encode("{}\r\n"))
        assertEquals("{}\n", FrameDecoder.encode("{}\n\n\n"))
    }

    // ── RPC 客户端 ──────────────────────────────────────────────

    private class FakeTransport(
        private val reply: (String) -> String = { """{"protocol":2,"id":1,"result":{}}""" },
        private val latencyMs: Long = 0,
    ) : PluginTransport {
        val sent = mutableListOf<String>()
        var inFlight = 0
        var maxInFlight = 0
        var closed = false

        override suspend fun exchange(requestLine: String): String {
            sent += requestLine
            inFlight++
            maxInFlight = maxOf(maxInFlight, inFlight)
            if (latencyMs > 0) delay(latencyMs)
            inFlight--
            return reply(requestLine)
        }

        override suspend fun close() { closed = true }
    }

    @Test
    fun successfulCallEchoesIdAndCarriesResult() = runTest {
        val t = FakeTransport(reply = { req ->
            val id = PluginProtocol.decodeRequest(req)!!.id
            PluginProtocol.encodeResult(id, buildJsonObject { put("songs", kotlinx.serialization.json.buildJsonArray { }) })
        })
        val response = PluginRpcClient(t).call(PluginProtocol.METHOD_SEARCH, buildJsonObject { put("keyword", "周") })
        assertFalse(response.isError)
        assertEquals(1, response.id)
        assertEquals(PluginProtocol.VERSION, PluginProtocol.decodeRequest(t.sent.single())!!.protocol)
        assertEquals("周", PluginProtocol.decodeRequest(t.sent.single())!!.params["keyword"]?.jsonPrimitive?.content)
    }

    @Test
    fun unparsableReplyBecomesAParseErrorNotACrash() = runTest {
        val response = PluginRpcClient(FakeTransport(reply = { "not json at all" })).call(PluginProtocol.METHOD_SHUTDOWN)
        assertTrue(response.isError)
        assertEquals(PluginProtocol.ERR_PARSE, response.errorCode)
        assertFalse("报文写坏了不该重试", response.retryable)
    }

    @Test
    fun silentPluginIsCutOffAndMarkedRetryable() = runTest {
        val client = PluginRpcClient(FakeTransport(latencyMs = 5_000), timeoutMs = 100)
        val response = client.call(PluginProtocol.METHOD_SEARCH)
        assertTrue(response.isError)
        assertTrue(response.retryable)
        assertTrue(response.errorMessage.contains("无响应"))
    }

    @Test
    fun oneClientNeverHasTwoRequestsInFlight() = runTest {
        val t = FakeTransport(latencyMs = 20)
        val client = PluginRpcClient(t)
        (1..4).map { launch { client.call(PluginProtocol.METHOD_SEARCH) } }.joinAll()
        assertEquals("同插件必须串行，否则并发负担推给了插件作者", 1, t.maxInFlight)
        assertEquals(4, t.sent.size)
    }

    @Test
    fun requestIdsIncreaseAndMatchResponses() = runTest {
        val t = FakeTransport(reply = { req -> PluginProtocol.encodeResult(PluginProtocol.decodeRequest(req)!!.id, buildJsonObject { }) })
        val client = PluginRpcClient(t)
        assertEquals(listOf(1, 2, 3), listOf(client.call("a"), client.call("b"), client.call("c")).map { it.id })
    }

    @Test
    fun closeReachesTheTransport() = runTest {
        val t = FakeTransport()
        PluginRpcClient(t).close()
        assertTrue(t.closed)
    }

    // ── 注册表 ──────────────────────────────────────────────────

    private fun manifestJson(id: String, protocol: Int = 2, platforms: List<String> = listOf("android"), minHost: String = "") =
        Json.encodeToString(
            kotlinx.serialization.json.JsonElement.serializer(),
            PluginManifest(
                protocolVersion = protocol, id = id, name = id, version = "1.0.0",
                platforms = platforms, capabilities = listOf("search"), minHostVersion = minHost,
            ).toJson(),
        )

    private fun registryOf(vararg texts: String, hostVersion: String = "1.8.13") = PluginRegistry(
        hostPlatform = "android",
        hostVersion = hostVersion,
        readManifests = { texts.toList() },
    )

    @Test
    fun usableKeepsOnlyWhatThisMachineCanRun() {
        val r = registryOf(
            manifestJson("good"),
            manifestJson("win-only", platforms = listOf("windows")),
            manifestJson("old-proto", protocol = 1),
            "totally not json",
        )
        assertEquals(listOf("good"), r.usable().map { it.id })
        val problems = r.scan().associate { it.manifest.id to it.problem }
        assertEquals(ManifestProblem.PlatformUnavailable("android"), problems["win-only"])
        assertEquals(ManifestProblem.UnsupportedProtocol(1), problems["old-proto"])
        assertEquals(ManifestProblem.Malformed, problems[""])
    }

    @Test
    fun brokenEntriesSortLastButAreNeverSwallowed() {
        val scan = registryOf("garbage", manifestJson("aaa"), manifestJson("zzz")).scan()
        assertEquals(3, scan.size)
        assertTrue(scan.last().problem != null)
        assertEquals(listOf("aaa", "zzz"), scan.take(2).map { it.manifest.id })
    }

    @Test
    fun hostBelowTheDeclaredFloorIsReportedWithThatFloor() {
        val r = registryOf(manifestJson("needs-newer", minHost = "2.0.0"), hostVersion = "1.8.13")
        assertEquals(ManifestProblem.HostTooOld("2.0.0"), r.scan().single().problem)
        assertNull("没声明下限就永远满足", registryOf(manifestJson("fine")).scan().single().problem)
    }

    @Test
    fun capabilityRankingPutsSupportingSourceFirst() {
        val withLyrics = PluginManifest(
            protocolVersion = 2, id = "b-lyrics", name = "b", version = "1",
            platforms = listOf("android"), capabilities = listOf("search", "lyrics"),
        )
        val plain = withLyrics.copy(id = "a-plain", capabilities = listOf("search"))
        val r = PluginRegistry("android", "1.8.13") { listOf(plain, withLyrics).map { Json.encodeToString(kotlinx.serialization.json.JsonElement.serializer(), it.toJson()) } }
        assertEquals(listOf("b-lyrics", "a-plain"), r.rankedByCapability("lyrics").map { it.id })
    }
}
