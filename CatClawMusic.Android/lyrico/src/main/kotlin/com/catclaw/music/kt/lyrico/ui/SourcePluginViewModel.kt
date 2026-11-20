package com.catclaw.music.kt.lyrico.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.lyrico.plugin.CatalogPlugin
import com.catclaw.music.kt.lyrico.plugin.CatalogResult
import com.catclaw.music.kt.lyrico.plugin.PluginCatalogClient
import com.catclaw.music.kt.lyrico.plugin.PluginDownloadOutcome
import com.catclaw.music.kt.lyrico.plugin.PluginEntry
import com.catclaw.music.kt.lyrico.plugin.PluginUpdateChecker
import com.catclaw.music.kt.lyrico.plugin.PluginUpdateStatus
import com.catclaw.music.kt.lyrico.plugin.PluginInstallOutcome
import com.catclaw.music.kt.lyrico.plugin.PluginRegistry
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class OnlinePluginItem(
    val plugin: CatalogPlugin,
    val status: PluginUpdateStatus,
)

data class SourcePluginUiState(
    val loading: Boolean = true,
    val plugins: List<PluginEntry> = emptyList(),
    val busy: Boolean = false,
    val message: String? = null,
    /** 在线目录：首次进入不自动拉，用户点「检查更新」才请求 */
    val online: List<OnlinePluginItem> = emptyList(),
    val onlineLoading: Boolean = false,
    val onlineError: String? = null,
)

@HiltViewModel
class SourcePluginViewModel @Inject constructor(
    private val registry: PluginRegistry,
    private val catalogClient: PluginCatalogClient,
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

    /** 拉取在线目录并对齐本地已装版本。 */
    fun checkOnline(catalogUrl: String = PluginCatalogClient.DEFAULT_CATALOG_URL) {
        if (_state.value.onlineLoading) return
        viewModelScope.launch {
            _state.update { it.copy(onlineLoading = true, onlineError = null) }
            when (val result = catalogClient.fetch(catalogUrl)) {
                is CatalogResult.Success -> {
                    val installed = _state.value.plugins.associate { it.pluginId to it }
                    val items = result.plugins.map { p ->
                        // 用真实 versionCode 比对，不能只看「装没装」——
                        // 那样永远提示不了更新
                        OnlinePluginItem(
                            plugin = p,
                            status = PluginUpdateChecker.statusOf(
                                p,
                                installed[p.id]?.versionCode,
                            ),
                        )
                    }
                    _state.update {
                        it.copy(
                            onlineLoading = false,
                            online = items,
                            onlineError = if (result.incompatible > 0) {
                                "${result.incompatible} 个插件要求更高版本的宿主，已隐藏"
                            } else {
                                null
                            },
                        )
                    }
                }
                is CatalogResult.Failed -> _state.update {
                    it.copy(onlineLoading = false, onlineError = result.message, online = emptyList())
                }
            }
        }
    }

    /** 下载并安装 catalog 里的某个插件（含摘要校验）。 */
    fun installFromCatalog(item: OnlinePluginItem) {
        if (_state.value.busy) return
        viewModelScope.launch {
            _state.update { it.copy(busy = true, message = "正在下载 ${item.plugin.name}…") }
            when (val dl = catalogClient.download(item.plugin)) {
                is PluginDownloadOutcome.Success -> {
                    val outcome = registry.install(dl.bytes)
                    val msg = when (outcome) {
                        is PluginInstallOutcome.Success -> {
                            val v = outcome.versionName.takeIf { it.isNotBlank() }?.let { " v$it" } ?: ""
                            "已安装 ${outcome.displayName}$v" + if (!dl.verified) "（上游未提供摘要，未校验）" else ""
                        }
                        is PluginInstallOutcome.Invalid -> "插件无效：${outcome.reason}"
                        is PluginInstallOutcome.Rejected -> "已拒绝：${outcome.reason}"
                        is PluginInstallOutcome.Failed -> "安装失败：${outcome.message}"
                    }
                    refresh()
                    viewModelScope.launch { checkOnline() }
                    _state.update { it.copy(busy = false, message = msg) }
                }
                PluginDownloadOutcome.ChecksumMismatch ->
                    _state.update { it.copy(busy = false, message = "摘要校验失败：包可能被篡改，已拒绝安装") }
                is PluginDownloadOutcome.Failed ->
                    _state.update { it.copy(busy = false, message = "下载失败：${dl.message}") }
            }
        }
    }

    fun consumeMessage() = _state.update { it.copy(message = null) }
}
