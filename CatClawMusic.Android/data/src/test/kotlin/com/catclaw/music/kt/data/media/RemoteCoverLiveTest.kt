package com.catclaw.music.kt.data.media

import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder
import java.security.SecureRandom
import java.security.cert.X509Certificate
import java.util.Base64
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Test

/**
 * 用**真实 NAS** 验证「内嵌封面抽取」这条链路的端到端行为。
 *
 * 单测里的构造字节只能证明「偏移算对了」；真正的问题往往在别处：
 * 服务器不理 Range、头部窗口拿到的东西比预期短、图被超大 APIC 挤到深处……
 * 那些只有拿真文件跑一次才知道。
 *
 * 环境不满足（没网 / 没配凭据）时整体跳过，不会让常规测试变红。
 * 跑法：
 * ```
 * CATCLAW_TEST_NAS_HOST=nas.example.com CATCLAW_TEST_NAS_USER=u CATCLAW_TEST_NAS_PASS=p \
 *   ./gradlew :data:testDebugUnitTest --tests '*RemoteCoverLiveTest*'
 * ```
 */
class RemoteCoverLiveTest {

    private val host = System.getenv("CATCLAW_TEST_NAS_HOST")
    private val user = System.getenv("CATCLAW_TEST_NAS_USER")
    private val pass = System.getenv("CATCLAW_TEST_NAS_PASS")
    private val port = System.getenv("CATCLAW_TEST_NAS_PORT") ?: "5006"
    private val file = System.getenv("CATCLAW_TEST_NAS_FILE") ?: "傅许 - 寂静之空.flac"

    private val sslFactory by lazy {
        val tm = object : X509TrustManager {
            override fun checkClientTrusted(c: Array<out X509Certificate>?, a: String?) = Unit
            override fun checkServerTrusted(c: Array<out X509Certificate>?, a: String?) = Unit
            override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
        }
        SSLContext.getInstance("TLS").apply { init(null, arrayOf(tm), SecureRandom()) }.socketFactory
    }

    private fun fetch(url: String, rangeHeader: String?): ByteArray? {
        val conn = URL(url).openConnection() as HttpURLConnection
        if (conn is HttpsURLConnection) {
            conn.sslSocketFactory = sslFactory
            // 自签证书的 CN 与访问用的域名常不一致，hostnameVerifier 也必须放开，
            // 否则 socketFactory 换对了照样 SSLEngine 校验失败
            conn.hostnameVerifier = javax.net.ssl.HostnameVerifier { _, _ -> true }
        }
        conn.connectTimeout = 15_000
        conn.readTimeout = 30_000
        conn.setRequestProperty(
            "Authorization",
            "Basic " + Base64.getEncoder().encodeToString("$user:$pass".toByteArray()),
        )
        if (rangeHeader != null) conn.setRequestProperty("Range", rangeHeader)
        return try {
            if (conn.responseCode !in 200..299) null else conn.inputStream.use { it.readBytes() }
        } finally {
            conn.disconnect()
        }
    }

    @Test
    fun `真实 FLAC 能定位并抽出内嵌封面`() {
        assumeTrue("未配置 NAS 测试凭据，跳过", !host.isNullOrBlank() && !user.isNullOrBlank())
        val encoded = URLEncoder.encode(file, "UTF-8").replace("+", "%20")
        val url = "https://$host:$port/Music/$encoded"

        // 1) 头部窗口定位
        val head = fetch(url, "bytes=0-${TagPicture.HEAD_WINDOW - 1}")
        assumeTrue("取不到头部窗口（网络或路径问题）", head != null)
        assertTrue("窗口不该为空", head!!.isNotEmpty())

        val location = TagPicture.locate(head)
        assertTrue("真实 FLAC 里应该能定位到 PICTURE", location != null)
        assertTrue(
            "图长度应合理（KB 到 MB 级），实际 ${location!!.length}",
            location.length in 1024..TagPicture.MAX_COVER_BYTES,
        )

        // 2) 按定位补取 —— 回填器与 UI 走的是同一条路
        val picture = if (location.end <= head.size) {
            TagPicture.extract(head)
        } else {
            val tail = fetch(url, "bytes=${head.size}-${location.end - 1}")
            assumeTrue("补取失败", tail != null)
            TagPicture.extract(head + tail!!)
        }

        assertTrue("应该抽出图片", picture != null)
        assertTrue("MIME 应是 image/*，实际 ${picture!!.mime}", picture.mime.startsWith("image/"))
        assertTrue("应该是真的 JPEG/PNG 而不是偏移差一位的垃圾", isImageMagic(picture.bytes))
    }

    private fun isImageMagic(b: ByteArray): Boolean =
        (b.size > 3 && b[0] == 0xFF.toByte() && b[1] == 0xD8.toByte()) ||
            (b.size > 8 && b[0] == 0x89.toByte() && b[1] == 0x50.toByte() && b[2] == 0x4E.toByte())
}
