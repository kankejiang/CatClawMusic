package com.catclaw.music.kt.core.ai

import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** 配置诊断与档位推导 —— `update_music_source` 的"验证不过就绝不保存"全靠这两块。 */
class SourceValidationTest {

    private class MemStorage(var text: String? = null) : SourceStorage {
        override fun read(): String? = text
        override fun write(value: String) { text = value }
    }

    /**
     * 按 URL 前缀路由的假 HTTP：让搜索/取链/Range 探测各自返回不同内容。
     * `searchCharset` 由调用方按**该源声明的字符集**给 —— 酷我是 GBK、网易云是 UTF-8；
     * 这个测试第一次跑就因为它把网易云的响应编成了 GBK，注册表按 UTF-8 解出乱码，
     * 反过来证明字符集确实是跟着 `spec.encoding` 走的而不是写死的。
     */
    private class RoutingHttp(
        val search: String?,
        val link: String?,
        val probe: ByteArray? = ByteArray(16),
        val searchCharset: java.nio.charset.Charset = charset("GBK"),
    ) : SourceHttp {
        val urls = mutableListOf<String>()

        override suspend fun get(url: String, headers: Map<String, String>, body: ByteArray): ByteArray? {
            urls += url
            return when {
                url.contains("r.s") || url.contains("search") -> search?.toByteArray(searchCharset)
                url.startsWith("http") && headers.containsKey("Range") -> probe
                else -> link?.toByteArray()
            }
        }

        override suspend fun postForm(url: String, form: Map<String, String>, headers: Map<String, String>): ByteArray? {
            urls += url
            return link?.toByteArray()
        }
    }

    private val kuwo = MusicSourceRegistry(MemStorage(), RoutingHttp(null, null)).defaults().first { it.id == "kuwo" }
    private val netease = MusicSourceRegistry(MemStorage(), RoutingHttp(null, null)).defaults().first { it.id == "netease" }

    private val kuwoTwoSongs = "junk{'AARTIST':'a','SONGNAME':'七里香','ARTIST':'周杰伦','MUSICRID':'MUSIC_1','FORMATS':'MP3,MP3H,FLAC'}" +
        "{'AARTIST':'b','SONGNAME':'另一首','ARTIST':'某人','MUSICRID':'MUSIC_2','FORMATS':'MP3,128'}"

    @Test
    fun bestQualityFollowsTheFormatsField() {
        assertEquals("flac", SourceSong("1", formats = "MP3,FLAC,NEW").bestQuality)
        assertEquals("flac", SourceSong("2", formats = "ape").bestQuality)
        assertEquals("320k", SourceSong("3", formats = "MP3,MP3H").bestQuality)
        assertEquals("320k", SourceSong("4", formats = "MP3,320").bestQuality)
        assertEquals("128k", SourceSong("5").bestQuality)
        assertEquals("128k", SourceSong("6", formats = "").bestQuality)
    }

    @Test
    fun fullPassReportsCountsPreviewAndProbe() = runTest {
        val http = RoutingHttp(
            search = kuwoTwoSongs,
            link = """{"data":{"surl":"https://t1.kuwo.cn/七里香.flac?sign=abc"}}""",
        )
        val diag = MusicSourceRegistry(MemStorage(), http).validate(kuwo)
        assertTrue(diag.ok)
        assertTrue(diag.searchOk && diag.urlOk && diag.httpOk)
        assertEquals(2, diag.songs)
        assertEquals("七里香-周杰伦", diag.firstResult)
        // 探针曲必须是"七里香"，档位取该曲 FORMATS 推出来的 flac（urls 顺序：搜索 → 取链 → Range 探测）
        assertTrue(http.urls[1].contains("br=2000kflac"))
    }

    @Test
    fun emptySearchStopsBeforeAnyLinkRequest() = runTest {
        val http = RoutingHttp(search = "nothing here", link = """{"data":{"surl":"https://x/y"}}""")
        val diag = MusicSourceRegistry(MemStorage(), http).validate(kuwo)
        assertFalse(diag.ok)
        assertFalse(diag.searchOk)
        assertEquals(0, diag.songs)
        assertTrue(diag.error.contains("搜索无结果"))
        assertEquals("搜索失败后不该再取链", 1, http.urls.size)
    }

    @Test
    fun linkFailureIsReportedSeparatelyFromSearchFailure() = runTest {
        val http = RoutingHttp(search = kuwoTwoSongs, link = """{"data":{"surl":""}}""")
        val diag = MusicSourceRegistry(MemStorage(), http).validate(kuwo)
        assertFalse(diag.ok)
        assertTrue("搜索这一步是好的", diag.searchOk)
        assertFalse(diag.urlOk)
        assertTrue(diag.error.contains("取链失败"))
    }

    /** 直链探测挂了不否掉整份配置：C# 的 early return 只在前两步 */
    @Test
    fun unreadableDirectLinkStillCountsAsValidConfig() = runTest {
        val http = RoutingHttp(
            search = kuwoTwoSongs,
            link = """{"data":{"surl":"https://t1.kuwo.cn/a.flac"}}""",
            probe = null,
        )
        val diag = MusicSourceRegistry(MemStorage(), http).validate(kuwo)
        assertTrue(diag.ok)
        assertFalse(diag.httpOk)
    }

    @Test
    fun unknownTemplateYieldsEmptyDiagnosisRatherThanCrash() = runTest {
        val diag = MusicSourceRegistry(MemStorage(), RoutingHttp(kuwoTwoSongs, null)).validate(kuwo.copy(template = "nope"))
        assertFalse(diag.ok)
        assertTrue(diag.error.contains("搜索无结果"))
    }

    @Test
    fun neteaseProbeUsesTheEapiPostPath() = runTest {
        val http = RoutingHttp(
            search = """{"result":{"songs":[{"id":42,"name":"七里香","artists":[{"name":"周杰伦"}]}]}}""",
            link = """{"data":[{"url":"https://m1.163.cn/verylongenoughurl.mp3"}]}""",
            searchCharset = Charsets.UTF_8,
        )
        val diag = MusicSourceRegistry(MemStorage(), http).validate(netease)
        assertTrue(diag.ok)
        assertEquals("七里香-周杰伦", diag.firstResult)
        assertEquals("https://interface3.music.163.com/eapi/song/enhance/player/url/v1", http.urls[1])
    }

    @Test
    fun urlPreviewIsCappedAtTheContractedLength() = runTest {
        val long = "https://t1.kuwo.cn/" + "x".repeat(200)
        val http = RoutingHttp(search = kuwoTwoSongs, link = """{"data":{"surl":"$long"}}""")
        val diag = MusicSourceRegistry(MemStorage(), http).validate(kuwo)
        assertEquals(113, diag.urlPreview.length)
        assertTrue(diag.urlPreview.endsWith("..."))
    }

    @Test
    fun singleConfigJsonDecodesTheModelFields() {
        val cfg = MusicSourceRegistry.decodeOne(
            """{"id":"x","name":"自定义","template":"kuwo_jsonp","search":{"url":"u","params":{"a":"{keyword}"}},""" +
                """"url_api":{},"quality_map":{},"regexes":{"id_pattern":"'MUSICRID':\\s*'([^']*)'"}}""",
        )!!
        assertEquals("x", cfg.id)
        assertEquals("kuwo_jsonp", cfg.template)
        assertEquals("u", cfg.search.url)
        assertEquals(mapOf("a" to "{keyword}"), cfg.search.params)
        assertEquals("'MUSICRID':\\s*'([^']*)'", cfg.regexes.idPattern)
        assertTrue("缺 enabled 时按启用处理", cfg.enabled)
        assertNull(MusicSourceRegistry.decodeOne("[1,2]"))
    }

    private fun assertNull(value: Any?) = org.junit.Assert.assertNull(value)
}
