package com.catclaw.music.kt.core.ai

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonPrimitive

/**
 * `browser_open` 取正文的那一段：注入脚本 + 返回值剥壳 + 补协议头。
 *
 * 从 `:data` 的 WebView 实现里提出来，是因为**看得见的预览窗与无头取流要用同一份脚本**：
 * MAUI 那边就是同一个 `AgentBrowserPreview` 既显示又提取（`OnWebViewNavigated`）。
 * 两份脚本各写一遍，迟早在「剥掉哪些标签 / 截多少字」上分叉，而分叉的后果是
 * 「用户看到的正文」与「agent 读到的正文」不是同一份 —— 排查起来极难。
 */
object BrowserExtraction {

    /** 剥掉 script/style/nav/header/footer/iframe/form 后取 innerText，压空白、截 8000 字 */
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

    private val JSON = Json { ignoreUnknownKeys = true }

    /**
     * `evaluateJavascript` 回调给的是 **JS 值的 JSON 编码**，而脚本本身返回一个字符串，
     * 所以拿到的是「字符串里再套一层 JSON」：`"{\"title\":\"x\",\"text\":\"y\"}"`。
     * 直接按对象解会抛 —— 必须先剥外层字符串。解不出来就当没正文。
     */
    fun unwrap(raw: String?): String? {
        if (raw.isNullOrBlank() || raw == "null") return null
        val inner = runCatching {
            (JSON.parseToJsonElement(raw).jsonPrimitive.takeIf { it.isString }?.content) ?: return null
        }.getOrNull() ?: return null
        val obj = runCatching { JSON.parseToJsonElement(inner) as? JsonObject }.getOrNull() ?: return null
        val text = (obj["text"] as? JsonPrimitive)?.takeIf { it.isString }?.content
        return text?.trim()?.takeIf { it.isNotEmpty() }
    }

    fun ensureScheme(url: String): String =
        if (url.startsWith("http://", true) || url.startsWith("https://", true)) url else "https://$url"
}
