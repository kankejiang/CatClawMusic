package com.catclaw.music.kt.data.ai

import android.content.Context
import android.os.Handler
import android.os.Looper
import android.webkit.RenderProcessGoneDetail
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import com.catclaw.music.kt.core.ai.BrowserNavigator
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

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

    override suspend fun openAndExtract(url: String): String? = withContext(Dispatchers.Main) {
        withTimeoutOrNull(TOTAL_TIMEOUT_MS) { extractOnce(url) }
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

        /** 与 C# `AgentBrowserPreview.OnWebViewNavigated` 那段脚本同功能：
         *  剥掉 script/style/nav/header/footer/iframe/form 后取 innerText，压空白、截 8000 字。 */
        val EXTRACT_JS = """
            (function(){
                var t = document.title || '';
                var body = document.body;
                if (!body) return JSON.stringify({title:t,text:''});
                var clone = body.cloneNode(true);
                ['script','style','noscript','svg','nav','header','footer','aside','iframe','form'].forEach(function(s){
                    var els = clone.querySelectorAll(s);
                    for (var i = els.length - 1; i >= 0; i--) els[i].parentNode.removeChild(els[i]);
                });
                var text = (clone.innerText || clone.textContent || '')
                    .replace(/\u00a0/g,' ').replace(/[ \t]{2,}/g,' ')
                    .replace(/\n{3,}/g,'\n\n').trim().slice(0, 8000);
                return JSON.stringify({title:t,text:text});
            })();
        """.trimIndent()

        /**
         * `evaluateJavascript` 回调给的是 **JS 值的 JSON 编码**，而脚本本身返回一个字符串，
         * 所以拿到的是「字符串里再套一层 JSON」：`"{\"title\":\"x\",\"text\":\"y\"}"`。
         * 直接按对象解会抛 —— 必须先剥外层字符串。解不出来就当没正文。
         */
        fun unwrap(raw: String?): String? {
            if (raw.isNullOrBlank() || raw == "null") return null
            val inner = runCatching {
                ((RUNTIME_JSON.parseToJsonElement(raw) as? JsonPrimitive)?.takeIf { it.isString }?.content) ?: return null
            }.getOrNull() ?: return null
            val obj = runCatching { RUNTIME_JSON.parseToJsonElement(inner) as? JsonObject }.getOrNull() ?: return null
            val text = (obj["text"] as? JsonPrimitive)?.takeIf { it.isString }?.content
            return text?.trim()?.takeIf { it.isNotEmpty() }
        }

        fun ensureScheme(url: String): String =
            if (url.startsWith("http://", true) || url.startsWith("https://", true)) url else "https://$url"

        val RUNTIME_JSON = Json { ignoreUnknownKeys = true }
    }
}
