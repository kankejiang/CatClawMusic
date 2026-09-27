package com.catclaw.music.kt.core.plugin

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** 插件协议 v2 的清单与报文层。纯字符串进出，不需要任何 Android 运行时。 */
class PluginProtocolTest {

    private val fullManifest = PluginManifest(
        protocolVersion = 2,
        id = "netease-online",
        name = "网易云音乐",
        version = "1.2.0",
        author = "lvjin",
        description = "在线音源",
        platforms = listOf("android", "windows"),
        capabilities = listOf("search", "play", "lyrics"),
        entrypoint = "bin/netease-plugin",
        minHostVersion = "1.8.0",
        timeoutMs = 8_000,
        sha256 = "ab12",
        updateUrl = "https://example/plugin.json",
    )

    @Test
    fun manifestRoundTripsThroughItsOwnJson() {
        val text = Json.encodeToString(kotlinx.serialization.json.JsonElement.serializer(), fullManifest.toJson())
        val back = PluginManifest.fromJson(text)
        assertEquals(fullManifest, back)
    }

    @Test
    fun unknownKeysAreIgnoredForForwardCompatibility() {
        val text = """{"protocolVersion":2,"id":"x","name":"n","version":"1","futureThing":{"a":1}}"""
        assertEquals("x", PluginManifest.fromJson(text)?.id)
    }

    @Test
    fun malformedJsonIsNotAPlugin() {
        assertNull(PluginManifest.fromJson("{not json"))
        assertNull(PluginManifest.fromJson("[]"))
    }

    @Test
    fun identityAndProtocolGateTheLoad() {
        assertEquals(ManifestProblem.MissingId, fullManifest.copy(id = "  ").validate("android"))
        assertEquals(ManifestProblem.MalformedId, fullManifest.copy(id = "Netease Online").validate("android"))
        val problem = fullManifest.copy(protocolVersion = 1).validate("android")
        assertEquals(ManifestProblem.UnsupportedProtocol(1), problem)
        assertTrue(problem!!.message.contains("1"))
        assertNull(fullManifest.validate("android"))
    }

    /** 市场 `index.json` 早就带 `platforms[]` 了，但只用来画徽标；v2 把它变成真的过滤 */
    @Test
    fun platformListActuallyFiltersNow() {
        assertEquals(
            ManifestProblem.PlatformUnavailable("android"),
            fullManifest.copy(platforms = listOf("windows")).validate("android"),
        )
        assertNull(fullManifest.copy(platforms = listOf("windows")).validate("windows"))
        assertNull("作者没声明就按可移植处理", fullManifest.copy(platforms = emptyList()).validate("android"))
    }

    @Test
    fun unknownCapabilitiesAreIgnoredNotFatal() {
        val m = fullManifest.copy(capabilities = listOf("search", "karaoke_hd"))
        assertNull(m.validate("android"))
        assertEquals(setOf("search"), m.effectiveCapabilities())
    }

    @Test
    fun timeoutIsClampedIntoAWorkableBand() {
        assertEquals(1_000, PluginManifest.fromJson("""{"protocolVersion":2,"id":"x","timeoutMs":5}""")?.timeoutMs)
        assertEquals(120_000, PluginManifest.fromJson("""{"protocolVersion":2,"id":"x","timeoutMs":99999999}""")?.timeoutMs)
        assertEquals(PluginManifest.DEFAULT_TIMEOUT_MS, PluginManifest.fromJson("""{"protocolVersion":2,"id":"x"}""")?.timeoutMs)
    }

    @Test
    fun versionComparisonIsNumericNotLexicographic() {
        assertTrue(compareVersions("1.10.0", "1.9.9") > 0)
        assertEquals(0, compareVersions("1.8", "1.8.0"))
        assertEquals(0, compareVersions("1.8.0-rc1", "1.8.0"))
        assertTrue(compareVersions("2.0", "1.99") > 0)
        assertTrue(hostSatisfies("1.8.13", fullManifest))
        assertFalse(hostSatisfies("1.7.9", fullManifest))
        assertTrue("插件没声明下限就一律放行", hostSatisfies("0.1", fullManifest.copy(minHostVersion = "")))
    }

    @Test
    fun requestEnvelopeRoundTrips() {
        val params = buildJsonObject {
            put("keyword", "周杰伦")
            put("page", 1)
            put("page_size", 8)
        }
        val line = PluginProtocol.encodeRequest(7, PluginProtocol.METHOD_SEARCH, params)
        val decoded = PluginProtocol.decodeRequest(line)!!
        assertEquals(PluginProtocol.VERSION, decoded.protocol)
        assertEquals(7, decoded.id)
        assertEquals(PluginProtocol.METHOD_SEARCH, decoded.method)
        assertEquals("周杰伦", decoded.params["keyword"]?.jsonPrimitive?.content)
        assertEquals(8, decoded.params["page_size"]?.jsonPrimitive?.content?.toIntOrNull())
    }

    @Test
    fun resultAndErrorShareOneEnvelopeShape() {
        val ok = PluginProtocol.encodeResult(3, buildJsonObject { put("songs", buildJsonArray { }) })
        val okDecoded = PluginProtocol.decodeResponse(ok)!!
        assertEquals(3, okDecoded.id)
        assertFalse(okDecoded.isError)
        assertNotNull(okDecoded.result)

        val bad = PluginProtocol.decodeResponse(PluginProtocol.encodeError(3, PluginProtocol.ERR_PLUGIN, "上游 503", retryable = true))!!
        assertTrue(bad.isError)
        assertEquals(PluginProtocol.ERR_PLUGIN, bad.errorCode)
        assertEquals("上游 503", bad.errorMessage)
        assertTrue(bad.retryable)
    }

    @Test
    fun unparsableOrHeadlessLinesDecodeToNull() {
        assertNull(PluginProtocol.decodeResponse("not json"))
        assertNull(PluginProtocol.decodeResponse("""{"id":1}"""))
        assertNull(PluginProtocol.decodeRequest("""{"params":{}}"""))
    }

    @Test
    fun negotiationIsMajorEquality() {
        assertTrue(PluginProtocol.negotiate(2))
        assertFalse(PluginProtocol.negotiate(1))
        assertFalse("老宿主不认识 v3，宁可拒也别说半话", PluginProtocol.negotiate(3))
    }

    /**
     * v2 把时长**钉死为毫秒**并放进 `duration_ms`。
     * 现状里 `Song.Duration` 注释写毫秒、实际有路径写秒（docs/plugin-development-candidates.md 记过这条），
     * 插件作者只能靠 `>1000` 猜 —— 换行分隔协议里这种歧义必须一次性消掉。
     */
    @Test
    fun songCarriesMillisecondsUnderAnExplicitKey() {
        val song = RpcSong(id = "abc", platform = "netease", title = "稻香", durationMs = 227_000)
        val json = Json.encodeToString(kotlinx.serialization.json.JsonElement.serializer(), song.toJson())
        assertTrue(json.contains("\"duration_ms\":227000"))
        assertEquals(song, RpcSong.fromJson(Json.parseToJsonElement(json)))
    }

    @Test
    fun remoteIdKeepsTheHostsPlatformPrefixedForm() {
        val parsed = RpcSong.fromJson(
            Json.parseToJsonElement(
                """{"id":"abc","platform":"netease","title":"稻香","extra":{"file_hash":"h1"}}""",
            ),
        )!!
        assertEquals("netease:abc", parsed.remoteId)
        assertEquals(mapOf("file_hash" to "h1"), parsed.extra)
    }

    @Test
    fun songWithoutIdIsDroppedInsteadOfBecomingAnEmptyRow() {
        assertNull(RpcSong.fromJson(Json.parseToJsonElement("""{"platform":"netease","title":"x"}""")))
        assertEquals(
            1,
            RpcSong.listFrom(Json.parseToJsonElement("""[{"platform":"p"},{"id":"ok","platform":"p"}]""").jsonArray).size,
        )
    }

    @Test
    fun methodNamesAreStablePublicContract() {
        val methods = listOf(
            PluginProtocol.METHOD_INITIALIZE, PluginProtocol.METHOD_SHUTDOWN, PluginProtocol.METHOD_SEARCH,
            PluginProtocol.METHOD_GET_PLAY_URL, PluginProtocol.METHOD_GET_LYRICS,
            PluginProtocol.METHOD_GET_PLAYLISTS, PluginProtocol.METHOD_GET_PLAYLIST_SONGS,
        )
        assertEquals(methods.size, methods.toSet().size)
        assertTrue(methods.all { it.matches(Regex("[a-z_]+")) })
    }

    @Test
    fun paramsMissingYieldsEmptyObjectRatherThanCrash() {
        val decoded = PluginProtocol.decodeRequest("""{"protocol":2,"id":1,"method":"shutdown"}""")!!
        assertEquals(JsonObject(emptyMap()), decoded.params)
    }
}
