package com.catclaw.music.kt.ui.settings

import android.content.Context
import android.content.Intent
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import com.catclaw.music.kt.core.audio.AudioCache
import com.catclaw.music.kt.core.ai.LlmConfig
import com.catclaw.music.kt.core.ai.OpenAiCompatibleClient
import com.catclaw.music.kt.core.theme.AppearanceMode
import com.catclaw.music.kt.core.ai.ChatMemoryRules
import com.catclaw.music.kt.data.ai.ChatMemoryStore
import com.catclaw.music.kt.data.ai.LlmConfigStore
import com.catclaw.music.kt.data.lyrics.DesktopLyricSettings
import com.catclaw.music.kt.data.lyrics.DesktopLyricStatus
import com.catclaw.music.kt.data.download.MusicDownloader
import com.catclaw.music.kt.data.lyrics.OverlayPermission
import com.catclaw.music.kt.data.settings.AppearanceSettings
import com.catclaw.music.kt.data.settings.CacheSettings
import com.catclaw.music.kt.ui.theme.ThemeStore
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@HiltViewModel
class SettingsViewModel @Inject constructor(
    @ApplicationContext private val context: Context,
    private val themeStore: ThemeStore,
    private val desktopLyric: DesktopLyricSettings,
    private val lyricStatus: DesktopLyricStatus,
    private val cacheSettings: CacheSettings,
    private val audioCache: AudioCache,
    private val downloader: MusicDownloader,
    private val appearance: AppearanceSettings,
    private val llmStore: LlmConfigStore,
    private val llmClient: OpenAiCompatibleClient,
    private val memory: ChatMemoryStore,
) : ViewModel() {

    /**
     * Yuki 的长期记忆。模型自己提炼出来的东西必须看得见、删得掉 ——
     * "它记住了我什么"这个问题不该只有模型知道答案。
     */
    val memories: StateFlow<List<ChatMemoryRules.Item>> = memory.items

    fun addMemory(content: String) {
        viewModelScope.launch { memory.add(content) }
    }

    fun removeMemory(id: String) {
        viewModelScope.launch { memory.remove(id) }
    }

    fun clearMemory() {
        viewModelScope.launch { memory.clear() }
    }

    val cacheSizeMb: StateFlow<Int> = cacheSettings.sizeMb

    /** 缓存占用与下载目录都要现算，所以做成手动刷新的快照而不是 Flow */
    private val _cacheUsage = MutableStateFlow(0L)
    val cacheUsageBytes: StateFlow<Long> = _cacheUsage.asStateFlow()

    fun refreshCacheUsage() {
        viewModelScope.launch(Dispatchers.IO) {
            _cacheUsage.value = audioCache.totalBytes() + downloader.directorySizeBytes()
        }
    }

    fun setCacheSizeMb(value: Int) {
        cacheSettings.setSizeMb(value)
        audioCache.maxSizeBytes = cacheSettings.sizeMb.value.toLong() * 1024L * 1024L
    }

    fun clearAudioCache() {
        viewModelScope.launch(Dispatchers.IO) {
            audioCache.clear()
            _cacheUsage.value = audioCache.totalBytes() + downloader.directorySizeBytes()
        }
    }

    fun downloadDirectory(): String = downloader.directoryPath()

    val startupIndex: StateFlow<Int> = appearance.startupIndex
    val monetEnabled: StateFlow<Boolean> = appearance.monetEnabled
    val hasCustomBackground: StateFlow<Boolean> = appearance.customBgPath
        .map { it != null }
        .stateIn(viewModelScope, SharingStarted.Eagerly, appearance.customBgPath.value != null)

    fun setStartupIndex(value: Int) = appearance.setStartupIndex(value)

    fun setMonetEnabled(value: Boolean) = appearance.setMonetEnabled(value)

    /**
     * 把选中的图复制进应用私有的 backgrounds 目录，文件名固定 `custom_bg.<ext>` 覆盖上一张 ——
     * 与 C# `AppearanceSettingsViewModel.cs:140-149` 同做法（不留原始 URI，重启后还能读）。
     * @return 成功与否
     */
    fun importCustomBackground(uri: android.net.Uri): Boolean {
        val ext = context.contentResolver.getType(uri)?.substringAfterLast('/', "")?.let {
            if (it.contains("jpeg") || it.contains("jpg")) "jpg" else if (it.contains("png")) "png" else "jpg"
        } ?: "jpg"
        val dest = java.io.File(context.filesDir, "backgrounds").apply { mkdirs() }
        val target = java.io.File(dest, "custom_bg.$ext")
        return runCatching {
            context.contentResolver.openInputStream(uri)?.use { input ->
                target.outputStream().use { input.copyTo(it) }
            } ?: return false
            appearance.setCustomBackground(target.absolutePath)
            true
        }.getOrDefault(false)
    }

    fun clearCustomBackground() = appearance.setCustomBackground(null)

    val mode: StateFlow<AppearanceMode> = themeStore.mode
    /**
     * 设置页要的是**用户意图**，两个都得单独给：
     * 窗口没起来时（权限被回收、服务被杀、刚装完还没附加）若这里也显示"关"，
     * 用户点开关会写成开、界面却仍是关——开关看起来是坏的，
     * 而字号/透明度/颜色这些行还被它挡住完全进不去（真机复现过）。
     */
    val desktopLyricWanted: StateFlow<Boolean> = desktopLyric.enabled
    val desktopLyricAttached: StateFlow<Boolean> = lyricStatus.attached
    val desktopLyricLocked: StateFlow<Boolean> = desktopLyric.locked
    val desktopLyricFontSize: StateFlow<Float> = desktopLyric.fontSizeSp
    val desktopLyricBgOpacity: StateFlow<Float> = desktopLyric.bgOpacity
    val desktopLyricTextColor: StateFlow<Int> = desktopLyric.textColor
    val desktopLyricHighlightColor: StateFlow<Int> = desktopLyric.highlightColor


    fun setDesktopLyricFontSize(size: Float) = desktopLyric.setFontSize(size)

    fun setDesktopLyricBgOpacity(value: Float) = desktopLyric.setBgOpacity(value)

    /** 两个颜色都只写设置；悬浮窗那条 onStyleChanged 会就地重绘（#35 已验过这条链） */
    fun setDesktopLyricTextColor(argb: Int) = desktopLyric.setTextColor(argb)

    fun setDesktopLyricHighlightColor(argb: Int) = desktopLyric.setHighlightColor(argb)

    fun setMode(mode: AppearanceMode) = themeStore.setMode(mode)

    /** @return false 表示缺悬浮窗权限，调用方应转去系统设置授权 */
    fun setDesktopLyric(enabled: Boolean): Boolean {
        if (!enabled) {
            desktopLyric.setEnabled(false)
            return true
        }
        if (!OverlayPermission.granted(context)) return false
        desktopLyric.setEnabled(true)
        return true
    }

    fun setDesktopLyricLocked(locked: Boolean) = desktopLyric.setLocked(locked)

    fun overlayPermissionIntent(): Intent = OverlayPermission.requestIntent(context)

    // ── AI 设置域 ────────────────────────────────────────────────

    val llmConfigs: StateFlow<List<LlmConfig>> = llmStore.configs
    val currentConfigName: StateFlow<String> = llmStore.currentName

    var maxToolRounds: Int
        get() = llmStore.maxToolRounds
        set(value) {
            llmStore.maxToolRounds = value
        }

    var reasoningEffort: String
        get() = llmStore.reasoningEffort
        set(value) {
            llmStore.reasoningEffort = value
        }

    fun saveConfig(config: LlmConfig) = llmStore.save(config)

    fun deleteConfig(name: String) = llmStore.delete(name)

    fun useConfig(name: String) = llmStore.setCurrentName(name)

    /**
     * 连通性测试。`OpenAiCompatibleClient.testConnection` 是**阻塞**调用（内部就是同步读 SSE/HTTP），
     * 所以必须挪到 IO 线程，结果再回主线程给 UI —— 与 C# 那边同一约束。
     */
    fun testConnection(onResult: (Boolean) -> Unit) {
        viewModelScope.launch(Dispatchers.IO) {
            val ok = runCatching { llmClient.testConnection() }.getOrDefault(false)
            withContext(Dispatchers.Main) { onResult(ok) }
        }
    }
}
