package com.catclaw.music.kt.core.plugin

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 适配器的行为：**主源码在此之前从来没调用过任何插件**，所以这里测的是那条新接上的线。
 *
 * 假的是 [PluginChannel]（报文层），不是传输 —— 也就是说信封、方法名、参数形状都真的走了一遍。
 */
class PluginMusicSourceTest {

    private class FakeChannel(
        private val reply: (String, JsonObject) -> RpcResponse,
    ) : PluginChannel {
        val calls = mutableListOf<Pair<String, JsonObject>>()
        var closed = false
        override suspend fun request(method: String, params: JsonElement): RpcResponse {
            val obj = params.jsonObject
            calls += method to obj
            return reply(method, obj)
        }
        override suspend fun close() { closed = true }
    }

    private fun ok(result: String) = RpcResponse(1, Json.parseToJsonElement(result), null, "", false)
    private fun err(message: String) = RpcResponse(1, null, PluginProtocol.ERR_PLUGIN, message, false)

    private fun manifest(vararg capabilities: String) = PluginManifest(
        protocolVersion = PluginProtocol.VERSION,
        id = "demo/netease",
        name = "netease",
        version = "1.0",
        capabilities = capabilities.toList(),
    )

    private val song = RpcSong(id = "1", platform = "netease", title = "歌")

    @Test
    fun `初始化协商协议版本，不匹配就当不可用`() = runTest {
        val good = FakeChannel { _, _ -> ok("{\"protocol\":2}") }
        assertTrue(PluginMusicSource(manifest(), good, "android", "2.0").initialize())
        assertEquals(PluginProtocol.METHOD_INITIALIZE, good.calls[0].first)
        assertEquals("android", good.calls[0].second["platform"]!!.jsonPrimitive.content)

        val primitives = FakeChannel { _, _ -> ok("2") }
        assertTrue("直接回数字也要认", PluginMusicSource(manifest(), primitives, "android", "2.0").initialize())

        val old = FakeChannel { _, _ -> ok("{\"protocol\":1}") }
        assertFalse("版本不匹配必须拒绝，不能带着未知语义硬调", PluginMusicSource(manifest(), old, "android", "2.0").initialize())

        val broken = FakeChannel { _, _ -> err("boom") }
        assertFalse(PluginMusicSource(manifest(), broken, "android", "2.0").initialize())
    }

    @Test
    fun `搜索把页码与页大小按契约发出去，结果解成 RpcSong`() = runTest {
        val channel = FakeChannel { _, _ ->
            ok("""[{"id":"1","platform":"netease","title":"歌","artist":"手","duration_ms":1000}]""")
        }
        val source = PluginMusicSource(manifest("search"), channel, "android", "2.0")
        val hits = source.search("周杰伦")
        assertEquals(1, hits.size)
        assertEquals("netease:1", hits[0].remoteId)
        val (method, params) = channel.calls[0]
        assertEquals(PluginProtocol.METHOD_SEARCH, method)
        assertEquals("周杰伦", params["keyword"]!!.jsonPrimitive.content)
        assertEquals("页码从 1 开始", 1, params["page"]!!.jsonPrimitive.content.toInt())
    }

    @Test
    fun `搜索失败返回空列表而不是抛异常`() = runTest {
        val channel = FakeChannel { _, _ -> err("限流") }
        val source = PluginMusicSource(manifest("search"), channel, "android", "2.0")
        assertEquals(emptyList<RpcSong>(), source.search("x"))
    }

    @Test
    fun `取流与歌词按契约解析`() = runTest {
        val channel = FakeChannel { method, _ ->
            when (method) {
                PluginProtocol.METHOD_GET_PLAY_URL -> ok("\"https://cdn.test/a.mp3\"")
                PluginProtocol.METHOD_GET_LYRICS -> ok("""{"lrc":"[00:01]原文","tlrc":"[00:01]译文","rlrc":"[00:01]kana"}""")
                else -> err("unexpected")
            }
        }
        val source = PluginMusicSource(manifest("play", "lyrics"), channel, "android", "2.0")
        assertEquals("https://cdn.test/a.mp3", source.playUrl(song))
        val streams = source.lyrics(song)!!
        assertEquals("[00:01]原文", streams.lrc)
        assertEquals("[00:01]译文", streams.translation)
        assertEquals("[00:01]kana", streams.roma)
        assertEquals("12345 那类平台 id 要带出去", "1", channel.calls[1].second["id"]!!.jsonPrimitive.content)
    }

    @Test
    fun `取流失败返回 null`() = runTest {
        val channel = FakeChannel { _, _ -> err("没有版权") }
        val source = PluginMusicSource(manifest("play"), channel, "android", "2.0")
        assertNull(source.playUrl(song))
    }

    @Test
    fun `清单写了的能力才调，没写的不调`() = runTest {
        val channel = FakeChannel { _, _ -> ok("\"https://a/b.mp3\"") }
        val lyricsOnly = PluginMusicSource(manifest("lyrics"), channel, "android", "2.0")
        assertEquals(emptyList<RpcSong>(), lyricsOnly.search("x"))
        assertNull("清单没写 play，就不该去取流", lyricsOnly.playUrl(song))
        assertEquals("连请求都不该发出去", 0, channel.calls.size)
    }

    @Test
    fun `清单不写能力时不限制`() = runTest {
        val channel = FakeChannel { _, _ -> ok("\"https://a/b.mp3\"") }
        val source = PluginMusicSource(manifest(), channel, "android", "2.0")
        assertTrue(source.supports("search"))
        assertEquals("https://a/b.mp3", source.playUrl(song))
        assertEquals(1, channel.calls.size)
    }

    @Test
    fun `歌单两条方法带 playlist_id 与页码`() = runTest {
        val channel = FakeChannel { method, _ ->
            when (method) {
                PluginProtocol.METHOD_GET_PLAYLISTS -> ok("""[{"id":"p1","name":"我的"}]""")
                else -> ok("""[{"id":"9","platform":"netease","title":"歌"}]""")
            }
        }
        val source = PluginMusicSource(manifest("playlist"), channel, "android", "2.0")
        assertEquals(1, source.playlists("流行").size)
        assertEquals("流行", channel.calls[0].second["category"]!!.jsonPrimitive.content)
        assertEquals(1, source.playlistSongs("p1").size)
        assertEquals("p1", channel.calls[1].second["playlist_id"]!!.jsonPrimitive.content)
        assertEquals(50, channel.calls[1].second["page_size"]!!.jsonPrimitive.content.toInt())
    }

    @Test
    fun `关闭会关掉通道`() = runTest {
        val channel = FakeChannel { _, _ -> ok("[]") }
        PluginMusicSource(manifest(), channel, "android", "2.0").close()
        assertTrue(channel.closed)
    }
}
