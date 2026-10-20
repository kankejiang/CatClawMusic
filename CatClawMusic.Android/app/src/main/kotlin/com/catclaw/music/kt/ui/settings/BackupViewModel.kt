package com.catclaw.music.kt.ui.settings

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.R
import com.catclaw.music.kt.data.backup.BackupItems
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

/**
 * 一条可勾选的备份内容。位标志与 [BackupItems] 同源，顺序照它的位序排 ——
 * 以后新增一类数据，这里加一行就够了。
 */
data class BackupItemOption(val flag: Int, @androidx.annotation.StringRes val labelRes: Int)

/**
 * 八类可勾选内容。C# 的选项对话框只摆了其中四项（聊天 / 记忆 / 模型 / 播放记录），
 * 另外四项没有入口 —— 于是那边的"选择性备份"会把歌单与收藏悄悄漏掉。
 * 本端把服务层支持的八项全摆出来，勾选结果就是真正写进/读出备份的那个位掩码。
 */
val BACKUP_ITEM_OPTIONS: List<BackupItemOption> = listOf(
    BackupItemOption(BackupItems.PLAYLISTS, R.string.backup_item_playlists),
    BackupItemOption(BackupItems.PLAY_HISTORY, R.string.backup_item_history),
    BackupItemOption(BackupItems.FAVORITES, R.string.backup_item_favorites),
    BackupItemOption(BackupItems.ARTISTS, R.string.backup_item_artists),
    BackupItemOption(BackupItems.ARTIST_COVERS, R.string.backup_item_covers),
    BackupItemOption(BackupItems.LLM_CONFIGS, R.string.backup_item_llm),
    BackupItemOption(BackupItems.CHAT_HISTORY, R.string.backup_item_chat),
    BackupItemOption(BackupItems.AI_MEMORY, R.string.backup_item_memory),
)

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

    /** 勾选的备份内容，默认全选（与 C# 默认一致）。备份与恢复共用这一份勾选 */
    private val _items = MutableStateFlow(BackupItems.ALL)
    val items = _items.asStateFlow()

    fun toggleItem(flag: Int) {
        _items.value = if (_items.value and flag != 0) _items.value and flag.inv() else _items.value or flag
    }

    fun selectAllItems() {
        _items.value = BackupItems.ALL
    }

    /**
     * 备份落在应用专属的外部目录（`Android/data/<包>/files/CatClawMusic`）：
     * 用户用文件管理器就能拿到，且不需要 `MANAGE_EXTERNAL_STORAGE` 这种全机授权。
     * C# 那边写的是外部存储根，那是 .NET 侧已有权限模型，安卓上照抄只会换来一个权限弹窗死路。
     */
    fun backupRoot(): File = context.getExternalFilesDir(null) ?: context.filesDir

    fun backups(): List<File> = BackupService.list(backupRoot())

    // 一项都没勾是空跑：拒绝执行，而不是生成一个只有 backup.json 的空包
    // （界面上那两颗按钮同时也是灰的，这道闸门是兜底）
    fun createBackup() {
        if (_items.value == BackupItems.NONE) return
        runJob {
            val name = service.backup(it, items = _items.value, onProgress = ::publish).name
            context.getString(R.string.backup_created, name)
        }
    }

    fun restore(file: File) {
        if (_items.value == BackupItems.NONE) return
        runJob {
            service.restore(file, items = _items.value, onProgress = ::publish)
            context.getString(R.string.backup_restored, file.name)
        }
    }

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
