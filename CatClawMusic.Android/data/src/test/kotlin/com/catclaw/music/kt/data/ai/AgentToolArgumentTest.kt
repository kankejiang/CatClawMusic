package com.catclaw.music.kt.data.ai

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** 工具的参数解析与结果 JSON —— 模型写出的参数形状五花八门，这里钉住容错。 */
class AgentToolArgumentTest {

    private fun obj(json: String) = Json.parseToJsonElement(json) as JsonObject

    @Test
    fun intArgAcceptsNumberAndQuotedStringAndAliases() {
        assertEquals(41, argInt("""{"song_id":41}""", "song_id"))
        // 模型常把数字写成字符串
        assertEquals(41, argInt("""{"song_id":"41"}""", "song_id"))
        // camelCase 别名也要吃到
        assertEquals(7, argInt("""{"songId":7}""", "song_id", "songId"))
        assertEquals(0, argInt("""{"other":7}""", "song_id"))
        assertEquals(0, argInt("not json", "song_id"))
        assertEquals(0, argInt("", "song_id"))
    }

    @Test
    fun stringArgIgnoresBlankAndNonStringValues() {
        assertEquals("夜曲", argString("""{"keyword":"夜曲"}""", "keyword"))
        assertEquals("夜曲", argString("""{"query":"夜曲"}""", "keyword", "query"))
        assertEquals("", argString("""{"keyword":""}""", "keyword"))
        assertEquals("", argString("""{"keyword":123}""", "keyword"))
        assertEquals("", argString("""{"keyword":null}""", "keyword"))
    }

    @Test
    fun resultsCarryTheKeysTheModelReads() {
        val ok = obj(resultJson("success" to true, "message" to "正在播放", "songs" to listOf("1|A|x", "2|B|y")))
        assertTrue(ok["success"]!!.jsonPrimitive.booleanOrNull == true)
        assertEquals("正在播放", ok["message"]!!.jsonPrimitive.content)
        assertEquals(2, obj(resultJson("songs" to listOf("a", "b")))["songs"]!!.toString().count { it == '"' } - 2)

        val err = obj(resultJson("error" to "未找到该歌曲"))
        assertEquals("未找到该歌曲", err["error"]!!.jsonPrimitive.content)
    }

    @Test
    fun songRowIsPipeDelimitedWithIdFirst() {
        val row = com.catclaw.music.kt.data.db.SongListItem(
            id = 9, title = "夜曲", artist = "周杰伦", album = "十一月的萧邦",
            durationSeconds = 253, filePath = "/m/9.mp3", coverArtPath = null,
            mediaStoreId = 0, source = 0, protocol = 0,
        ).asSongRow()
        assertEquals("9|夜曲|周杰伦|十一月的萧邦", row)
    }
}
