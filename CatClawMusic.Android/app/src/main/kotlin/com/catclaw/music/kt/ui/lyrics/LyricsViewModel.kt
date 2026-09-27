package com.catclaw.music.kt.ui.lyrics

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.lyrics.LyricPresentation
import com.catclaw.music.kt.core.lyrics.Lyrics
import com.catclaw.music.kt.core.lyrics.LyricsState
import com.catclaw.music.kt.core.lyrics.withoutEmptyLines
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.lyrics.LyricsReader
import com.catclaw.music.kt.data.lyrics.LyricsSettings
import com.catclaw.music.kt.data.lyrics.LyricsSourceMode
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

@HiltViewModel
class LyricsViewModel @Inject constructor(
    private val controller: PlayerController,
    private val reader: LyricsReader,
    private val settings: LyricsSettings,
) : ViewModel() {

    val positionMs: StateFlow<Long> = controller.positionMsFlow(120)
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(3_000), 0L)

    private val _lyrics = MutableStateFlow<Lyrics?>(null)
    val lyrics: StateFlow<Lyrics?> = _lyrics

    private val _loading = MutableStateFlow(false)
    val loading: StateFlow<Boolean> = _loading

    /** 显示设置全部落在 LyricsSettings（键与默认值对齐 MAUI），这里只做转发 */
    val lineMode: StateFlow<Boolean> = settings.lineMode
    val alignment: StateFlow<Int> = settings.alignment
    val fontSizeSp: StateFlow<Float> = settings.fontSizeSp
    val removeEmptyLines: StateFlow<Boolean> = settings.removeEmptyLines
    val showTranslation: StateFlow<Boolean> = settings.showTranslation
    val showRoma: StateFlow<Boolean> = settings.showRoma
    val sourceMode: StateFlow<LyricsSourceMode> = settings.sourceMode

    private var loadedSongId: Int? = null

    init {
        viewModelScope.launch {
            // 换来源档位要重走一遍取词顺序（侧车优先 / 只看内嵌 / 只看外挂）
            settings.sourceMode.drop(1).distinctUntilChanged().collect {
                val song = controller.state.value.currentSong ?: return@collect
                loadedSongId = null
                _lyrics.value = runCatching { reader.loadFor(song, it) }.getOrNull()
                loadedSongId = song.id
            }
        }
        viewModelScope.launch {
            controller.state.map { it.currentSong }.distinctUntilChanged().collect { song ->
                if (song == null) {
                    _lyrics.value = null
                    loadedSongId = null
                    return@collect
                }
                if (song.id == loadedSongId) return@collect
                loadedSongId = song.id
                _loading.value = true
                _lyrics.value = runCatching { reader.loadFor(song, settings.sourceMode.value) }.getOrNull()
                _loading.value = false
            }
        }
    }

    /** 按「删除空行」档位过滤后的歌词；索引仍按原表算，过滤只影响展示列表 */
    fun visibleLyrics(): Lyrics? {
        val lyrics = _lyrics.value ?: return null
        return if (settings.removeEmptyLines.value) lyrics.withoutEmptyLines() else lyrics
    }

    fun presentation(): LyricPresentation? =
        LyricsState.present(visibleLyrics(), positionMs.value, settings.lineMode.value)

    fun toggleLineMode() = settings.setLineMode(!settings.lineMode.value)

    fun setAlignment(value: Int) = settings.setAlignment(value)

    fun setFontSize(value: Float) = settings.setFontSize(value)

    fun setRemoveEmptyLines(value: Boolean) = settings.setRemoveEmptyLines(value)

    fun setShowTranslation(value: Boolean) = settings.setShowTranslation(value)

    fun setShowRoma(value: Boolean) = settings.setShowRoma(value)

    fun setSourceMode(value: LyricsSourceMode) = settings.setSourceMode(value)

    fun seekTo(ms: Long) = controller.seekTo(ms)

    fun currentSongTitle(): String = controller.state.value.currentSong?.title.orEmpty()

    fun currentSongArtist(): String = controller.state.value.currentSong?.artist.orEmpty()
}
