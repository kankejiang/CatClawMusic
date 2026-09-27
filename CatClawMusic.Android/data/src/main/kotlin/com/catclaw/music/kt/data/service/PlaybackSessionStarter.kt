package com.catclaw.music.kt.data.service

import android.content.Context
import android.content.Intent
import com.catclaw.music.kt.core.playback.SessionLauncher
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 播放器单例本来就活在应用进程里，但要通知栏 / 锁屏 / 媒体会话在位，
 * 就得把 MediaSessionService 拉起来 —— 任何「从 UI 直接发起播放」的入口都要先走这一步。
 */
@Singleton
class PlaybackSessionStarter @Inject constructor(
    @ApplicationContext private val context: Context,
) : SessionLauncher {
    override fun ensure() {
        runCatching { context.startService(Intent(context, MediaPlaybackService::class.java)) }
    }
}
