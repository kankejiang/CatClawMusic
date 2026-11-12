package com.catclaw.music.kt.ui.settings

import android.content.Intent
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.data.permission.PermissionKind
import com.catclaw.music.kt.data.permission.PermissionSnapshot
import com.catclaw.music.kt.data.repository.permission.PermissionRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/**
 * 权限管理页 ViewModel：快照在 IO 线程读取，Composable 只消费状态。
 * 不再把 `AndroidPermissionProbe` 作为公开属性暴露给 UI（旧实现如此）。
 */
@HiltViewModel
class PermissionViewModel @Inject constructor(
    private val permissions: PermissionRepository,
) : ViewModel() {

    private val _snapshot = MutableStateFlow(
        PermissionSnapshot(audio = false, manageStorage = false, overlay = false, notification = false),
    )
    val snapshot: StateFlow<PermissionSnapshot> = _snapshot.asStateFlow()

    init {
        refresh()
    }

    /** 进页 / 从系统设置返回 / 点刷新时重读 */
    fun refresh() {
        viewModelScope.launch { _snapshot.value = permissions.snapshot() }
    }

    fun requestIntent(kind: PermissionKind): Intent? = permissions.requestIntent(kind)

    fun runtimePermissions(kind: PermissionKind): Array<String> = permissions.runtimePermissions(kind)

    fun appSettingsIntent(): Intent = permissions.appSettingsIntent()
}
