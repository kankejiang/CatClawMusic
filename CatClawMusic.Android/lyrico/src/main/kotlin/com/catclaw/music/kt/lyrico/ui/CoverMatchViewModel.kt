package com.catclaw.music.kt.lyrico.ui

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.lyrico.search.CoverCandidate
import com.catclaw.music.kt.lyrico.search.CoverSearchService
import com.catclaw.music.kt.lyrico.search.CoverWriteOutcome
import com.catclaw.music.kt.lyrico.search.CoverWriter
import com.catclaw.music.kt.lyrico.search.LyricsTarget
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

data class CoverMatchUiState(
    val target: LyricsMatchTarget? = null,
    val searching: Boolean = false,
    val writing: Boolean = false,
    val covers: List<CoverCandidate> = emptyList(),
    /** url -> 缩略图（懒加载，失败为 null） */
    val thumbs: Map<String, Bitmap?> = emptyMap(),
    val selectedUrl: String? = null,
    val message: String? = null,
)

@HiltViewModel
class CoverMatchViewModel @Inject constructor(
    private val coverSearch: CoverSearchService,
    private val coverWriter: CoverWriter,
) : ViewModel() {

    private val _state = MutableStateFlow(CoverMatchUiState())
    val state: StateFlow<CoverMatchUiState> = _state.asStateFlow()

    fun prepare(target: LyricsMatchTarget) {
        if (_state.value.target?.songId == target.songId) return
        _state.value = CoverMatchUiState(target = target)
    }

    fun search() {
        val target = _state.value.target ?: return
        if (_state.value.searching) return
        viewModelScope.launch {
            _state.update { it.copy(searching = true, message = null, covers = emptyList(), thumbs = emptyMap()) }
            val query = LyricsTarget(target.title, target.artist, target.album, target.durationMs)
            val covers = runCatching { coverSearch.search(query) }.getOrDefault(emptyList())
            _state.update {
                it.copy(
                    searching = false,
                    covers = covers,
                    selectedUrl = covers.firstOrNull()?.url,
                )
            }
            // 缩略图按顺序加载，避免一次性并发几十个请求
            covers.take(24).forEach { c -> loadThumb(c.url) }
        }
    }

    fun select(url: String) = _state.update { it.copy(selectedUrl = url) }

    fun consumeMessage() = _state.update { it.copy(message = null) }

    fun write() {
        val s = _state.value
        val target = s.target ?: return
        val cover = s.covers.firstOrNull { it.url == s.selectedUrl } ?: run {
            _state.update { it.copy(message = "请先选一张封面") }
            return
        }
        if (s.writing) return
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
            val outcome = runCatching { coverWriter.write(song, cover) }
                .getOrElse { CoverWriteOutcome.Failed(it.message ?: it.javaClass.simpleName) }
            val msg = when (outcome) {
                is CoverWriteOutcome.Success -> "已写入封面（${outcome.bytes / 1024} KB）"
                is CoverWriteOutcome.Unsupported -> "无法写入：${outcome.reason}"
                is CoverWriteOutcome.Failed -> "写入失败：${outcome.message}"
            }
            _state.update { it.copy(writing = false, message = msg) }
        }
    }

    private fun loadThumb(url: String) {
        if (_state.value.thumbs.containsKey(url)) return
        viewModelScope.launch {
            val bitmap = withContext(Dispatchers.IO) {
                val bytes = coverSearch.download(url) ?: return@withContext null
                decodeSampled(bytes, targetPx = 320)
            }
            _state.update { it.copy(thumbs = it.thumbs + (url to bitmap)) }
        }
    }

    private fun decodeSampled(bytes: ByteArray, targetPx: Int): Bitmap? = runCatching {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
        var sample = 1
        while (bounds.outWidth / sample > targetPx && sample < 8) sample *= 2
        val opts = BitmapFactory.Options().apply { inSampleSize = sample }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, opts)
    }.getOrNull()
}
