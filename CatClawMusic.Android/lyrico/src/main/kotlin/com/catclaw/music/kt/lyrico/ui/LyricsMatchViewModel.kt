package com.catclaw.music.kt.lyrico.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.lyrico.lyrics.LyricoLyricsEncoder
import com.catclaw.music.kt.lyrico.lyrics.LyricoLyricsFormat
import com.catclaw.music.kt.lyrico.lyrics.LyricoToLyrics
import com.catclaw.music.kt.lyrico.search.LyricsCandidate
import com.catclaw.music.kt.lyrico.search.LyricsSearchService
import com.catclaw.music.kt.lyrico.settings.LyricsRuleEngine
import com.catclaw.music.kt.lyrico.settings.LyricoSettings
import com.catclaw.music.kt.lyrico.settings.LyricoSettingsStore
import com.catclaw.music.kt.lyrico.search.LyricsTarget
import com.catclaw.music.kt.lyrico.write.LyricsWriteOutcome
import com.catclaw.music.kt.lyrico.write.LyricsWriter
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** 歌词匹配页打开时带的歌曲信息（详情页已有这些字段，不必再查一次库）。 */
data class LyricsMatchTarget(
    val songId: Int,
    val title: String,
    val artist: String = "",
    val album: String = "",
    val durationMs: Long = 0L,
    val filePath: String = "",
)

/** 一个候选项在界面上的展示形态 */
data class CandidateUi(
    val index: Int,
    val sourceName: String,
    val title: String,
    val artist: String,
    val album: String,
    val durationMs: Long,
    val synced: Boolean,
    val wordByWord: Boolean,
    val score: Double,
    val lineCount: Int,
)

data class LyricsMatchUiState(
    val target: LyricsMatchTarget? = null,
    val queryTitle: String = "",
    val queryArtist: String = "",
    val searching: Boolean = false,
    val writing: Boolean = false,
    val candidates: List<CandidateUi> = emptyList(),
    val selectedIndex: Int = -1,
    val preview: String = "",
    val format: LyricoLyricsFormat = LyricoLyricsFormat.PLAIN,
    /** 一次性提示（写入成功/失败），界面消费后清空 */
    val message: String? = null,
    val sourceErrors: List<String> = emptyList(),
)

@HiltViewModel
class LyricsMatchViewModel @Inject constructor(
    private val searchService: LyricsSearchService,
    private val writer: LyricsWriter,
    private val settingsStore: LyricoSettingsStore,
) : ViewModel() {

    private val _state = MutableStateFlow(LyricsMatchUiState())
    val state: StateFlow<LyricsMatchUiState> = _state.asStateFlow()

    /** 原始候选（含完整歌词），只在这里保存，界面拿不到结构化数据也不能改它 */
    private var rawCandidates: List<LyricsCandidate> = emptyList()

    fun prepare(target: LyricsMatchTarget) {
        if (_state.value.target?.songId == target.songId) return
        rawCandidates = emptyList()
        _state.value = LyricsMatchUiState(
            target = target,
            queryTitle = target.title,
            queryArtist = target.artist,
        )
    }

    fun setQueryTitle(value: String) = _state.update { it.copy(queryTitle = value) }

    fun setQueryArtist(value: String) = _state.update { it.copy(queryArtist = value) }

    fun setFormat(format: LyricoLyricsFormat) {
        _state.update { it.copy(format = format) }
        refreshPreview()
    }

    fun search() {
        val s = _state.value
        val target = s.target ?: return
        if (s.searching) return
        val query = LyricsTarget(
            title = s.queryTitle.trim().ifBlank { target.title },
            artist = s.queryArtist.trim(),
            album = target.album,
            durationMs = target.durationMs,
        )
        viewModelScope.launch {
            _state.update { it.copy(searching = true, message = null, sourceErrors = emptyList()) }
            val result = runCatching { searchService.search(query) }
            result.onSuccess { r ->
                rawCandidates = r.candidates
                _state.update {
                    it.copy(
                        searching = false,
                        candidates = r.candidates.mapIndexed { index, c -> c.toUi(index) },
                        selectedIndex = if (r.candidates.isEmpty()) -1 else 0,
                        sourceErrors = r.errors,
                    )
                }
                refreshPreview()
            }.onFailure { e ->
                _state.update {
                    it.copy(searching = false, message = "搜索失败：${e.message}", candidates = emptyList())
                }
            }
        }
    }

    fun select(index: Int) {
        _state.update { it.copy(selectedIndex = index) }
        refreshPreview()
    }

    fun consumeMessage() = _state.update { it.copy(message = null) }

    /** 把选中的候选写入音频内嵌标签。 */
    fun write() {
        val s = _state.value
        val target = s.target ?: return
        val candidate = rawCandidates.getOrNull(s.selectedIndex) ?: return
        if (s.writing) return

        val converted = LyricoToLyrics.convert(candidate.lyrics, fallbackTitle = target.title) ?: run {
            _state.update { it.copy(message = "该候选没有可用歌词") }
            return
        }
        val lyrics = LyricsRuleEngine.applyTo(converted, settingsStore.current())
        val song = Song(
            id = target.songId,
            title = target.title,
            artist = target.artist,
            album = target.album,
            durationSeconds = (target.durationMs / 1000).toInt(),
            filePath = target.filePath,
        )
        viewModelScope.launch {
            _state.update { it.copy(writing = true, message = null) }
            val outcome = runCatching { writer.write(song, lyrics, s.format) }
                .getOrElse { LyricsWriteOutcome.Failed(it.message ?: it.javaClass.simpleName) }
            val message = when (outcome) {
                is LyricsWriteOutcome.Success -> "已写入歌词（${outcome.format.label}）"
                is LyricsWriteOutcome.Unsupported -> "无法写入：${outcome.reason}"
                is LyricsWriteOutcome.Failed -> "写入失败：${outcome.message}"
            }
            _state.update { it.copy(writing = false, message = message) }
        }
    }

    private fun refreshPreview() {
        val s = _state.value
        val candidate = rawCandidates.getOrNull(s.selectedIndex)
        val lyrics = candidate?.let { LyricoToLyrics.convert(it.lyrics, fallbackTitle = s.target?.title) }
        // 预览与写入必须走同一套规则：否则「预览干净、写进去带制作人员行」
        val normalized = lyrics?.let { LyricsRuleEngine.applyTo(it, settingsStore.current()) }
        val preview = when {
            normalized == null -> ""
            // 预览用当前选中格式，所见即所写
            else -> LyricoLyricsEncoder.encode(normalized, s.format)
        }
        _state.update { it.copy(preview = preview) }
    }

    private fun LyricsCandidate.toUi(index: Int): CandidateUi {
        val lyrics = LyricoToLyrics.convert(this.lyrics, fallbackTitle = trackTitle)
        return CandidateUi(
            index = index,
            sourceName = sourceName,
            title = trackTitle,
            artist = artist,
            album = album,
            durationMs = durationMs,
            synced = isSynced,
            wordByWord = lyrics?.lines?.any { !it.wordTimestamps.isNullOrEmpty() } ?: false,
            score = score,
            lineCount = lyrics?.lines?.size ?: 0,
        )
    }
}
