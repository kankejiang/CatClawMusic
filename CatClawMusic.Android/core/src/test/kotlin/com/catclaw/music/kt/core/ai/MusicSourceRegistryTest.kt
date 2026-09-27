package com.catclaw.music.kt.core.ai

import java.io.File
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** 音乐源注册表：配置存储/回落、切块解析、GBK、eapi 加密链路。全程假 HTTP，不打网络。 */
class MusicSourceRegistryTest {

    private class MemStorage(var text: String? = null) : SourceStorage {
        override fun read(): String? = text
        override fun write(value: String) { text = value }
    }

    private class FakeHttp(
        val responses: MutableMap<String, ByteArray> = mutableMapOf(),
        val postResponse: ByteArray? = null,
    ) : SourceHttp {
        val gets = mutableListOf<String>()
        val getHeaders = mutableListOf<Map<String, String>>()
        var lastForm: Map<String, String> = emptyMap()
        var lastPostHeaders: Map<String, String> = emptyMap()

        override suspend fun get(url: String, headers: Map<String, String>, body: ByteArray): ByteArray? {
            gets += url
            getHeaders += headers
            val key = responses.keys.firstOrNull { url.startsWith(it) } ?: return null
            return responses[key]
        }

        override suspend fun postForm(url: String, form: Map<String, String>, headers: Map<String, String>): ByteArray? {
            lastForm = form
            lastPostHeaders = headers
            return postResponse
        }
    }

    private fun registry(http: SourceHttp, storage: SourceStorage = MemStorage()) = MusicSourceRegistry(storage, http)

    private val kuwo get() = MusicSourceRegistry(MemStorage(), FakeHttp()).defaults().first { it.id == "kuwo" }
    private val netease get() = MusicSourceRegistry(MemStorage(), FakeHttp()).defaults().first { it.id == "netease" }

    @Test
    fun builtInSourcesCarryTheConfigurableProtocol() {
        val defaults = registry(FakeHttp()).defaults()
        assertEquals(listOf("kuwo", "netease"), defaults.map { it.id })
        assertEquals(listOf("kuwo_jsonp", "netease_eapi"), defaults.map { it.template })
        assertEquals(
            mapOf("flac" to "2000kflac", "320k" to "320kmp3", "128k" to "128kmp3"),
            kuwo.qualityMap,
        )
        assertEquals(mapOf("flac" to "lossless", "320k" to "exhigh", "128k" to "standard"), netease.qualityMap)
        assertEquals("gbk", kuwo.search.encoding)
        assertEquals("https://www.kuwo.cn/", kuwo.urlApi.headers["Referer"])
    }

    @Test
    fun storageOverridesDefaultsAndGarbageFallsBack() {
        val saved = listOf(kuwo.copy(name = "酷我（自定义）", enabled = false))
        val storage = MemStorage(MusicSourceRegistry.encodeList(saved))
        val r = registry(FakeHttp(), storage)
        assertEquals("酷我（自定义）", r.all().first { it.id == "kuwo" }.name)
        assertNull("禁用后 get 拿不到（与 C# 的 `s.Enabled` 判定一致）", r.get("kuwo"))

        assertEquals(2, registry(FakeHttp(), MemStorage("{坏掉的 json")).all().size)
        assertEquals(2, registry(FakeHttp(), MemStorage("")).all().size)
    }

    @Test
    fun upsertReplacesInPlaceAndKeepsTheOthers() {
        val storage = MemStorage()
        val r = registry(FakeHttp(), storage)
        r.upsert(netease.copy(name = "网易云改名"))
        val all = r.all()
        assertEquals(2, all.size)
        assertEquals("网易云改名", all.first { it.id == "netease" }.name)
        assertEquals("内置源必须还在（update_music_source 只改一个源）", "酷我音乐", all.first { it.id == "kuwo" }.name)
    }

    @Test
    fun placeholdersAndPercentEncoding() {
        assertEquals(
            "http://search.kuwo.cn/r.s?all=%E5%91%A8%E6%9D%B0%E4%BC%A6&ft=music&rformat=json",
            queryOf("http://search.kuwo.cn/r.s", linkedMapOf("all" to "{kw}", "ft" to "music", "rformat" to "json"), mapOf("kw" to "周杰伦")),
        )
        assertEquals("{a}{b}", MusicSourceRegistry.fill("{a}{b}", emptyMap()))
        assertEquals("1-2", MusicSourceRegistry.fill("{x}-{y}", mapOf("x" to "1", "y" to "2")))
    }

    private fun queryOf(url: String, params: Map<String, String>, values: Map<String, String>): String {
        val filled = params.entries.joinToString("&") { (k, v) ->
            "${MusicSourceRegistry.encodeComponent(k)}=${MusicSourceRegistry.encodeComponent(MusicSourceRegistry.fill(v, values))}"
        }
        return "$url?$filled"
    }

    @Test
    fun kuwoBlocksAreSplitMarkedAndDecoded() {
        val text = "junk{'AARTIST':'x','SONGNAME':'稻香 &amp; Cover','ARTIST':'周杰伦','MUSICRID':'MUSIC_123','FORMATS':'flac,mp3'}" +
            "{'AARTIST':'y','SONGNAME':'第二首','ARTIST':'某人','MUSICRID':'MUSIC_456'}"
        val songs = MusicSourceRegistry.parseBlocks(kuwo.regexes, text)
        assertEquals(listOf("MUSIC_123", "MUSIC_456"), songs.map { it.id })
        assertEquals("稻香 & Cover", songs[0].name)
        assertEquals("周杰伦", songs[0].artist)
        assertEquals("flac,mp3", songs[0].formats)
        assertEquals("没有 FORMATS 字段时留空而不是 null", "", songs[1].formats)
    }

    @Test
    fun searchHitsTheConfiguredUrlAndDecodesGbk() = runTest {
        val text = "{'AARTIST':'x','SONGNAME':'夜曲','ARTIST':'李袁杰','MUSICRID':'MUSIC_1'}"
        val bytes = text.toByteArray(charset("GBK"))
        val http = FakeHttp(responses = mutableMapOf("http://search.kuwo.cn/r.s" to bytes))
        val songs = registry(http).search(kuwo, "夜曲")
        assertEquals(listOf("夜曲"), songs.map { it.name })
        assertTrue("关键词要按百分号编码进 query", http.singleUrl().contains("%E5%A4%9C%E6%9B%B2"))
        assertEquals("搜索这一跳没有自定义头（Referer 只挂在取链那一跳上）", emptyMap<String, String>(), http.getHeaders.single())
    }

    private fun FakeHttp.singleUrl() = gets.single()

    @Test
    fun kuwoUrlPrefersSurlAndStripsTheMusicPrefix() = runTest {
        val http = FakeHttp(
            responses = mutableMapOf(
                "https://mobi.kuwo.cn/mobi.s" to
                    """{"data":{"surl":"https://t1.kuwo.cn/ok.flac?sign=1"}}""".toByteArray(),
            ),
        )
        val url = registry(http).url(kuwo, SourceSong("MUSIC_789"), "flac")
        assertEquals("https://t1.kuwo.cn/ok.flac?sign=1", url)
        assertTrue("rid 不该带 MUSIC_ 前缀", http.singleUrl().contains("rid=789"))
        assertTrue(http.singleUrl().contains("br=2000kflac"))
    }

    @Test
    fun kuwoUrlFallsBackToRawUrlField() = runTest {
        val http = FakeHttp(
            responses = mutableMapOf("https://mobi.kuwo.cn/mobi.s" to "url=https%3A%2F%2Fa.b%2Fc.mp3".toByteArray()),
        )
        assertEquals("https://a.b/c.mp3", registry(http).url(kuwo, SourceSong("1"), "未知质量"))
        assertTrue("未知质量要回落到 128kmp3", http.singleUrl().contains("br=128kmp3"))
    }

    @Test
    fun neteaseSearchJoinsArtists() = runTest {
        val body = """{"result":{"songs":[
            {"id":11,"name":"晴天","artists":[{"name":"周杰伦"},{"name":"X"}]},
            {"name":"没有 id 的要被丢掉","artists":[]},
            {"id":22,"name":"安静"}
        ]}}"""
        val http = FakeHttp(responses = mutableMapOf("https://music.163.com/api/search/get/web" to body.toByteArray()))
        val songs = registry(http).search(netease, "周杰伦")
        assertEquals(listOf("11", "22"), songs.map { it.id })
        assertEquals("周杰伦, X", songs[0].artist)
        assertEquals("", songs[1].artist)
    }

    /**
     * eapi 的签名进摘要，所以**加密链路必须独立验证**而不是"跑一遍看像不像"：
     * 把产出的十六进制用同一密钥解回去，逐字节核对 `path-salt-payload-salt-md5` 的拼装。
     */
    @Test
    fun eapiParamsRoundTripThroughAesEcb() = runTest {
        val http = FakeHttp(postResponse = """{"data":[{"url":"https://m1.163.cn/ok.mp3"}]}""".toByteArray())
        val url = registry(http).url(netease, SourceSong("186016"), "320k")
        assertEquals("https://m1.163.cn/ok.mp3", url)

        val hex = http.lastForm.getValue("params")
        assertTrue("十六进制必须是大写且成块", Regex("^[0-9A-F]+$").matches(hex) && hex.length % 32 == 0)
        val recovered = MusicSourceRegistry.aes128EcbUnhex(hex, "e82ckenh8dichen8")
        val payload = """{"ids":[186016],"level":"exhigh","encodeType":"flac","immerseType":"c51"}"""
        val digest = MusicSourceRegistry.md5Hex("nobody/api/song/enhance/player/url/v1use${payload}md5forencrypt")
        assertEquals("/api/song/enhance/player/url/v1-36cd479b6b5-$payload-36cd479b6b5-$digest", recovered)
        assertEquals(
            "os=pc; appver=; osver=; deviceId=pyncm!",
            http.lastPostHeaders["Cookie"],
        )
        assertEquals("https://music.163.com/", http.lastPostHeaders["Referer"])
    }

    @Test
    fun shortUrlAndFailuresAreReportedAsNullNotCrashes() = runTest {
        val shortUrl = FakeHttp(postResponse = """{"data":[{"url":"tiny"}]}""".toByteArray())
        assertNull(registry(shortUrl).url(netease, SourceSong("1"), "128k"))

        assertNull("HTTP 返回 null", registry(FakeHttp()).url(kuwo, SourceSong("9"), "flac"))
        assertEquals("HTTP 挂掉时搜索给空表", emptyList<SourceSong>(), registry(FakeHttp()).search(kuwo, "x"))
        assertEquals("模板不认识就什么都不做", null, registry(FakeHttp()).url(kuwo.copy(template = "weird"), SourceSong("1"), "flac"))
    }

    @Test
    fun md5HexMatchesTheKnownDigestOfAnEmptyString() {
        // 独立可核对的已知值：MD5("") = d41d8cd98f00b204e9800998ecf8427e
        assertEquals("d41d8cd98f00b204e9800998ecf8427e", MusicSourceRegistry.md5Hex(""))
    }

    @Test
    fun fileBackedStorageRoundTripsLikePrefsWould() {
        // 用临时文件当存储，确认编码里没有任何平台相关假设
        val tmp = File.createTempFile("sources", ".json").apply { deleteOnExit() }
        val storage = object : SourceStorage {
            override fun read(): String? = tmp.readText().takeIf { it.isNotBlank() }
            override fun write(value: String) { tmp.writeText(value) }
        }
        val r = registry(FakeHttp(), storage)
        r.upsert(kuwo.copy(name = "酷我 A"))
        assertTrue(tmp.readText().contains("酷我 A"))
        assertEquals("酷我 A", registry(FakeHttp(), storage).get("kuwo")?.name)
    }
}
