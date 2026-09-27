package com.catclaw.music.kt.data.service

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Intent
import android.os.Build
import androidx.annotation.OptIn
import androidx.media3.session.DefaultMediaNotificationProvider
import androidx.media3.session.MediaSession
import androidx.media3.common.util.UnstableApi
import androidx.media3.session.MediaSessionService
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.lyrics.DesktopLyricHost
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import com.catclaw.music.kt.data.R
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

/**
 * 媒体会话前台服务：把 PlayerController 的 ExoPlayer 直接交给 MediaSession，
 * 锁屏 / 车机 / 耳机的上一曲下一曲因此与 App 内看到的是同一份列表。
 *
 * 通知参数沿用 C# ForegroundPlayerService：id 1001、渠道 `catclaw_playback`「播放控制」、
 * Importance.Low、锁屏 Public、不显示角标。
 */
@AndroidEntryPoint
@OptIn(UnstableApi::class)
class MediaPlaybackService : MediaSessionService() {

    @Inject
    lateinit var controller: PlayerController

    @Inject
    lateinit var desktopLyricHost: DesktopLyricHost

    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    private var session: MediaSession? = null

    override fun onCreate() {
        super.onCreate()
        controller.start()
        desktopLyricHost.start(serviceScope)
        createChannel()

        val mediaSession = MediaSession.Builder(this, controller.player)
            .setSessionActivity(launchPendingIntent())
            .build()
        session = mediaSession

        setMediaNotificationProvider(
            DefaultMediaNotificationProvider.Builder(this)
                .setNotificationId(NOTIFICATION_ID)
                .setChannelId(CHANNEL_ID)
                .setChannelName(R.string.playback_channel_name)
                .build(),
        )
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaSession? = session

    override fun onTaskRemoved(rootIntent: Intent?) {
        val player = controller.player
        if (!player.playWhenReady || player.mediaItemCount == 0) {
            stopSelf()
        }
        super.onTaskRemoved(rootIntent)
    }

    override fun onDestroy() {
        serviceScope.cancel()
        session?.release()
        session = null
        super.onDestroy()
    }

    private fun createChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val manager = getSystemService(NotificationManager::class.java)
        if (manager.getNotificationChannel(CHANNEL_ID) != null) return
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL_ID, getString(R.string.playback_channel_name), NotificationManager.IMPORTANCE_LOW).apply {
                description = getString(R.string.playback_channel_description)
                lockscreenVisibility = android.app.Notification.VISIBILITY_PUBLIC
                setShowBadge(false)
            },
        )
    }

    private fun launchPendingIntent(): PendingIntent {
        val launch = packageManager.getLaunchIntentForPackage(packageName) ?: Intent()
        return PendingIntent.getActivity(
            this,
            REQUEST_CODE_LAUNCH,
            launch,
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
    }

    private companion object {
        const val CHANNEL_ID = "catclaw_playback"
        const val NOTIFICATION_ID = 1001
        const val REQUEST_CODE_LAUNCH = 5
    }
}
