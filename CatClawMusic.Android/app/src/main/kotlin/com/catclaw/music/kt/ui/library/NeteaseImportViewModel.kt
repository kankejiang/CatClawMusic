package com.catclaw.music.kt.ui.library

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.data.netease.NeteasePlaylistImporter
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/**
 * 导入网易云歌单的状态机。
 *
 * 放在 ViewModel 而不是 Compose 的 `remember` 里，是因为**导入不能跟着面板一起消失**：
 * 用户点完开始导入很可能顺手把面板关掉去干别的，500 首的写入还在跑。
 * 状态留在 VM 里，重新打开面板能看到进度与结果。
 */
@HiltViewModel
class NeteaseImportViewModel @Inject constructor(
    private val importer: NeteasePlaylistImporter,
) : ViewModel() {

    sealed interface State {
        data object Idle : State
        data class Running(val done: Int, val total: Int) : State
        data class Done(val result: NeteasePlaylistImporter.Result) : State
        data class Failed(val reason: NeteasePlaylistImporter.Reason) : State
    }

    private val _state = MutableStateFlow<State>(State.Idle)
    val state: StateFlow<State> = _state.asStateFlow()

    fun start(raw: String) {
        if (_state.value is State.Running) return
        viewModelScope.launch {
            _state.value = State.Running(0, 0)
            runCatching {
                importer.import(raw) { done, total ->
                    _state.value = State.Running(done, total)
                }
            }.onSuccess { result ->
                _state.value = State.Done(result)
            }.onFailure { error ->
                val reason = (error as? NeteasePlaylistImporter.ImportException)?.reason
                    ?: NeteasePlaylistImporter.Reason.FETCH_FAILED
                _state.value = State.Failed(reason)
            }
        }
    }

    /** 关掉面板后再打开时回到输入态：上一轮的结果已经落地，没必要一直占着屏幕 */
    fun reset() {
        if (_state.value is State.Running) return
        _state.value = State.Idle
    }
}
