package com.catclaw.music.kt.data.service

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import androidx.core.content.ContextCompat
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import com.catclaw.music.kt.core.playback.SessionLauncher
import com.google.common.util.concurrent.FutureCallback
import com.google.common.util.concurrent.Futures
import com.google.common.util.concurrent.ListenableFuture
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 播放器单例本来就活在应用进程里，但要通知栏 / 锁屏 / 媒体会话在位，
 * 就得把 MediaSessionService 拉起来 —— 任何「从 UI 直接发起播放」的入口都要先走这一步。
 *
 * 2026-10-20「通知栏播放控件」修复的关键一步：**再连一条 `MediaController`**。
 * media3 只有在有控制器连上来时，才会把 `MediaSessionService` 当成真正的媒体服务来管理
 * （挂系统媒体通知、进前台）。只 `startService` 不连控制器时：服务在跑、会话也在、锁屏/车机都能用，
 * 但通知栏永远空白 —— 实测 `dumpsys notification` 零记录，服务 `startRequested=true` 却不 `isForeground`。
 * 这个模式照抄自 Halcyon 的 `ExoPlayerManager.connectAfterRestore()`（同类可用的 media3 实现）。
 *
 * 控制器不用来做播放控制（本应用直接驱动 ExoPlayer），它存在的意义就是让 media3 接管通知。
 */
@Singleton
class PlaybackSessionStarter @Inject constructor(
    @ApplicationContext private val context: Context,
) : SessionLauncher {

    /** 连接中的 future 与结果都要持有引用，否则会被回收、连接随之断开 */
    private var controllerFuture: ListenableFuture<MediaController>? = null

    @Suppress("unused")
    private var mediaController: MediaController? = null

    override fun ensure() {
        // 只能是 startService，**不能**用 startForegroundService：
        // 这一步发生在「装载媒体之前」，播放器还是空的，media3 这时不会调 startForeground，
        // 用前台方式启动会在 5 秒后抛 ForegroundServiceDidNotStartInTimeException 把进程打崩
        // （2026-10-20 实测崩溃栈就是这个）。
        runCatching { context.startService(Intent(context, MediaPlaybackService::class.java)) }
        connectController()
    }

    private fun connectController() {
        if (controllerFuture != null) return
        val token = SessionToken(
            context,
            ComponentName(context, MediaPlaybackService::class.java),
        )
        val future = MediaController.Builder(context, token).buildAsync()
        controllerFuture = future
        Futures.addCallback(
            future,
            object : FutureCallback<MediaController> {
                override fun onSuccess(result: MediaController?) {
                    mediaController = result
                }

                override fun onFailure(t: Throwable) {
                    // 连接失败就允许下次重试；异常不往 UI 线程冒
                    if (controllerFuture === future) controllerFuture = null
                }
            },
            ContextCompat.getMainExecutor(context),
        )
    }
}
