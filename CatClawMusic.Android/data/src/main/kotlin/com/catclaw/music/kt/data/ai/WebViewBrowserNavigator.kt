package com.catclaw.music.kt.data.ai

import android.content.Context
import android.os.Handler
import android.os.Looper
import android.webkit.RenderProcessGoneDetail
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import com.catclaw.music.kt.core.ai.BrowserExtraction
import com.catclaw.music.kt.core.ai.BrowserNavigator
import com.catclaw.music.kt.core.ai.BrowserPreview
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

/**
 * `browser_open` 的安卓实现：一个不挂到窗口上的 WebView，等 JS 跑完再把正文取回来。
 *
 * 三条不能省的约束：
 * 1. **全程在主线程**。WebView 的构造与 `loadUrl`/`evaluateJavascript` 都要求调用线程有 Looper。
 * 2. **导航完成不等于内容就绪**。SPA / 天气这类页面在 `onPageFinished` 之后才刚开始填 DOM，
 *    所以照 C# 一样再等 [SETTLE_MS] 毫秒才注入脚本（早取就是空正文）。
 * 3. **渲染进程可能被系统杀掉**（内存压力）。不接 `onRenderProcessGone` 的话整个 App 会一起被杀，
 *    这里接住并按「没取到正文」收敛。
 */
@Singleton
class WebViewBrowserNavigator @Inject constructor(
    @ApplicationContext private val context: Context,
) : BrowserNavigator {

    override suspend fun openAndExtract(url: String): String? {
        // 聊天页开着预览窗就走它：同一个 WebView 既给用户看、又用来取正文。
        // 同时再开一个无头的话，同一页要拉两次，而且**用户看到的与 agent 读到的不是同一份**
        BrowserPreview.shared.current()?.let { host ->
            return withContext(Dispatchers.Main) { withTimeoutOrNull(TOTAL_TIMEOUT_MS) { host.loadAndExtract(url) } }
        }
        return withContext(Dispatchers.Main) { withTimeoutOrNull(TOTAL_TIMEOUT_MS) { extractOnce(url) } }
    }

    private suspend fun extractOnce(url: String): String? = suspendCancellableCoroutine { cont ->
        val main = Handler(Looper.getMainLooper())
        val webView = WebView(context)
        var settled = false

        fun finish(value: String?) {
            if (settled) return
            settled = true
            main.post { runCatching { webView.destroy() } }
            if (cont.isActive) cont.resumeWith(Result.success(value))
        }

        webView.settings.javaScriptEnabled = true
        webView.settings.domStorageEnabled = true
        webView.isFocusable = false
        webView.webViewClient = object : WebViewClient() {
            override fun onPageFinished(view: WebView, endedUrl: String) {
                main.postDelayed({
                    runCatching { view.evaluateJavascript(EXTRACT_JS) { raw -> finish(unwrap(raw)) } }
                }, SETTLE_MS)
            }

            override fun onReceivedError(view: WebView, request: WebResourceRequest, error: WebResourceError) {
                // 只认主文档失败；页内子资源的 404 不该让整次抓取作废
                if (request.isForMainFrame) finish(null)
            }

            override fun onRenderProcessGone(view: WebView, detail: RenderProcessGoneDetail): Boolean {
                finish(null)
                return true
            }
        }
        cont.invokeOnCancellation { main.post { runCatching { webView.destroy() } } }
        runCatching { webView.loadUrl(ensureScheme(url)) }.onFailure { finish(null) }
    }

    internal companion object {
        const val SETTLE_MS = 800L
        const val TOTAL_TIMEOUT_MS = 25_000L

        /**
         * 与预览窗共用同一份脚本：就是 [BrowserExtraction.EXTRACT_JS]（C# `AgentBrowserPreview.OnWebViewNavigated`
         * 那段同功能 —— 剥掉 script/style/nav/header/footer/iframe/form 后取 innerText，压空白、截 8000 字）。
         * 留这条转发是为了让「无头取流」这条老路继续引用同一个常量，而不是各存一份副本。
         */
        val EXTRACT_JS get() = BrowserExtraction.EXTRACT_JS

        /**
         * `evaluateJavascript` 回调给的是 **JS 值的 JSON 编码**，而脚本本身返回一个字符串，
         * 所以拿到的是「字符串里再套一层 JSON」：`"{\"title\":\"x\",\"text\":\"y\"}"`。
         * 直接按对象解会抛 —— 必须先剥外层字符串。解不出来就当没正文。
         */
        fun unwrap(raw: String?): String? = BrowserExtraction.unwrap(raw)

        fun ensureScheme(url: String): String = BrowserExtraction.ensureScheme(url)
    }
}
