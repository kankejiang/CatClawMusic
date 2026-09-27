package com.catclaw.music.kt.core.ai

import com.catclaw.music.kt.core.model.Song
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 未配置模型时的本地指令，对齐 C# `AgentService.TryLocalCommandAsync`。
 * 重点是那几条「看起来一样、其实会差一格」的分支：
 * "播放" 到底是继续放还是去搜索、音量方向是在改之前判还是之后判。
 */
class LocalCommandTest {

    private class RecordingTool(
        override val name: String,
        var reply: String = """{"success":true,"message":"ok"}""",
        private val onCall: (String) -> Unit = {},
    ) : AgentTool {
        override val description = "t"
        override val properties = emptyMap<String, ToolParamSchema>()
        override val required = emptyList<String>()
        override val isReadOnly = false
        val calls = mutableListOf<String>()

        override suspend fun execute(argumentsJson: String): String {
            calls += argumentsJson
            onCall(argumentsJson)
            return reply
        }
    }

    private fun song(id: Int, title: String) =
        Song(id = id, title = title, filePath = "/m/$id.mp3")

    private class Harness(
        control: RecordingTool? = null,
        current: RecordingTool? = null,
        private val songs: List<Song> = emptyList(),
        initialVolume: Int = 50,
    ) {
        var volume = initialVolume
        val controlTool = control ?: RecordingTool("control_playback") { volume = readVolume(it) ?: volume }
        val currentTool = current ?: RecordingTool("get_current_song", """{"success":true,"message":"「晴天」- 周杰伦"}""")
        val searched = mutableListOf<String>()

        private fun readVolume(args: String): Int? =
            args.substringAfter("\"volume\":", "").substringBefore("}").toIntOrNull()

        fun service(): AgentService {
            val tools = listOfNotNull(controlTool, currentTool)
            return AgentService(
                chat = { _, _, _ -> error("本地指令不该打模型") },
                tools = tools,
                systemPrompt = { "s" },
                searchSongs = { keyword ->
                    searched += keyword
                    songs
                },
                volumePercent = { volume },
            )
        }
    }

    @Test
    fun pauseHitsControlToolAndEchoesItsMessage() = runTest {
        val control = RecordingTool("control_playback", """{"success":true,"message":"已暂停播放"}""")
        val h = Harness(control = control)
        val reply = h.service().tryLocalCommand("暂停一下")
        assertEquals("""{"action":"pause"}""", control.calls.single())
        assertEquals("已暂停播放", reply?.content)
    }

    @Test
    fun barePlayWordResumesInsteadOfSearching() = runTest {
        val control = RecordingTool("control_playback", """{"success":true,"message":"已恢复播放"}""")
        val h = Harness(control = control, songs = listOf(song(1, "稻香")))
        val reply = h.service().tryLocalCommand("播放")
        assertEquals("""{"action":"resume"}""", control.calls.single())
        assertTrue("搜索分支不该被走到", h.searched.isEmpty())
        assertEquals("已恢复播放", reply?.content)
    }

    @Test
    fun nextPreviousStopMapToTheirActions() = runTest {
        val control = RecordingTool("control_playback")
        val h = Harness(control = control)
        val service = h.service()
        service.tryLocalCommand("切歌")
        service.tryLocalCommand("上一曲")
        service.tryLocalCommand("停止播放")
        assertEquals(
            listOf("""{"action":"next"}""", """{"action":"previous"}""", """{"action":"stop"}"""),
            control.calls,
        )
    }

    /**
     * 回归：音量方向必须在**执行设置之前**用旧值判断。
     * 假工具会把音量真改掉，若实现里在 execute 之后才读 volumePercent()，
     * "小声点" 会被判成 "调大"。
     */
    @Test
    fun quieterUsesVolumeBeforeTheChange() = runTest {
        // 用 Harness 的默认可变音量工具：假工具会真的把音量改掉，才能复现"改完之后才判断方向"这个坑
        val h = Harness(initialVolume = 50)
        val reply = h.service().tryLocalCommand("小声一点")
        assertEquals("""{"volume":40}""", h.controlTool.calls.single())
        assertEquals("音量已调小到 40", reply?.content)
        assertEquals(40, h.volume)
    }

    @Test
    fun louderRaisesByTenPoints() = runTest {
        val h = Harness(initialVolume = 50)
        val reply = h.service().tryLocalCommand("声音大一点")
        assertEquals("音量已调大到 60", reply?.content)
        assertEquals(60, h.volume)
    }

    @Test
    fun muteGoesToZeroAndSaysMuted() = runTest {
        val h = Harness(initialVolume = 30)
        val reply = h.service().tryLocalCommand("静音")
        assertEquals("""{"volume":0}""", h.controlTool.calls.single())
        assertEquals("已静音", reply?.content)
    }

    @Test
    fun currentSongKeywordsUseReadOnlyTool() = runTest {
        val current = RecordingTool("get_current_song", """{"success":true,"message":"「稻香」- 周杰伦","song_id":7}""")
        val h = Harness(current = current)
        val reply = h.service().tryLocalCommand("现在在放什么")
        assertEquals(listOf("{}"), current.calls)
        assertEquals("「稻香」- 周杰伦", reply?.content)
    }

    @Test
    fun searchKeywordDropsQualifiersAndCapsCardsAtTen() = runTest {
        val songs = (1..12).map { song(it, "歌$it") }
        val h = Harness(songs = songs)
        val reply = h.service().tryLocalCommand("帮我找一下周杰伦的歌曲")
        // 关键词是"动词之后的内容减限定词"，C# 的词表里没有"的"，所以留着 —— 这里照抄而非顺手清理
        assertEquals(listOf("周杰伦的"), h.searched)
        assertEquals(10, reply?.songs?.size)
        assertTrue(reply!!.content.contains("找到 12 首"))
        assertTrue(reply.content.contains("还有 2 首未显示"))
    }

    @Test
    fun missReturnsNullSoUpperLayerCanFallBack() = runTest {
        val h = Harness()
        assertNull(h.service().tryLocalCommand("今天天气不错"))
        assertNull(h.service().tryLocalCommand("   "))
    }
}
