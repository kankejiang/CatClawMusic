package com.catclaw.music.kt

import android.app.Application
import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.data.diagnostic.CrashReporter
import com.catclaw.music.kt.data.diagnostic.DiagnosticLogStore
import com.catclaw.music.kt.data.netease.NeteaseLoginClient
import dagger.hilt.android.HiltAndroidApp
import javax.inject.Inject
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

// Application 只挂 @HiltAndroidApp：它自己就会做成员注入，
// 再加 @AndroidEntryPoint 会被 Hilt 判成"两个入口注解同时存在"而拒绝编译
@HiltAndroidApp
class CatClawApplication : Application() {

    @Inject
    lateinit var diagnosticLog: DiagnosticLogStore

    @Inject
    lateinit var crashReporter: CrashReporter

    @Inject
    lateinit var neteaseLogin: NeteaseLoginClient

    /** 会话续期定时器：只在进程存活期间跑，App 被杀就停（下次启动 30 秒后自动续上） */
    private val sessionScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    override fun onCreate() {
        super.onCreate()
        // 门面在 :core，落盘实现在 :data —— 这里是一次性的接线，
        // 之后任何模块都能 AppLog.xxx 而不必依赖 :data
        AppLog.attach(diagnosticLog)
        // 未捕获异常落盘（见 CrashReporter）。挂在这里而不是各 Activity：
        // 崩溃可能发生在任何线程、任何界面之前
        crashReporter.install()
        // 网易云会话续期：30 秒后首检，之后每 6 小时（与插件一致）。
        // 只在已登录时真正发请求；未登录 refreshSession 直接返回 false。
        sessionScope.launch {
            delay(30_000)
            while (true) {
                runCatching { neteaseLogin.refreshSession() }
                delay(6 * 60 * 60 * 1000L)
            }
        }
    }
}
