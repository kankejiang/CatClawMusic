package com.catclaw.music.kt.lyrico.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.lyrico.plugin.PluginEntry
import com.catclaw.music.kt.lyrico.plugin.PluginInstallOutcome
import com.catclaw.music.kt.lyrico.plugin.PluginRegistry
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class SourcePluginUiState(
    val loading: Boolean = true,
    val plugins: List<PluginEntry> = emptyList(),
    val busy: Boolean = false,
    val message: String? = null,
)

@HiltViewModel
class SourcePluginViewModel @Inject constructor(
    private val registry: PluginRegistry,
) : ViewModel() {

    private val _state = MutableStateFlow(SourcePluginUiState())
    val state: StateFlow<SourcePluginUiState> = _state.asStateFlow()

    init {
        refresh()
    }

    fun refresh() {
        viewModelScope.launch {
            _state.update { it.copy(loading = true) }
            val plugins = runCatching { registry.refresh() }.getOrDefault(emptyList())
            _state.update { it.copy(loading = false, plugins = plugins) }
        }
    }

    fun setEnabled(pluginId: String, enabled: Boolean) {
        viewModelScope.launch {
            registry.setEnabled(pluginId, enabled)
            _state.update { s ->
                s.copy(
                    plugins = s.plugins.map {
                        if (it.pluginId == pluginId) it.copy(enabled = enabled) else it
                    },
                )
            }
        }
    }

    fun uninstall(pluginId: String, bundled: Boolean) {
        if (bundled) {
            _state.update { it.copy(message = "内置源不能卸载，只能停用") }
            return
        }
        viewModelScope.launch {
            _state.update { it.copy(busy = true) }
            val outcome = registry.uninstall(pluginId, bundled)
            val msg = when (outcome) {
                is PluginInstallOutcome.Success -> "已卸载"
                is PluginInstallOutcome.Invalid -> outcome.reason
                is PluginInstallOutcome.Rejected -> outcome.reason
                is PluginInstallOutcome.Failed -> "卸载失败：${outcome.message}"
            }
            refresh()
            _state.update { it.copy(busy = false, message = msg) }
        }
    }

    /** 从外部读到的 ZIP 字节安装（文件选择器的回调最终走到这里）。 */
    fun installZip(bytes: ByteArray) {
        viewModelScope.launch {
            _state.update { it.copy(busy = true, message = null) }
            val outcome = registry.install(bytes)
            val msg = when (outcome) {
                is PluginInstallOutcome.Success ->
                    "已安装 ${outcome.displayName}${outcome.versionName.takeIf { v -> v.isNotBlank() }?.let { " v$it" } ?: ""}"
                is PluginInstallOutcome.Invalid -> "插件无效：${outcome.reason}"
                is PluginInstallOutcome.Rejected -> "已拒绝：${outcome.reason}"
                is PluginInstallOutcome.Failed -> "安装失败：${outcome.message}"
            }
            refresh()
            _state.update { it.copy(busy = false, message = msg) }
        }
    }

    fun consumeMessage() = _state.update { it.copy(message = null) }
}
