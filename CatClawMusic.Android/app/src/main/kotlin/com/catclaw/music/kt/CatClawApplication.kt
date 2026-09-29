package com.catclaw.music.kt

import android.app.Application
import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.data.diagnostic.DiagnosticLogStore
import dagger.hilt.android.HiltAndroidApp
import javax.inject.Inject

// Application 只挂 @HiltAndroidApp：它自己就会做成员注入，
// 再加 @AndroidEntryPoint 会被 Hilt 判成"两个入口注解同时存在"而拒绝编译
@HiltAndroidApp
class CatClawApplication : Application() {

    @Inject
    lateinit var diagnosticLog: DiagnosticLogStore

    override fun onCreate() {
        super.onCreate()
        // 门面在 :core，落盘实现在 :data —— 这里是一次性的接线，
        // 之后任何模块都能 AppLog.xxx 而不必依赖 :data
        AppLog.attach(diagnosticLog)
    }
}
