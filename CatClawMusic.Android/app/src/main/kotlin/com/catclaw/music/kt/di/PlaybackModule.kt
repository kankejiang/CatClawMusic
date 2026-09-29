package com.catclaw.music.kt.di

import android.content.Context
import com.catclaw.music.kt.core.audio.AudioCache
import com.catclaw.music.kt.core.playback.KeyValueStore
import com.catclaw.music.kt.core.playback.PlayerApi
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.core.playback.SessionLauncher
import com.catclaw.music.kt.data.equalizer.AndroidAudioFx
import com.catclaw.music.kt.data.equalizer.AudioFxPreferences
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.Module
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
        // 音效同理：会话就绪那一刻把效果挂上，并把存下来的设置应用一遍，
        // 否则"上次调过 EQ、这次没进 EQ 页"就等于没设过
        onAudioSessionReady = { sessionId ->
            audioFx.attach(sessionId)
            audioFx.apply(fxPrefs.settings.value)
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
}
