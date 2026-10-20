package com.catclaw.music.kt.core.plugin

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 报文里装什么。这里钉的都是「猜错不会报错、只会静默返回空」的那种约定。
 */
class PluginPayloadsTest {

    private val song = RpcSong(
        id = "12345",
        platform = "netease",
        title = "歌名",
        artist = "歌手",
        durationMs = 251_000,
        extra = mapOf("albumId" to "999"),
    )

    private fun params(text: String) = Json.parseToJsonElement(text).jsonObject

    @Test
    fun `搜索参数页码从 1 开始，且不接受 0`() {
        val p = PluginPayloads.searchParams("周杰伦")
        assertEquals("周杰伦", p["keyword"]!!.jsonPrimitive.content)
        assertEquals(
            "C# 的 SearchAsync 页码从 1 开始；从 0 开始会让第一页永远是空的",
            1,
            p["page"]!!.jsonPrimitive.content.toInt(),
        )
        assertEquals(8, p["page_size"]!!.jsonPrimitive.content.toInt())
        assertEquals("传 0 也要被抬到 1", 1, PluginPayloads.searchParams("x", page = 0)["page"]!!.jsonPrimitive.content.toInt())
    }

    @Test
    fun `取流参数带 remoteId 的三段与 extra`() {
        val p = PluginPayloads.playUrlParams(song, quality = 2)
        assertEquals("12345", p["id"]!!.jsonPrimitive.content)
        assertEquals("netease", p["platform"]!!.jsonPrimitive.content)
        assertEquals(2, p["quality"]!!.jsonPrimitive.content.toInt())
        assertEquals(
            "extra 里常放着平台侧真正的 id，丢了就取不到直链",
            "999",
            p["extra"]!!.jsonObject["albumId"]!!.jsonPrimitive.content,
        )
    }

    @Test
    fun `remoteId 是 platform 冒号 id`() {
        assertEquals("netease:12345", song.remoteId)
    }

    @Test
    fun `取流结果两种形状都认，空白算没取到`() {
        assertEquals("https://a.test/x.mp3", PluginPayloads.playUrlFrom(JsonPrimitive("https://a.test/x.mp3")))
        assertEquals("https://a.test/x.mp3", PluginPayloads.playUrlFrom(params("""{"url":"https://a.test/x.mp3"}""")))
        assertNull("空串不能当 URL 传下去", PluginPayloads.playUrlFrom(JsonPrimitive("   ")))
        assertNull(PluginPayloads.playUrlFrom(JsonPrimitive(1)))
        assertNull(PluginPayloads.playUrlFrom(null))
    }

    @Test
    fun `三流歌词按 lrc tlrc rlrc 解，也认裸字符串`() {
        val three = PluginPayloads.lyricsFrom(
            params(
                """{"lrc":"[00:01.00]原文","tlrc":"[00:01.00]译文","rlrc":"[00:01.00]kana"}""",
            ),
        )!!
        assertEquals("[00:01.00]原文", three.lrc)
        assertEquals("[00:01.00]译文", three.translation)
        assertEquals("[00:01.00]kana", three.roma)
        assertFalse(three.isEmpty)

        val single = PluginPayloads.lyricsFrom(JsonPrimitive("[00:01.00]只有原文"))!!
        assertEquals("[00:01.00]只有原文", single.lrc)
        assertNull("单流时不该凭空造出译文", single.translation)
    }

    @Test
    fun `三流全空等于没有歌词`() {
        assertNull(PluginPayloads.lyricsFrom(params("""{"lrc":"  ","tlrc":""}""")))
        assertNull(PluginPayloads.lyricsFrom(params("{}")))
        assertNull(PluginPayloads.lyricsFrom(null))
    }

    @Test
    fun `歌单结果解析成 id 名称 封面 数量`() {
        val list = PluginPayloads.RpcPlaylist.listFrom(
            Json.parseToJsonElement(
                """[{"id":"p1","name":"我喜欢的","cover_url":"https://c.test/1.jpg","count":42},{"name":"没有 id"}]""",
            ),
        )
        assertEquals("没有 id 的条目要跳过，而不是造一条空的", 1, list.size)
        assertEquals("p1", list[0].id)
        assertEquals(42, list[0].count)
        assertTrue(list[0].toJson().toString().contains("c.test"))
    }
}
