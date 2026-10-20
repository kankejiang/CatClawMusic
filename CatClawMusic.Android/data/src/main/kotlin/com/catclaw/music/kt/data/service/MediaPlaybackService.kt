package com.catclaw.music.kt.data.service

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Intent
import android.os.Build
import android.os.Bundle
import androidx.annotation.OptIn
import androidx.media3.common.util.UnstableApi
import androidx.media3.session.CommandButton
import androidx.media3.session.DefaultMediaNotificationProvider
import androidx.media3.session.MediaNotification
import androidx.media3.session.MediaSession
import androidx.media3.session.MediaSessionService
import androidx.media3.session.SessionCommand
import androidx.media3.session.SessionCommands
import androidx.media3.session.SessionResult
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.R
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.entity.FavoriteEntity
import com.catclaw.music.kt.data.lyrics.DesktopLyricHost
import com.catclaw.music.kt.data.lyrics.DesktopLyricSettings
import com.catclaw.music.kt.data.lyrics.OverlayPermission
import com.google.common.util.concurrent.Futures
import com.google.common.util.concurrent.ListenableFuture
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

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

    @Inject
    lateinit var desktopLyric: DesktopLyricSettings

    @Inject
    lateinit var libraryDao: LibraryDao

    @Inject
    lateinit var icons: NotificationIcons

    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    /** 系统媒体通知的生成器（media3 自带样式：封面、标题、歌手、系统播放按钮） */
    private val notificationProvider: MediaNotification.Provider by lazy {
        DefaultMediaNotificationProvider.Builder(this)
            .setNotificationId(NOTIFICATION_ID)
            .setChannelId(CHANNEL_ID)
            .setChannelName(R.string.playback_channel_name)
            .build()
    }

    private var session: MediaSession? = null

    override fun onCreate() {
        super.onCreate()
        controller.start()
        desktopLyricHost.start(serviceScope)
        createChannel()
        // 通知 provider 必须在**建会话之前**配好：media3 的通知管理器在第一次需要时取用，
        // 放在建会话之后配，早期那次 onUpdateNotification 会拿不到 provider 而静默不挂通知
        setMediaNotificationProvider(notificationProvider)
        // 暂停/停止时也保留通知（MAUI 的 ForegroundPlayerService 同样常驻）：
        // 默认策略在播放器空闲时撤通知，用户一暂停通知就没了
        setShowNotificationForIdlePlayer(SHOW_NOTIFICATION_FOR_IDLE_PLAYER_ALWAYS)

        val mediaSession = MediaSession.Builder(this, controller.player)
            .setSessionActivity(launchPendingIntent())
            .setCallback(sessionCallback)
            .build()
        session = mediaSession
        // 通知栏那两颗按钮：内容随状态变（开着桌面歌词时显示「关闭」、已收藏显示实心），
        // 所以进来先摆一次，再订阅状态变化重摆
        refreshNotificationButtons()
        serviceScope.launch {
            launch { desktopLyric.enabled.collect { refreshNotificationButtons() } }
            // 收藏一变化就重摆按钮：应用内点收藏、通知栏点收藏都会走到这里（Room 的 Flow 自己负责通知）
            launch {
                runCatching { libraryDao.observeFavorites().collect { refreshNotificationButtons() } }
            }
            launch {
                controller.state.collect {
                    refreshNotificationButtons()
                    updateMediaNotification()
                }
            }
        }

    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val result = super.onStartCommand(intent, flags, startId)
        // 兜底：服务每次被拉起都按当前状态刷新一次通知（media3 在「startService + 进程内播放器」
        // 这种非常规组合下不会自己挂，得由我们按播放状态推它）
        updateMediaNotification()
        return result
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

    /**
     * 会话回调：这里只做一件事 —— 认领通知栏那两颗自定义按钮。
     *
     * `onConnect` 必须把这两个命令列进可用集合：media3 的规矩是「按钮画得出来 ≠ 点了能进来」，
     * 没列进去的话通知栏发过来的命令会被直接拒掉，表现成点了没反应。
     */
    private val sessionCallback = object : MediaSession.Callback {
        override fun onConnect(
            session: MediaSession,
            controller: MediaSession.ControllerInfo,
        ): MediaSession.ConnectionResult = MediaSession.ConnectionResult.AcceptedResultBuilder(session)
            .setAvailableSessionCommands(
                SessionCommands.Builder()
                    .add(SessionCommand(ACTION_LYRICS, Bundle.EMPTY))
                    .add(SessionCommand(ACTION_FAVORITE, Bundle.EMPTY))
                    .build(),
            )
            .build()

        override fun onCustomCommand(
            session: MediaSession,
            controller: MediaSession.ControllerInfo,
            customCommand: SessionCommand,
            args: Bundle,
        ): ListenableFuture<SessionResult> = when (customCommand.customAction) {
            ACTION_LYRICS -> {
                toggleDesktopLyricFromNotification()
                Futures.immediateFuture(SessionResult(SessionResult.RESULT_SUCCESS))
            }
            ACTION_FAVORITE -> {
                toggleFavoriteFromNotification()
                Futures.immediateFuture(SessionResult(SessionResult.RESULT_SUCCESS))
            }
            else -> super.onCustomCommand(session, controller, customCommand, args)
        }
    }

    /** 重摆按钮：文案与图标都跟状态走（开着桌面歌词显示「关闭」、已收藏显示实心） */
    private fun refreshNotificationButtons() {
        val target = session ?: return
        serviceScope.launch {
            val song = controller.state.value.currentSong
            val favorited = song != null &&
                runCatching { libraryDao.isFavorite(song.id) }.getOrDefault(false)
            val lyricsOn = desktopLyric.enabled.value
            target.setCustomLayout(
                listOf(
                    CommandButton.Builder()
                        .setDisplayName(
                            getString(
                                if (lyricsOn) R.string.notif_desktop_lyric_off
                                else R.string.notif_desktop_lyric_on,
                            ),
                        )
                        // 开/关两套图标：SystemUI 统一染色，只能靠形状区分
                        .setIconResId(if (lyricsOn) icons.desktopLyricOn else icons.desktopLyricOff)
                        .setSessionCommand(SessionCommand(ACTION_LYRICS, Bundle.EMPTY))
                        .build(),
                    CommandButton.Builder()
                        .setDisplayName(
                            getString(
                                if (favorited) R.string.notif_favorite_remove
                                else R.string.notif_favorite_add,
                            ),
                        )
                        .setIconResId(if (favorited) icons.favoriteOn else icons.favoriteOff)
                        .setSessionCommand(SessionCommand(ACTION_FAVORITE, Bundle.EMPTY))
                        // 没有当前曲时这颗心没有意义：置灰，而不是让它点了没反应
                        .setEnabled(song != null)
                        .build(),
                ),
            )
            // 摆完按钮**必须**重建一次通知：setCustomLayout 只改会话侧的命令集合，
            // 系统通知是媒体通知生成器产出的快照 —— 不重建的话图标/文案一直停在上一次
            //（2026-10-20 用户反馈「按钮图标没有变化」，实测就是这个原因）
            updateMediaNotification()
        }
    }

    /**
     * 通知栏那颗「桌面歌词」。判定与播放页/设置页同一条：没权限就先带用户去授 ——
     * 服务里弹不了对话框，只能把系统那个授权页拉起来（与 `SettingScreen` 的做法一致）。
     */
    private fun toggleDesktopLyricFromNotification() {
        if (desktopLyric.enabled.value) {
            desktopLyric.setEnabled(false)
            return
        }
        if (!OverlayPermission.granted(this)) {
            runCatching {
                startActivity(OverlayPermission.requestIntent(this).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            }
            return
        }
        desktopLyric.setEnabled(true)
    }

    /** 通知栏那颗「收藏」：与歌曲菜单同一个 DAO 写入口，列表页与它看到的永远是同一份 */
    private fun toggleFavoriteFromNotification() {
        val song = controller.state.value.currentSong ?: return
        serviceScope.launch {
            runCatching {
                if (libraryDao.isFavorite(song.id)) {
                    libraryDao.removeFavorite(song.id)
                } else {
                    libraryDao.upsertFavorite(
                        FavoriteEntity(songId = song.id, addedAt = System.currentTimeMillis()),
                    )
                }
                // 立即重摆一次（observeFavorites 也会来，但那要等 Room 发通知，体感上会顿一下）
                refreshNotificationButtons()
            }
        }
    }

    /**
     * 把**系统媒体通知**挂出来 / 更新它。
     *
     * media3 的 `MediaSessionService` 只在自己认为必要时 `startForeground` —— 而「服务用
     * `startService` 起来、播放器随后才装载媒体」这种 in-process 用法下它**根本不发通知**
     * （2026-10-20 实测：播放中 `dumpsys notification` 里没有本应用任何 NotificationRecord；
     * 改用 `startForegroundService` 则 5 秒后 `ForegroundServiceDidNotStartInTimeException`
     * 把整个进程打崩）。官方给的出口就是 `MediaNotification.Provider.onUpdateNotification(..)`：
     * 我们自己决定时机，通知内容与样式仍由 media3 的系统媒体通知生成器产出。
     *
     * `startInForegroundRequired` = 正在播放：暂停时传 false 只刷新不占前台，
     * 免得空转的前台服务白常驻；没有媒体时直接跳过，否则会把服务崩掉。
     */
    private fun updateMediaNotification() {
        val mediaSession = session ?: return
        val player = controller.player
        if (player.mediaItemCount == 0) return
        // onUpdateNotification 是 MediaSessionService 自己的 public 方法（media3 1.11.1 用 javap 核实），
        // 不在 MediaNotification.Provider 上 —— 后者只有 createNotification/handleCustomCommand
        runCatching { onUpdateNotification(mediaSession, player.isPlaying) }
            .onFailure { android.util.Log.e("CatClawNotif", "onUpdateNotification failed", it) }
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

        /** 与 C# `ForegroundPlayerService` 的两个 CustomAction 同名（`catclaw.custom.*`） */
        const val ACTION_LYRICS = "catclaw.custom.LYRICS"
        const val ACTION_FAVORITE = "catclaw.custom.FAVORITE"
    }
}
