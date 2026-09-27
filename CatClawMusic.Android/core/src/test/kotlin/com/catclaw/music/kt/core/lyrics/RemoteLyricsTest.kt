package com.catclaw.music.kt.core.lyrics

import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * 远程侧车歌词这条链。假件只给字节，所以测的是"取回之后怎么办"，
 * 而"能不能取回"由 `OkHttpBytesFetcher` 那条真 HTTP 服务器用例负责。
 */
class RemoteLyricsTest {

    private class Fetcher(private val body: ByteArray? = null, private val boom: Boolean = false) :
        RemoteBytesFetcher {
        var calls = 0
        var lastUrl: String? = null
        override suspend fun fetch(url: String): ByteArray? {
            calls++
            lastUrl = url
            if (boom) throw IllegalStateException("connect refused")
            return body
        }
    }

    private val lrc = "[00:12.30]第一行\n[00:20.00]第二行\n"

    @Test
    fun localOrMissingPathsNeverTouchTheNetwork() = runTest {
        for (path in listOf(null, "", "   ", "/storage/emulated/0/Music/a.lrc", "content://media/2/lrc")) {
            val fetcher = Fetcher(lrc.toByteArray())
            assertNull(RemoteLyrics(fetcher).load(path))
            assertEquals("侧车是本地路径时不该发请求：$path", 0, fetcher.calls)
        }
    }

    @Test
    fun bothHttpSchemesAreAcceptedRegardlessOfCase() = runTest {
        for (url in listOf("http://h/a.lrc", "https://h/a.lrc", "HTTPS://H/a.lrc")) {
            val fetcher = Fetcher(lrc.toByteArray())
            assertEquals(2, RemoteLyrics(fetcher).load(url)?.lines?.size)
            assertEquals(url, fetcher.lastUrl)
        }
    }

    @Test
    fun timestampsSurviveTheRoundTrip() = runTest {
        val lyrics = RemoteLyrics(Fetcher(lrc.toByteArray())).load("https://h/a.lrc")!!
        assertEquals(listOf("第一行", "第二行"), lyrics.lines.map { it.text })
        assertEquals(listOf(12_300L, 20_000L), lyrics.lines.map { it.startMs })
    }

    @Test
    fun encodingFollowsTheBytesNotAHardCodedCharset() = runTest {
        // 把 LRC 编成 GBK：如果解码写死 UTF-8，这里就会拿到乱码而不是这两行
        val gbk = lrc.toByteArray(Charsets.UTF_8).let { utf8 ->
            String(utf8, Charsets.UTF_8).toByteArray(charset("GBK"))
        }
        val lyrics = RemoteLyrics(Fetcher(gbk)).load("https://h/a.lrc")
        assertEquals(listOf("第一行", "第二行"), lyrics?.lines?.map { it.text })
    }

    @Test
    fun ttmlContentIsRoutedToTheTtmlParser() = runTest {
        val ttml = """
            <?xml version="1.0" encoding="utf-8"?>
            <tt xmlns="http://www.w3.org/ns/ttml">
              <body><div><p begin="00:00:12.300" end="00:00:15.000">副歌</p></div></body>
            </tt>
        """.trimIndent()
        val lyrics = RemoteLyrics(Fetcher(ttml.toByteArray())).load("https://h/a.ttml")
        assertEquals(listOf("副歌"), lyrics?.lines?.map { it.text })
    }

    @Test
    fun everyFailureShapeComesBackAsNullNotAnException() = runTest {
        assertNull("对端 404 / 拿不到", RemoteLyrics(Fetcher(null)).load("https://h/a.lrc"))
        assertNull("连接抛异常", RemoteLyrics(Fetcher(boom = true)).load("https://h/a.lrc"))
        assertNull("空响应", RemoteLyrics(Fetcher(ByteArray(0))).load("https://h/a.lrc"))
        // 走 HTTP 最常见的两种"不是歌词"：错误页与 JSON 错误响应 —— 标记防御要挡住
        assertNull(RemoteLyrics(Fetcher("<html><body>404 Not Found</body></html>".toByteArray())).load("https://h/a.lrc"))
        assertNull(RemoteLyrics(Fetcher("""{"code":403,"message":"forbidden"}""".toByteArray())).load("https://h/a.lrc"))
        // 控制字符占大半的字节流也要挡住
        assertNull(RemoteLyrics(Fetcher(byteArrayOf(1, 2, 3, 4, 5, 6, 7, 8))).load("https://h/a.lrc"))
    }

    /**
     * 已知边界，**并且是 C# 的既有行为**，不是本端漏掉：
     * 一小坨二进制被编码回退链解成几个"非控制字符"的生僻码点后，两边都会把它当一行纯文本歌词。
     * 这条断言把它钉成"明确知道的最小暴露面"，而不是假装挡住 —— 想收紧得连 C# 一起改。
     */
    @Test
    fun shortBinaryBlobBecomesOneTextLineJustLikeCSharpDoes() = runTest {
        val lyrics = RemoteLyrics(Fetcher(byteArrayOf(0, 1, 2, 3, -1, -2, 0, 9))).load("https://h/a.lrc")
        assertEquals(1, lyrics?.lines?.size)
    }

    @Test
    fun oversizedBodyIsRefusedBeforeDecoding() = runTest {
        val huge = ByteArray(LyricsEncoding.MAX_LYRICS_FILE_BYTES + 10) { 'x'.code.toByte() }
        assertNull(RemoteLyrics(Fetcher(huge)).load("https://h/a.lrc"))
    }

    @Test
    fun aPlainTextSidecarStillYieldsLines() = runTest {
        // 没有时间戳的 .txt/.lrc：C# 与本端都按纯文本兜底，不该因为"没有方括号"就整条判死
        // 这一档此前本端没有：C# `LyricsService.Lrc.cs:494-496` 是「先试 LRC，再试纯文本」，
        // 本端 `parseContent` 到 `LrcParser.parse` 就结束了。现已按 C# 补齐（`PlainTextLyrics.kt`）。
        val lyrics = RemoteLyrics(Fetcher("没有时间的歌词\n第二行".toByteArray())).load("https://h/a.lrc")
        assertEquals(listOf("没有时间的歌词", "第二行"), lyrics?.lines?.map { it.text })
        assertEquals("合成时间轴：每行 5 秒", listOf(0L, 5_000L), lyrics?.lines?.map { it.startMs })
    }
}
