package com.catclaw.music.kt.data.ai

import java.util.concurrent.atomic.AtomicInteger

/** 内存假抓取器：按脚本依次返回，并记下每次用的 UA（换 UA 重试那条分支要看这个才知道走没走到） */
internal class FakeFetcher(
    val replies: MutableList<FetchedPage>,
    val uas: MutableList<String?> = mutableListOf(),
    val calls: AtomicInteger = AtomicInteger(),
) : PageFetcher {
    override suspend fun fetch(url: String, userAgent: String?): FetchedPage {
        uas += userAgent
        calls.incrementAndGet()
        return replies.removeAt(0)
    }
}

internal fun page(status: Int = 200, type: String = "text/html", body: String = "", cf: Boolean = false, url: String = "https://example.com/a") =
    FetchedPage(url = url, status = status, contentType = type, body = body, cfMitigated = cf)
