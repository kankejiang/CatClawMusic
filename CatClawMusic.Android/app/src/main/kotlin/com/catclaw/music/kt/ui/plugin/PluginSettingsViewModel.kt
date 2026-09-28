package com.catclaw.music.kt.ui.plugin

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.plugin.pluginRegistry
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import java.io.File
import javax.inject.Inject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/** 插件目录：应用私有外部目录下的 plugins/，用户可用文件管理器直接放/取 */
internal fun pluginsRoot(context: Context): File =
    context.getExternalFilesDir("plugins") ?: File(context.filesDir, "plugins")

/**
 * 插件协议 v2 的第一个消费者：把 `pluginRegistry(...)` 扫出来的条目映射成管理页的行。
 *
 * 之前这套契约 + 传输只有 core 内部互相引用，随包发布却没人调用；这个 VM 就是接上的那一环。
 */
@HiltViewModel
class PluginSettingsViewModel @Inject constructor(
    @ApplicationContext private val context: Context,
) : ViewModel() {

    private val _rows = MutableStateFlow<List<PluginRow>>(emptyList())
    val rows: StateFlow<List<PluginRow>> = _rows.asStateFlow()

    init {
        rescan()
    }

    fun rescan() {
        viewModelScope.launch(Dispatchers.IO) {
            val registry = pluginRegistry(
                root = pluginsRoot(context),
                hostPlatform = HOST_PLATFORM,
                hostVersion = hostVersion(),
            )
            _rows.value = pluginRows(registry.scan())
        }
    }

    fun directoryPath(): String = pluginsRoot(context).absolutePath

    private fun hostVersion(): String = runCatching {
        @Suppress("DEPRECATION")
        context.packageManager.getPackageInfo(context.packageName, 0).versionName
    }.getOrNull().orEmpty()

    private companion object {
        /** 与宿主市场 index.json 的取值一致，避免再造同义词 */
        const val HOST_PLATFORM = "android"
    }
}
