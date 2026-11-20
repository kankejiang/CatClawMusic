package com.catclaw.music.kt.lyrico.ui

import androidx.lifecycle.ViewModel
import com.catclaw.music.kt.lyrico.settings.ArtistSplitConfig
import com.catclaw.music.kt.lyrico.settings.CharacterMapping
import com.catclaw.music.kt.lyrico.settings.EditFieldVisibility
import com.catclaw.music.kt.lyrico.settings.LyricoSettings
import com.catclaw.music.kt.lyrico.settings.LyricoSettingsStore
import com.catclaw.music.kt.lyrico.settings.LyricsCleanupRules
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** 设置页的状态：直接镜像 [LyricoSettingsStore] 的当前值。 */
@HiltViewModel
class LyricoSettingsViewModel @Inject constructor(
    private val store: LyricoSettingsStore,
) : ViewModel() {

    private val _state = MutableStateFlow(store.current())
    val state: StateFlow<LyricoSettings> = _state.asStateFlow()

    private fun update(transform: (LyricoSettings) -> LyricoSettings) {
        _state.value = store.update(transform)
    }

    // ── 字段可见性 ──
    fun toggleEditField(key: String, enabled: Boolean) = update {
        it.copy(editFields = it.editFields.toggle(key, enabled))
    }

    // ── 清理规则 ──
    fun setRemoveCreditLines(enabled: Boolean) = update {
        it.copy(cleanup = it.cleanup.copy(removeCreditLines = enabled))
    }

    fun setRemoveEmptyLines(enabled: Boolean) = update {
        it.copy(cleanup = it.cleanup.copy(removeEmptyLines = enabled))
    }

    fun setTrimLines(enabled: Boolean) = update {
        it.copy(cleanup = it.cleanup.copy(trimLines = enabled))
    }

    fun setRemoveMetadataTags(enabled: Boolean) = update {
        it.copy(cleanup = it.cleanup.copy(removeMetadataTags = enabled))
    }

    fun setDedupeTimestamps(enabled: Boolean) = update {
        it.copy(cleanup = it.cleanup.copy(dedupeTimestamps = enabled))
    }

    // ── 艺人拆分 ──
    fun setArtistSplitEnabled(enabled: Boolean) = update {
        it.copy(artistSplit = it.artistSplit.copy(enabled = enabled))
    }

    fun setArtistJoinSeparator(separator: String) = update {
        it.copy(artistSplit = it.artistSplit.copy(joinSeparator = separator.ifBlank { "/" }))
    }

    // ── 字符映射 ──
    fun setCharacterMappingEnabled(enabled: Boolean) = update {
        it.copy(characterMapping = it.characterMapping.copy(enabled = enabled))
    }

    /** 以 `源=目标` 的形式整块设置映射表（设置页用一个多行输入框编辑）。 */
    fun setCharacterMappingPairs(text: String) = update { settings ->
        val pairs = text.lineSequence()
            .mapNotNull { line ->
                val trimmed = line.trim()
                if (trimmed.isEmpty() || trimmed.startsWith("#")) return@mapNotNull null
                val idx = trimmed.indexOf('=')
                if (idx <= 0) return@mapNotNull null
                val from = trimmed.substring(0, idx)
                val to = trimmed.substring(idx + 1)
                if (from.isEmpty()) null else from to to
            }
            .toMap()
        settings.copy(characterMapping = settings.characterMapping.copy(pairs = pairs))
    }

    /** 把映射表导出成设置页可编辑的文本。 */
    fun characterMappingText(settings: LyricoSettings): String =
        settings.characterMapping.pairs.entries.joinToString("\n") { "${it.key}=${it.value}" }

    // ── 默认值 ──
    fun setDefaultOverwrite(enabled: Boolean) = update { it.copy(defaultOverwrite = enabled) }

    fun reset() {
        _state.value = LyricoSettings()
        store.save(LyricoSettings())
    }
}
