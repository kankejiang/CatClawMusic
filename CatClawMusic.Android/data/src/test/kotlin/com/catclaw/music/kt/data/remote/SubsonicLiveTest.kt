package com.catclaw.music.kt.data.remote

import com.catclaw.music.kt.core.model.ConnectionProfile
import com.catclaw.music.kt.core.model.ProtocolType
import org.junit.Assume.assumeTrue
import org.junit.Test

/**
 * 用**真实 Navidrome** 验证 Subsonic 客户端。
 *
 * 单测里塞假 JSON 只能证明解析对；连不上时的症状是「测试连接失败」一个词，
 * 而失败可能来自：域名解析、明文 HTTP 策略、认证签名、路径前缀、协议版本。
 * 只有对着真服务器跑一次才能分清是哪一种。
 *
 * 环境不满足时整体跳过。
 */
class SubsonicLiveTest {

    private val host = System.getenv("CATCLAW_TEST_NDR_HOST")
    private val port = System.getenv("CATCLAW_TEST_NDR_PORT")?.toIntOrNull() ?: 4533
    private val user = System.getenv("CATCLAW_TEST_NDR_USER")
    private val pass = System.getenv("CATCLAW_TEST_NDR_PASS")

    private fun profile(basePath: String = "") = ConnectionProfile(
        name = "live-ndr",
        protocol = ProtocolType.Navidrome,
        host = host.orEmpty(),
        port = port,
        userName = user.orEmpty(),
        password = pass.orEmpty(),
        basePath = basePath,
        useHttps = false,
    )

    @Test
    fun `真实 Navidrome ping 通过并把版本报出来`() {
        assumeTrue("未配置 Navidrome 测试凭据，跳过", !host.isNullOrBlank() && !user.isNullOrBlank())
        val ping = SubsonicClient(profile()).ping()
        println("ping => ok=${ping.ok} version=${ping.version} message=${ping.message}")
        assert(ping.ok) { "ping 失败: ${ping.message}" }
    }

    @Test
    fun `真实 Navidrome 能列出专辑与曲目`() {
        assumeTrue("未配置 Navidrome 测试凭据，跳过", !host.isNullOrBlank() && !user.isNullOrBlank())
        val client = SubsonicClient(profile())
        val albums = client.allAlbums(maxPages = 2, pageSize = 20)
        println("albums=${albums.size} first=${albums.firstOrNull()}")
        assumeTrue("这个库没有专辑，跳过后续断言", albums.isNotEmpty())
        val songs = client.albumSongs(albums.first().id)
        println("songs of first album=${songs.size} first=${songs.firstOrNull()?.title}")
    }

    @Test
    fun `basePath 带斜杠也能 ping 通`() {
        assumeTrue("未配置 Navidrome 测试凭据，跳过", !host.isNullOrBlank() && !user.isNullOrBlank())
        // Subsonic 的 rest 端点在站点根，basePath 一般留空；但用户可能误填子路径。
        // 这里只确认「填了 /」不会把 URL 拼坏（双斜杠）。
        val ping = SubsonicClient(profile(basePath = "/")).ping()
        println("ping(basePath=/) => ok=${ping.ok} ${ping.message}")
        assert(ping.ok) { "basePath 误填时拼出的 URL 不可用: ${ping.message}" }
    }
}
