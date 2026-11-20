package com.catclaw.music.kt.lyrico.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.lyrico.batch.BatchOperation
import com.catclaw.music.kt.lyrico.batch.BatchOptions
import com.catclaw.music.kt.lyrico.batch.BatchRunner
import com.catclaw.music.kt.lyrico.batch.SelectedSong
import com.catclaw.music.kt.lyrico.db.BatchStatus
import com.catclaw.music.kt.lyrico.db.BatchTaskEntity
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class BatchSongItem(
    val id: Int,
    val title: String,
    val artist: String,
    val album: String,
    val filePath: String,
    val remote: Boolean,
)

data class BatchUiState(
    val loading: Boolean = true,
    val allSongs: List<BatchSongItem> = emptyList(),
    val query: String = "",
    val selectedIds: Set<Int> = emptySet(),
    /** null 表示还没选操作 */
    val operation: BatchOperation? = null,
    val options: BatchOptions = BatchOptions(),
    val busy: Boolean = false,
    val message: String? = null,
    val tasks: List<BatchTaskEntity> = emptyList(),
    val runningTaskId: Long? = null,
) {
    /** 过滤后的列表（搜索只影响展示，不影响已选集合） */
    val visibleSongs: List<BatchSongItem>
        get() = if (query.isBlank()) {
            allSongs
        } else {
            val q = query.trim()
            allSongs.filter {
                it.title.contains(q, ignoreCase = true) ||
                    it.artist.contains(q, ignoreCase = true) ||
                    it.album.contains(q, ignoreCase = true)
            }
        }
}

@HiltViewModel
class BatchViewModel @Inject constructor(
    private val libraryDao: LibraryDao,
    private val runner: BatchRunner,
) : ViewModel() {

    private val _state = MutableStateFlow(BatchUiState())
    val state: StateFlow<BatchUiState> = _state.asStateFlow()

    init {
        viewModelScope.launch {
            // 断点恢复：上次被杀掉的任务标失败，未处理条目留 pending
            runCatching { runner.recoverInterrupted() }
        }
        viewModelScope.launch {
            runCatching { libraryDao.observeAllSongs().collect { rows ->
                _state.update { state ->
                    state.copy(
                        loading = false,
                        allSongs = rows.map { row ->
                            BatchSongItem(
                                id = row.id,
                                title = row.title,
                                artist = row.artist,
                                album = row.album,
                                filePath = row.filePath,
                                remote = row.source != 0 || row.filePath.startsWith("http", true),
                            )
                        },
                    )
                }
            } }
        }
        viewModelScope.launch {
            runCatching { runner.observeTasks().collect { tasks ->
                _state.update { it.copy(tasks = tasks) }
            } }
        }
    }

    fun setQuery(value: String) = _state.update { it.copy(query = value) }

    fun toggle(id: Int) = _state.update {
        val next = if (id in it.selectedIds) it.selectedIds - id else it.selectedIds + id
        it.copy(selectedIds = next)
    }

    fun selectAllVisible() = _state.update {
        it.copy(selectedIds = it.selectedIds + it.visibleSongs.map { s -> s.id })
    }

    fun clearSelection() = _state.update { it.copy(selectedIds = emptySet()) }

    fun chooseOperation(operation: BatchOperation) = _state.update {
        it.copy(
            operation = operation,
            // ReplayGain 未移植：直接给提示，避免用户白等
            message = if (operation == BatchOperation.REPLAY_GAIN) {
                "ReplayGain 需要 ffmpeg + ebur128，当前版本未移植，任务会全部跳过"
            } else {
                it.message
            },
        )
    }

    fun updateOptions(transform: (BatchOptions) -> BatchOptions) =
        _state.update { it.copy(options = transform(it.options)) }

    fun consumeMessage() = _state.update { it.copy(message = null) }

    /** 创建并立即执行任务。 */
    fun start() {
        val s = _state.value
        val operation = s.operation ?: return
        if (s.busy) return
        val selected = s.allSongs.filter { it.id in s.selectedIds }
        if (selected.isEmpty()) {
            _state.update { it.copy(message = "先选几首歌") }
            return
        }
        viewModelScope.launch {
            _state.update { it.copy(busy = true, message = null) }
            val songs = selected.map {
                SelectedSong(
                    id = it.id,
                    title = it.title,
                    artist = it.artist,
                    album = it.album,
                    filePath = it.filePath,
                )
            }
            val taskId = runCatching { runner.createTask(operation, s.options, songs, "") }
                .getOrElse { e ->
                    _state.update { it.copy(busy = false, message = "创建任务失败：${e.message}") }
                    return@launch
                }
            _state.update { it.copy(runningTaskId = taskId, busy = false) }
            val status = runCatching { runner.run(taskId) }.getOrElse { BatchStatus.FAILED }
            _state.update {
                it.copy(
                    runningTaskId = null,
                    message = when (status) {
                        BatchStatus.COMPLETED -> "任务完成"
                        BatchStatus.CANCELLED -> "任务已取消"
                        BatchStatus.FAILED -> "任务失败（详情里看每首歌的原因）"
                        else -> "任务结束：$status"
                    },
                    selectedIds = if (status == BatchStatus.COMPLETED) emptySet() else it.selectedIds,
                )
            }
        }
    }

    fun cancelRunning() {
        val id = _state.value.runningTaskId ?: return
        runner.cancel(id)
        _state.update { it.copy(message = "正在取消…") }
    }

    fun deleteTask(taskId: Long) {
        viewModelScope.launch {
            runCatching { runner.deleteTask(taskId) }
        }
    }

    fun clearFinished() {
        viewModelScope.launch {
            val removed = runCatching { runner.clearFinished() }.getOrDefault(0)
            _state.update { it.copy(message = "已清理 $removed 条已完成任务") }
        }
    }

    fun observeItems(taskId: Long) = runner.observeItems(taskId)
    fun observeTask(taskId: Long) = runner.observeTask(taskId)
}
