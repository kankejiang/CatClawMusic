package com.catclaw.music.kt.lyrico.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.lyrico.write.EditableTags
import com.catclaw.music.kt.lyrico.write.TagEditor
import com.catclaw.music.kt.lyrico.write.TagReadOutcome
import com.catclaw.music.kt.lyrico.write.TagSyncService
import com.catclaw.music.kt.lyrico.write.TagWriteOutcome
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class MetadataEditUiState(
    val target: LyricsMatchTarget? = null,
    val loading: Boolean = false,
    val saving: Boolean = false,
    val original: EditableTags? = null,
    val edited: EditableTags = EditableTags(),
    val message: String? = null,
    /** 当前歌曲在库里的既有属性，用于同步时保留未编辑字段 */
    val currentGenre: String? = null,
    val currentYear: Int = 0,
    val currentTrack: Int = 0,
    val supported: Boolean = true,
) {
    val dirty: Boolean get() = original != null && original != edited
}

@HiltViewModel
class MetadataEditViewModel @Inject constructor(
    private val tagEditor: TagEditor,
    private val tagSync: TagSyncService,
) : ViewModel() {

    private val _state = MutableStateFlow(MetadataEditUiState())
    val state: StateFlow<MetadataEditUiState> = _state.asStateFlow()

    fun prepare(target: LyricsMatchTarget) {
        if (_state.value.target?.songId == target.songId) return
        _state.value = MetadataEditUiState(target = target)
        viewModelScope.launch {
            _state.update { it.copy(loading = true) }
            when (val outcome = tagEditor.read(target.filePath)) {
                is TagReadOutcome.Success -> _state.update {
                    it.copy(
                        loading = false,
                        original = outcome.tags,
                        edited = outcome.tags,
                        supported = true,
                    )
                }
                is TagReadOutcome.Unsupported -> _state.update {
                    it.copy(loading = false, supported = false, message = outcome.reason)
                }
                is TagReadOutcome.Failed -> _state.update {
                    it.copy(loading = false, supported = false, message = "读取失败：${outcome.message}")
                }
            }
        }
    }

    fun update(transform: (EditableTags) -> EditableTags) =
        _state.update { it.copy(edited = transform(it.edited)) }

    fun updateCurrentAttributes(genre: String?, year: Int, track: Int) = _state.update {
        it.copy(currentGenre = genre, currentYear = year, currentTrack = track)
    }

    fun consumeMessage() = _state.update { it.copy(message = null) }

    fun save() {
        val s = _state.value
        val target = s.target ?: return
        val original = s.original ?: return
        if (s.saving || !s.dirty) return
        viewModelScope.launch {
            _state.update { it.copy(saving = true, message = null) }
            when (val outcome = tagEditor.write(target.filePath, original, s.edited)) {
                is TagWriteOutcome.Success -> {
                    if (outcome.changed.isEmpty()) {
                        _state.update { it.copy(saving = false, message = "没有改动") }
                        return@launch
                    }
                    // 同步曲库：否则回列表还是旧标题
                    val synced = runCatching {
                        tagSync.sync(
                            songId = target.songId,
                            edited = s.edited,
                            currentGenre = s.currentGenre,
                            currentYear = s.currentYear,
                            currentTrack = s.currentTrack,
                        )
                    }.onFailure {
                        // 之前这里直接 getOrDefault(false)，同步失败被完全吞掉：
                        // 用户看到「已写入但标题没变」，日志里也没有任何线索。
                        com.catclaw.music.kt.core.diagnostic.AppLog.w(
                            "LyricoTagSync",
                            "曲库同步失败 songId=${target.songId}",
                            it,
                        )
                    }.getOrDefault(false)
                    _state.update {
                        it.copy(
                            saving = false,
                            original = s.edited,
                            message = buildString {
                                append("已写入 ")
                                append(outcome.changed.size)
                                append(" 个字段")
                                if (synced) append("，曲库已同步")
                            },
                        )
                    }
                }
                is TagWriteOutcome.Unsupported -> _state.update {
                    it.copy(saving = false, message = "无法写入：${outcome.reason}")
                }
                is TagWriteOutcome.Failed -> _state.update {
                    it.copy(saving = false, message = "写入失败：${outcome.message}")
                }
            }
        }
    }
}
