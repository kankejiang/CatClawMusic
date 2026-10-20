package com.catclaw.music.kt.di

import android.content.Context
import com.catclaw.music.kt.core.audio.AudioCache
import com.catclaw.music.kt.core.playback.KeyValueStore
import com.catclaw.music.kt.core.playback.PlayerApi
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.core.playback.SessionLauncher
import com.catclaw.music.kt.data.equalizer.AndroidAudioFx
import com.catclaw.music.kt.data.equalizer.AudioFxPreferences
import com.catclaw.music.kt.data.service.NotificationIcons
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.Module
import com.catclaw.music.kt.R
import dagger.Provides
import dagger.hilt.InstallIn
import com.catclaw.music.kt.data.settings.CacheSettings
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import java.io.File
import javax.inject.Singleton
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import com.catclaw.music.kt.data.playback.PlayHistoryRecorder
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

@Module
@InstallIn(SingletonComponent::class)
object PlaybackModule {

    @Provides
    @Singleton
    fun keyValueStore(@ApplicationContext context: Context): KeyValueStore {
        val prefs = context.getSharedPreferences("catclaw_state", Context.MODE_PRIVATE)
        return object : KeyValueStore {
            override fun put(key: String, value: String) {
                prefs.edit().putString(key, value).apply()
            }

            override fun get(key: String): String? = prefs.getString(key, null)
        }
    }

    @Provides
    @Singleton
    fun audioCache(
        @ApplicationContext context: Context,
        cacheSettings: CacheSettings,
    ): AudioCache = AudioCache(
        File(context.cacheDir, "music_cache"),
        maxSizeMb = cacheSettings.sizeMbSnapshot(),
    )

    @Provides
    @Singleton
    fun appScope(): CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    @Provides
    @Singleton
    fun playerController(
        @ApplicationContext context: Context,
        cache: AudioCache,
        store: KeyValueStore,
        scope: CoroutineScope,
        history: PlayHistoryRecorder,
        audioFx: AndroidAudioFx,
        fxPrefs: AudioFxPreferences,
    ): PlayerController = PlayerController(context, cache, store, scope).apply {
        // 在构造点接，不挂在服务的 onCreate 上：冷启动时第一个拿到这个单例的往往是 Activity，
        // 整个会话里媒体服务可能压根没被拉起 —— 挂服务上就会静默不记（真机验到过）
        onListenStarted = { songId -> history.record(songId) }
        // 收听时长统计走同一条链：:core 只报"现在听到哪儿了"，落库在这层
        onListenSample = { id, pos, playing -> history.sample(id, pos, playing) }
        // 音效同理：会话就绪那一刻把效果挂上，并把存下来的设置应用一遍，
        // 否则"上次调过 EQ、这次没进 EQ 页"就等于没设过
        onAudioSessionReady = { sessionId ->
            audioFx.attach(sessionId)
            audioFx.apply(fxPrefs.settings.value)
        }
        // 淡入淡出不走 audiofx：它是"跟着进度改音量"，归播放器自己管。
        // 在这里收一条流，设置页改一次同步一次，冷启动时也把存下来的值喂进去一次
        scope.launch {
            fxPrefs.settings.collect { s -> setCrossfade(s.crossfade, s.crossfadeSec) }
        }
    }

    /**
     * AI 工具层只依赖这两个窄接口，才能在 JVM 单测里用假实现构造 `MusicAgentTools`
     * （直接依赖 `PlayerController` 等于所有工具都只能上真机测）。
     */
    @Provides
    @Singleton
    fun playerApi(controller: PlayerController): PlayerApi = controller

    @Provides
    @Singleton
    fun sessionLauncher(starter: PlaybackSessionStarter): SessionLauncher = starter

    /**
     * 通知栏那两颗自定义按钮的图标。
     *
     * 服务在 `:data`，而 `:data` 一个 drawable 都没有 —— 由 app 这边把资源 id 交过去，
     * 两边用的永远是同一份图标（见 `NotificationIcons` 的注释：同名资源跨模块会互相覆盖）。
     */
    @Provides
    @Singleton
    fun notificationIcons(): NotificationIcons = NotificationIcons(
        // 开/关各一张：SystemUI 会给图标统一染色，形态差异只能靠**形状**表达（实心 vs 空心）
        desktopLyricOn = R.drawable.ic_notif_lyric_on,
        desktopLyricOff = R.drawable.ic_notif_lyric_off,
        // 通知栏用自己那套 24dp 心形，不借界面的大图标：要和 media3 的细线按钮同一种重量
        favoriteOn = R.drawable.ic_notif_favorite,
        favoriteOff = R.drawable.ic_notif_favorite_border,
    )
}
