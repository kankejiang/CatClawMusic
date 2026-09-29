package com.catclaw.music.kt.ui.settings

import android.net.Uri
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.data.library.LibraryScanner
import com.catclaw.music.kt.data.library.ScanResult
import com.catclaw.music.kt.data.settings.LocalScanSettings
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/**
 * 本地音乐设置的读写层。
 *
 * 设置值不放在 StateFlow 里当唯一事实来源：`LocalScanSettings`（SharedPreferences）才是。
 * 这里只是把它的当前值镜像成一份可组合的状态，改完立刻回读一次 ——
 * 与通用设置/外观页同一条规则，避免"页面上显示的是内存里那份"。
 */
@HiltViewModel
class LocalMusicSettingsViewModel @Inject constructor(
    private val settings: LocalScanSettings,
    private val scanner: LibraryScanner,
) : ViewModel() {

    data class View(
        val useMediaStore: Boolean = true,
        val useSafScan: Boolean = false,
        val folders: List<String> = emptyList(),
        val scanning: Boolean = false,
        val lastResult: ScanResult? = null,
        /** 一次添加被拒的原因：0 = 没有，1 = 清单里已经有了 */
        val duplicateFolder: Boolean = false,
    )

    private val _view = MutableStateFlow(View())
    val view: StateFlow<View> = _view.asStateFlow()

    init {
        reload()
    }

    fun reload() {
        _view.update {
            it.copy(
                useMediaStore = settings.useMediaStore,
                useSafScan = settings.useSafScan,
                folders = settings.folders(),
            )
        }
    }

    fun setMediaStore(on: Boolean) {
        settings.useMediaStore = on
        reload()
    }

    fun setSafScan(on: Boolean) {
        settings.useSafScan = on
        reload()
    }

    /** 记住一个 SAF 文件夹（持久授权在 store 里一起要到手） */
    fun addFolder(uri: Uri) {
        val added = settings.addFolder(uri)
        _view.update { it.copy(duplicateFolder = !added) }
        reload()
    }

    fun removeFolder(uri: String) {
        settings.removeFolder(uri)
        reload()
    }

    /** 两种开关全关时不发扫描：那是一次注定 0 结果的空跑，只会让人以为扫描坏了 */
    fun scan() {
        val state = _view.value
        if (state.scanning || !(state.useMediaStore || state.useSafScan)) return
        viewModelScope.launch {
            _view.update { it.copy(scanning = true) }
            val result = runCatching { scanner.scanFull() }.getOrNull()
            _view.update { it.copy(scanning = false, lastResult = result ?: it.lastResult) }
        }
    }
}

/** 树 URI → 给人看的文件夹名（`…/tree/primary%3AMusic` → `Music`） */
internal fun folderLabel(uri: String): String {
    val tail = runCatching { Uri.parse(uri).lastPathSegment }.getOrNull().orEmpty()
    return tail.substringAfter(':').ifBlank { tail.ifBlank { uri } }
}
