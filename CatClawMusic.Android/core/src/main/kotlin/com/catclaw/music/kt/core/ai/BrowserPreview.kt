package com.catclaw.music.kt.core.ai

/**
 * 浏览器预览窗的宿主 —— 对应 C# `AgentBrowserPreview` 的协调器那一半。
 *
 * 为什么要有这一层：MAUI 的 agent 抓网页时**用户看得见**（聊天页顶部一个小窗，可手动关），
 * 而本端实现的是「不挂到窗口上的 WebView」（`WebViewBrowserNavigator` 的注释里写着）。
 * 无头那条路能出结果，但用户完全不知道 agent 在读什么，也没法中断。
 *
 * 现在的规矩：**界面上开着预览窗就用它**（同一个 WebView 既显示又提取）。
 * 不这么做、而是「显示一个 + 另开一个无头取正文」的话，同一页要拉两次，
 * 而且用户看到的与 agent 读到的不是同一份 —— 这类不一致在真机上几乎没法查。
 */
interface BrowserPreviewHost {
    /** 载入 url、等 JS 就绪、返回正文；取不到返回 null（与无头路径同一收敛口径） */
    suspend fun loadAndExtract(url: String): String?
}

/**
 * 当前宿主登记处。宿主是**聊天页在这次组合里**的那一个，随组合进入/离开登记与摘除。
 *
 * 做成类而不是单例对象：测试各自 `BrowserPreview()` 互不干扰
 * （本工程已经吃过一次亏 —— `AppLog` 的环形缓冲是进程级单例，测试互相看得见彼此的日志，
 * 只能改成断言相对位置）。生产用 [shared]。
 */
class BrowserPreview {

    @Volatile
    private var host: BrowserPreviewHost? = null

    fun current(): BrowserPreviewHost? = host

    fun attach(value: BrowserPreviewHost) {
        host = value
    }

    /** 只摘自己那一个：新页面先 attach、旧页面后 detach 时，不能把新的摘掉 */
    fun detach(value: BrowserPreviewHost) {
        if (host === value) host = null
    }

    companion object {
        /** 生产用的那一个 —— 由 `:data` 的取流实现与 `:app` 的预览窗共用 */
        val shared = BrowserPreview()
    }
}
