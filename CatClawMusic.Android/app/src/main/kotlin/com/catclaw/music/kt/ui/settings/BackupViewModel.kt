package com.catclaw.music.kt.ui.settings

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.data.backup.BackupService
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import android.content.Context
import java.io.File
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

data class BackupUiState(
    val busy: Boolean = false,
    val percent: Int = 0,
    /** 成功时是文件名，失败时是原因；界面直接显示，不再另造一套文案 */
    val message: String? = null,
)

@HiltViewModel
class BackupViewModel @Inject constructor(
    @ApplicationContext private val context: Context,
    private val service: BackupService,
) : ViewModel() {

    private val _state = MutableStateFlow(BackupUiState())
    val state = _state.asStateFlow()

    /**
     * 备份落在应用专属的外部目录（`Android/data/<包>/files/CatClawMusic`）：
     * 用户用文件管理器就能拿到，且不需要 `MANAGE_EXTERNAL_STORAGE` 这种全机授权。
     * C# 那边写的是外部存储根，那是 .NET 侧已有权限模型，安卓上照抄只会换来一个权限弹窗死路。
     */
    fun backupRoot(): File = context.getExternalFilesDir(null) ?: context.filesDir

    fun backups(): List<File> = BackupService.list(backupRoot())

    fun createBackup() = runJob { root -> service.backup(root, onProgress = ::publish).name }

    fun restore(file: File) = runJob { root -> service.restore(file, onProgress = ::publish).let { file.name } }

    fun dismissMessage() {
        _state.value = _state.value.copy(busy = false, percent = 0, message = null)
    }

    private fun publish(progress: com.catclaw.music.kt.data.backup.BackupProgress) {
        _state.value = _state.value.copy(busy = true, percent = progress.percent, message = progress.message)
    }

    private fun runJob(work: suspend (File) -> String) {
        if (_state.value.busy) return
        _state.value = BackupUiState(busy = true)
        viewModelScope.launch {
            runCatching { withContext(Dispatchers.IO) { work(backupRoot()) } }
                .onSuccess { _state.value = _state.value.copy(busy = false, percent = 100, message = it) }
                // 失败必须把原因说出来：备份/恢复出问题时"没反应"比报错更难查
                .onFailure { _state.value = _state.value.copy(busy = false, message = it.message ?: it.javaClass.simpleName) }
        }
    }
}
