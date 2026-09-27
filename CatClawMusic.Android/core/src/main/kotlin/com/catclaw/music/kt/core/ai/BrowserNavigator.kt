package com.catclaw.music.kt.core.ai

/**
 * `browser_open` 的浏览器口子。对应 C# 的 `AgentBrowserBridge.Navigator` 静态委托：
 * 工具在 Core、浏览器在平台层，用委托解耦。
 *
 * 与 `fetch_web_page` 的分界照 C# 原话：本口子背后的浏览器**会执行页面 JS**，
 * 动态渲染 / SPA / 实时数据页只有走它才拿得到内容，[PageFetcher] 那条只能取原始 HTML。
 *
 * 返回渲染后的正文；拿不到就返回 null 或空串，由工具层收敛成给模型的提示。
 */
fun interface BrowserNavigator {
    suspend fun openAndExtract(url: String): String?
}
