package com.catclaw.music.kt.data.ai

import com.catclaw.music.kt.core.ai.DownloadEnqueuer
import com.catclaw.music.kt.core.ai.MusicSourceRegistry
import com.catclaw.music.kt.core.ai.SourceHttp
import com.catclaw.music.kt.core.ai.SourceStorage
import com.catclaw.music.kt.core.playback.SessionLauncher
import java.lang.reflect.Proxy
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 源下载与 `update_music_source` 的工具层分支。
 * 注册表用真的（假 HTTP + 内存存储），所以"验证不过就不许落盘"这条安全性质是被真跑出来的。
 */
class SourceToolsTest {

    private class MemStorage(var text: String? = null) : SourceStorage {
        override fun read(): String? = text
        override fun write(value: String) { text = value }
    }

    private class FakeHttp(val searchBody: String? = null, val surl: String? = null) : SourceHttp {
        val linkUrls = mutableListOf<String>()
        var searchCalls = 0

        override suspend fun get(url: String, headers: Map<String, String>, body: ByteArray): ByteArray? = when {
            url.contains("search.kuwo.cn/r.s") -> { searchCalls++; searchBody?.toByteArray(charset("GBK")) }
            url.contains("mobi.kuwo.cn/mobi.s") -> {
                linkUrls += url
                """{"data":{"surl":"${surl ?: ""}"}}""".toByteArray()
            }
            else -> null // Range 探测：读不到不影响 ok 判据
        }

        override suspend fun postForm(url: String, form: Map<String, String>, headers: Map<String, String>): ByteArray? = null
    }

    private class FakeEnqueuer : DownloadEnqueuer {
        val calls = mutableListOf<Pair<String, String>>()
        override fun enqueue(url: String, filename: String): String? {
            calls += url to filename
            return "已开始下载：$filename"
        }
    }

    private class Fixture(
        searchBody: String? = null,
        surl: String? = null,
        storedConfig: String? = null,
    ) {
        val http = FakeHttp(searchBody, surl)
        val storage = MemStorage(storedConfig)
        val registry = MusicSourceRegistry(storage, http)
        val enqueuer = FakeEnqueuer()
        val tools = MusicAgentTools(
            player = unused(clsPlayer),
            dao = unused(clsDao),
            session = SessionLauncher { },
            playlists = unused(clsPlaylists),
            fetcher = FakeFetcher(mutableListOf()),
            sources = registry,
            downloads = enqueuer,
        )

        fun tool(name: String) = tools.all.first { it.name == name }
    }

    private fun blocks(vararg entries: Triple<String, String, String>) = entries.joinToString("") { (id, name, formats) ->
        "{'AARTIST':'a','SONGNAME':'$name','ARTIST':'周杰伦','MUSICRID':'MUSIC_$id','FORMATS':'$formats'}"
    }

    /** config_json 是「JSON 里再嵌一段 JSON」，手写转义必错 —— 交给编码器生成 */
    private fun updateArgs(id: String, config: String?): String = buildJsonObject {
        put("id", id)
        if (config != null) put("config_json", config)
    }.toString()

    private val customConfig = buildJsonObject {
        put("id", "kuwo")
        put("name", "自定义")
        put("template", "kuwo_jsonp")
        put(
            "search",
            buildJsonObject {
                put("url", "http://search.kuwo.cn/r.s")
                put("encoding", "gbk")
                put("params", buildJsonObject { put("all", "{keyword}") })
            },
        )
        put(
            "url_api",
            buildJsonObject {
                put("url", "https://mobi.kuwo.cn/mobi.s")
                put("params", buildJsonObject { put("rid", "{id}"); put("br", "{quality}") })
            },
        )
        // 模型必须一并提交解析规则，否则注册表切不出块、验证自然不过 —— 这里同时验 snake_case 解码
        put(
            "regexes",
            buildJsonObject {
                put("block_split", "\\{'AARTIST")
                put("block_marker", "'SONGNAME'")
                put("id_pattern", "'MUSICRID':\\s*'([^']*)'")
                put("name_pattern", "'SONGNAME':\\s*'([^']*)'")
                put("artist_pattern", "'ARTIST':\\s*'([^']*)'")
                put("formats_pattern", "'FORMATS':\\s*'([^']*)'")
            },
        )
    }.toString()

    @Test
    fun kuwoDownloadPicksFlacAndEnqueuesByFilename() = runTest {
        val f = Fixture(
            searchBody = blocks(Triple("11", "七里香", "MP3,FLAC"), Triple("22", "另一首", "MP3,128")),
            surl = "https://t1.kuwo.cn/qixiang.flac",
        )
        val result = f.tool("kuwo_download").execute("""{"keyword":"七里香","artist":"周杰"}""")
        assertTrue(result.contains("FLAC无损"))
        assertTrue(result.contains("匹配到目标歌手版本"))
        assertEquals(listOf("https://t1.kuwo.cn/qixiang.flac" to "七里香-周杰伦.flac"), f.enqueuer.calls)
    }

    @Test
    fun kuwoDownloadValidatesKeywordBeforeAnyRequest() = runTest {
        val f = Fixture(searchBody = blocks(Triple("1", "x", "MP3")))
        assertTrue(f.tool("kuwo_download").execute("""{"keyword":"  "}""").contains("请提供歌曲名称"))
        assertEquals(0, f.http.searchCalls)
    }

    @Test
    fun disabledSourcePointsAtTheRepairTool() = runTest {
        val disabled = MusicSourceRegistry.encodeList(
            listOf(MusicSourceRegistry(MemStorage(), FakeHttp()).defaults().first { it.id == "kuwo" }.copy(enabled = false)),
        )
        val f = Fixture(searchBody = blocks(Triple("1", "x", "MP3")), storedConfig = disabled)
        val result = f.tool("kuwo_download").execute("""{"keyword":"x"}""")
        assertTrue(result.contains("配置无效或已禁用"))
        assertTrue("要把模型引向 self-repair 工具", result.contains("update_music_source"))
        assertEquals(0, f.http.searchCalls)
    }

    @Test
    fun noUsableLinkSaysInterfaceMayHaveChangedAndEnqueuesNothing() = runTest {
        val f = Fixture(searchBody = blocks(Triple("1", "七里香", "MP3,FLAC")), surl = null)
        val result = f.tool("kuwo_download").execute("""{"keyword":"七里香"}""")
        assertTrue(result.contains("未能获取到播放地址"))
        assertTrue(result.contains("接口可能已变更"))
        assertTrue(f.enqueuer.calls.isEmpty())
    }

    @Test
    fun probingIsCappedAtSixCandidates() = runTest {
        val many = (1..9).joinToString("") {
            "{'AARTIST':'a','SONGNAME':'歌$it','ARTIST':'某人','MUSICRID':'MUSIC_$it','FORMATS':'MP3'}"
        }
        val f = Fixture(searchBody = many, surl = "https://t1.kuwo.cn/x.mp3")
        f.tool("kuwo_download").execute("""{"keyword":"歌"}""")
        assertEquals("并行探链上限是 6，不能拿整页去压平台", 6, f.http.linkUrls.size)
        assertEquals(1, f.enqueuer.calls.size)
    }

    @Test
    fun updateToolRejectsMalformedConfigWithoutSaving() = runTest {
        val f = Fixture()
        val args = buildJsonObject { put("id", "kuwo"); put("config_json", "{坏 json") }.toString()
        assertTrue(f.tool("update_music_source").execute(args).contains("不是合法 JSON"))
        assertNull(f.storage.text)
    }

    @Test
    fun updateToolNeverPersistsAnUnverifiedConfig() = runTest {
        val f = Fixture(searchBody = "搜不到东西")
        val result = f.tool("update_music_source").execute(updateArgs("kuwo", customConfig))
        assertTrue("实际返回：$result", result.contains("验证未通过，配置未保存"))
        assertNull("坏配置一旦落盘会把可用的内置源顶掉", f.storage.text)
    }

    @Test
    fun updateToolSavesAndEnablesAfterAPassedProbe() = runTest {
        val f = Fixture(searchBody = blocks(Triple("1", "七里香", "MP3,FLAC")), surl = "https://t1.kuwo.cn/a.flac")
        val result = f.tool("update_music_source").execute(updateArgs("kuwo", customConfig))
        assertTrue("实际返回：$result", result.contains("验证通过并已保存"))
        val saved = f.storage.text!!
        assertTrue(saved.contains("自定义"))
        assertTrue("落盘必须带上 enabled", saved.contains("\"enabled\":true"))
        assertEquals("自定义", f.registry.get("kuwo")?.name)
    }

    @Test
    fun updateToolWithoutConfigOnlyDiagnoses() = runTest {
        val stored = MusicSourceRegistry.encodeList(MusicSourceRegistry(MemStorage(), FakeHttp()).defaults())
        val f = Fixture(
            searchBody = blocks(Triple("1", "七里香", "MP3,FLAC")),
            surl = "https://t1.kuwo.cn/a.flac",
            storedConfig = stored,
        )
        val result = f.tool("update_music_source").execute(updateArgs("kuwo", null))
        assertTrue("实际返回：$result", result.contains("现有配置可用"))
        assertEquals("只诊断不许改存储", stored, f.storage.text)
    }

    @Test
    fun downloadFileInfersTheNameFromTheLinkTail() = runTest {
        val f = Fixture()
        val result = f.tool("download_file").execute("""{"url":"https://cdn.x/a/专辑封面.png?v=2#top"}""")
        assertTrue(result.contains("专辑封面.png"))
        assertEquals(listOf("https://cdn.x/a/专辑封面.png?v=2#top" to "专辑封面.png"), f.enqueuer.calls)
    }

    @Test
    fun downloadFilePrefersAnExplicitNameAndRejectsNonHttpLinks() = runTest {
        val f = Fixture()
        f.tool("download_file").execute("""{"url":"https://cdn.x/a.bin","filename":"安装包.apk"}""")
        assertEquals("安装包.apk", f.enqueuer.calls.single().second)

        assertTrue(f.tool("download_file").execute("""{"url":"ftp://cdn.x/a.bin"}""").contains("只支持 http/https"))
        assertTrue(f.tool("download_file").execute("""{"url":""}""").contains("请提供下载链接"))
        assertEquals("被拒的链接不该入队", 1, f.enqueuer.calls.size)
    }

    @Test
    fun downloadFileIsAWriteToolAndNeedsAUrl() {
        val f = Fixture()
        val tool = f.tool("download_file")
        assertEquals(listOf("url"), tool.required)
        assertFalse("入队会改下载队列状态，不能标成只读", tool.isReadOnly)
    }

    @Test
    fun downloadToolsKeepTheCSharpContract() {
        val f = Fixture()
        val kuwo = f.tool("kuwo_download")
        assertEquals(listOf("keyword"), kuwo.required)
        assertTrue(kuwo.isReadOnly)
        assertTrue(kuwo.description.contains("无损"))
        val netease = f.tool("netease_download")
        assertEquals("netease_download", netease.name)
        assertFalse(netease.description.contains("FLAC无损"))
    }
}

/** 只被用到的方法之外的调用 = 测试写错了，直接抛 */
private val clsPlayer = com.catclaw.music.kt.core.playback.PlayerApi::class.java
private val clsDao = com.catclaw.music.kt.data.db.LibraryDao::class.java
private val clsPlaylists = com.catclaw.music.kt.data.db.PlaylistDao::class.java

private fun <T> unused(clazz: Class<T>): T = Proxy.newProxyInstance(
    clazz.classLoader, arrayOf(clazz),
) { _, method, _ -> throw UnsupportedOperationException(method.name) } as T
