package com.catclaw.music.kt.ui.playback

import android.content.Context
import android.content.Intent
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.data.lyrics.DesktopLyricSettings
import com.catclaw.music.kt.data.lyrics.DesktopLyricStatus
import com.catclaw.music.kt.data.lyrics.OverlayPermission
import com.catclaw.music.kt.data.library.LibraryScanner
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

@HiltViewModel
class PlayingViewModel @Inject constructor(
    @ApplicationContext private val context: Context,
    private val controller: PlayerController,
    private val scanner: LibraryScanner,
    private val libraryDao: LibraryDao,
    private val desktopLyric: DesktopLyricSettings,
    private val lyricStatus: DesktopLyricStatus,
    private val session: PlaybackSessionStarter,
) : ViewModel() {

    /** 标签要说的是**实况**：意图为开 **且** 窗口真的在屏幕上 */
    val desktopLyricEnabled: StateFlow<Boolean> =
        combine(desktopLyric.enabled, lyricStatus.attached) { wanted, attached -> wanted && attached }
            .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), false)

    init {
        // 播放器与队列恢复都必须在主线程发生（ExoPlayer 的线程约束）
        controller.start()
    }

    val state = controller.state

    /** 睡眠定时的实况（剩余秒、是否在等曲目播完）。倒计时与音量淡出都在播放器那一侧走，
     *  页面只是读它——关掉页面定时也该继续。 */
    val sleep = controller.sleep

    fun startSleepTimer(minutes: Int, stopAfterSong: Boolean, fadeOut: Boolean) =
        controller.startSleepTimer(minutes, stopAfterSong, fadeOut)

    fun cancelSleepTimer() = controller.cancelSleepTimer()

    val queueSongs = controller.queueSongs.stateIn(
        scope = viewModelScope,
        started = SharingStarted.WhileSubscribed(5_000),
        initialValue = emptyList(),
    )

    val positionMs: StateFlow<Long> = controller.positionMsFlow()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(2_000), 0L)

    private val _busy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = _busy

    private val _lastScan = MutableStateFlow<ScanCounts?>(null)
    val lastScan: StateFlow<ScanCounts?> = _lastScan

    /** 扫描结果只带计数，文案由资源出（多语言不能在 VM 里拼中文） */
    data class ScanCounts(val scanned: Int, val inserted: Int, val removed: Int)

    /** 扫描入库后，把曲库整表入队并从第一首开始播 */
    fun scanAndPlayAll() {
        if (_busy.value) return
        viewModelScope.launch {
            _busy.value = true
            try {
                val result = kotlinx.coroutines.withContext(Dispatchers.IO) { scanner.scanFull() }
                val songs = libraryDao.observeLocalSongs().first().map { it.toSong() }
                _lastScan.value = ScanCounts(result.scanned, result.inserted + result.updated, result.removed)
                if (songs.isNotEmpty()) {
                    ensureSessionService()
                    controller.playSongs(songs, startIndex = 0)
                }
            } finally {
                _busy.value = false
            }
        }
    }

    fun playSongs(songs: List<com.catclaw.music.kt.core.model.Song>, startIndex: Int) {
        if (songs.isEmpty()) return
        ensureSessionService()
        controller.playSongs(songs, startIndex)
    }

    fun playAt(index: Int) {
        ensureSessionService()
        controller.playSongAt(index)
    }

    /** 冷启动后队列会恢复，此时按「播放」只是就地 resume，不走 playSongs/playAt —— 少了这一步，通知栏、媒体会话、桌面歌词宿主都不会起来 */
    fun toggle() {
        ensureSessionService()
        controller.togglePlayPause()
    }

    /** 上一曲/下一曲同样能直接把声音放出去（`moveToCurrent` 里有 `player.play()`），所以和 toggle 一样要先拉会话 */
    fun next() {
        ensureSessionService()
        controller.next()
    }

    fun previous() {
        ensureSessionService()
        controller.previous()
    }

    fun cycleMode() = controller.cycleMode()

    fun seekTo(ms: Long) = controller.seekTo(ms)

    fun removeAt(songId: Int) = controller.removeFromQueue(songId)

    /** @return 是否已授予悬浮窗权限；未授予时调用方应去设置页申请 */
    fun toggleDesktopLyric(): Boolean {
        if (desktopLyric.enabled.value) {
            desktopLyric.setEnabled(false)
            return true
        }
        if (!OverlayPermission.granted(context)) return false
        desktopLyric.setEnabled(true)
        return true
    }

    fun overlayPermissionIntent(): Intent = OverlayPermission.requestIntent(context)

    private fun ensureSessionService() = session.ensure()
}
