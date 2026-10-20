package com.catclaw.music.kt.ui.chat

import android.webkit.RenderProcessGoneDetail
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.collectAsState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.ai.BrowserExtraction
import com.catclaw.music.kt.core.ai.BrowserPreview
import com.catclaw.music.kt.core.ai.BrowserPreviewHost
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.glassCard
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.coroutines.resume
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

/**
 * Agent 浏览器预览窗 —— MAUI `Controls/AgentBrowserPreview.xaml` 的对应物。
 *
 * agent 调 `browser_open` 时，聊天页顶部出现这个小窗，加载它正在读的页面；
 * 用户可以看着它读，也可以直接关掉（关掉即中止这次抓取）。
 *
 * 存在的理由不是「好看」：本端原来走的是**不挂到窗口上的 WebView**（`WebViewBrowserNavigator`），
 * 结果能出来，但用户完全不知道 agent 在读什么、也没法中断 —— 一个会自己上网的 agent，
 * 「它在读哪个页面」是必须可见的。
 *
 * 取正文与显示合用**同一个 WebView**：再开一个无头的话，同一页要拉两次，
 * 而且用户看到的与 agent 读到的不是同一份。
 */
class AgentBrowserPreviewController : BrowserPreviewHost {

    private val _url = MutableStateFlow<String?>(null)

    /** 当前显示的地址，null = 不显示 */
    val url: StateFlow<String?> = _url.asStateFlow()

    private var view: WebView? = null
    private var ready: CompletableDeferred<WebView>? = null
    private var pending: ((String?) -> Unit)? = null

    override suspend fun loadAndExtract(url: String): String? {
        _url.value = url
        // 组合还没跑起来时 WebView 还不存在：等它（超时就当没取到，与无头路径同一收敛口径）
        val webView = view ?: withTimeoutOrNull(VIEW_WAIT_MS) {
            (ready ?: CompletableDeferred<WebView>().also { ready = it }).await()
        } ?: return null
        return withContext(Dispatchers.Main) {
            suspendCancellableCoroutine { cont ->
                val settled = AtomicBoolean(false)
                fun finish(value: String?) {
                    if (!settled.compareAndSet(false, true)) return
                    pending = null
                    if (cont.isActive) cont.resume(value)
                }
                pending = { finish(it) }
                webView.webViewClient = object : WebViewClient() {
                    override fun onPageFinished(v: WebView, endedUrl: String) {
                        // 导航完成不等于内容就绪：SPA 在 onPageFinished 之后才填 DOM，早取就是空正文
                        v.postDelayed({
                            runCatching {
                                v.evaluateJavascript(BrowserExtraction.EXTRACT_JS) { raw ->
                                    finish(BrowserExtraction.unwrap(raw))
                                }
                            }.onFailure { finish(null) }
                        }, SETTLE_MS)
                    }

                    override fun onReceivedError(v: WebView, request: WebResourceRequest, error: WebResourceError) {
                        // 只认主文档失败；页内子资源的 404 不该让整次抓取作废
                        if (request.isForMainFrame) finish(null)
                    }

                    override fun onRenderProcessGone(v: WebView, detail: RenderProcessGoneDetail): Boolean {
                        finish(null)
                        return true
                    }
                }
                cont.invokeOnCancellation { finish(null) }
                runCatching { webView.loadUrl(BrowserExtraction.ensureScheme(url)) }.onFailure { finish(null) }
            }
        }
    }

    /** 用户点关闭：中止这次抓取（agent 那边会按「没取到正文」收敛），并把窗收起来 */
    fun close() {
        _url.value = null
        pending?.invoke(null)
    }

    internal fun onViewCreated(webView: WebView) {
        view = webView
        ready?.complete(webView)
    }

    internal fun onViewDisposed(webView: WebView) {
        if (view === webView) view = null
        // 下一次要等的是新的那个 WebView；不换的话 await 会在已完成的 Deferred 上立刻返回旧实例
        ready = null
        pending?.invoke(null)
        runCatching { webView.destroy() }
    }

    private companion object {
        const val SETTLE_MS = 800L
        const val VIEW_WAIT_MS = 3_000L
    }
}

/** 把预览窗挂进聊天页：进入组合时登记为当前宿主，离开时摘掉 */
@Composable
fun AgentBrowserPreview(controller: AgentBrowserPreviewController, modifier: Modifier = Modifier) {
    val palette = LocalCatClawPalette.current
    val context = LocalContext.current
    val url by controller.url.collectAsState()
    val shown = url ?: return
    // 记住组合造出来的那个 WebView：销毁时要交还给控制器（它负责 destroy 并让出等着的取正文那一路）
    var created by remember { mutableStateOf<WebView?>(null) }

    DisposableEffect(controller) {
        BrowserPreview.shared.attach(controller)
        onDispose {
            BrowserPreview.shared.detach(controller)
            created?.let { controller.onViewDisposed(it) }
            created = null
        }
    }

    Column(
        modifier = modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 6.dp)
            .glassCard(palette = palette, corner = 20)
            .padding(horizontal = 10.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(
                text = stringResource(R.string.browser_preview_title),
                fontSize = 12.sp,
                color = Color(palette.textSecondary),
                maxLines = 1,
            )
            Text(
                text = shown,
                fontSize = 11.sp,
                color = Color(palette.textSecondary),
                maxLines = 1,
                overflow = TextOverflow.MiddleEllipsis,
                modifier = Modifier.weight(1f),
            )
            Box(
                modifier = Modifier
                    .size(24.dp)
                    .clip(RoundedCornerShape(12.dp))
                    .background(Color(palette.textPrimary).copy(alpha = 0.10f))
                    .clickable(onClickLabel = stringResource(R.string.browser_preview_close)) { controller.close() },
                contentAlignment = Alignment.Center,
            ) {
                Text(text = "✕", fontSize = 12.sp, color = Color(palette.textPrimary))
            }
        }
        // 小窗高度固定：让它跟着页面高度走的话，页面一加载完聊天页会整体位移，
        // 而且长页面会把输入行挤出屏幕外
        AndroidView(
            factory = { ctx ->
                WebView(ctx).also {
                    created = it
                    controller.onViewCreated(it)
                }
            },
            update = { webView ->
                webView.settings.javaScriptEnabled = true
                webView.settings.domStorageEnabled = true
                webView.isFocusable = false
            },
            modifier = Modifier
                .fillMaxWidth()
                .height(180.dp)
                .clip(RoundedCornerShape(12.dp)),
        )
    }
}
