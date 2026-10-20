package com.catclaw.music.kt.core.ai

import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Test

class BrowserPreviewTest {

    private class FakeHost(private val reply: String? = "正文") : BrowserPreviewHost {
        var calls = 0
        override suspend fun loadAndExtract(url: String): String? {
            calls++
            return reply
        }
    }

    @Test
    fun `没有宿主时 current 为空`() {
        assertNull(BrowserPreview().current())
    }

    @Test
    fun `登记后按宿主取正文`() = runTest {
        val preview = BrowserPreview()
        val host = FakeHost()
        preview.attach(host)
        assertSame(host, preview.current())
        assertEquals("正文", preview.current()!!.loadAndExtract("https://a.test"))
        assertEquals(1, host.calls)
    }

    @Test
    fun `摘除只摘自己那一个`() {
        val preview = BrowserPreview()
        val first = FakeHost()
        val second = FakeHost()
        preview.attach(first)
        preview.attach(second)
        // 旧页面后摘：不能把新登记的摘掉（页面切换时先 attach 新、后 detach 旧的）
        preview.detach(first)
        assertSame(second, preview.current())
        preview.detach(second)
        assertNull(preview.current())
    }

    @Test
    fun `取不到正文时宿主返回 null 而不是抛`() = runTest {
        val preview = BrowserPreview()
        preview.attach(FakeHost(reply = null))
        assertNull(preview.current()!!.loadAndExtract("https://a.test"))
    }
}
