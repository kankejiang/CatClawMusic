package com.catclaw.music.kt.data.repository.permission

import android.content.Intent
import com.catclaw.music.kt.data.di.IoDispatcher
import com.catclaw.music.kt.data.permission.AndroidPermissionProbe
import com.catclaw.music.kt.data.permission.PermissionKind
import com.catclaw.music.kt.data.permission.PermissionSnapshot
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.withContext

/**
 * 权限探测唯一入口。
 *
 * `PackageManager` 查询与通知开关读取都是跨进程调用，必须在 [IoDispatcher] 上执行 ——
 * 旧实现在 Composable 组合期直接 `probe.snapshot()`，进权限页会掉帧。
 * `requestIntent` / `runtimePermissions` 只是构造 Intent/数组，保持同步。
 */
@Singleton
class PermissionRepository @Inject constructor(
    private val probe: AndroidPermissionProbe,
    @IoDispatcher private val io: CoroutineDispatcher,
) {
    suspend fun snapshot(): PermissionSnapshot = withContext(io) { probe.snapshot() }

    fun requestIntent(kind: PermissionKind): Intent? = probe.requestIntent(kind)

    fun runtimePermissions(kind: PermissionKind): Array<String> = probe.runtimePermissions(kind)

    fun appSettingsIntent(): Intent = probe.appSettingsIntent()
}
