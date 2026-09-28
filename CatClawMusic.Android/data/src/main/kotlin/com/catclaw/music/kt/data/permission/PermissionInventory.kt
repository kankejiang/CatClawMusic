package com.catclaw.music.kt.data.permission

/**
 * 权限管理页的行模型与排布规则（纯 Kotlin，不碰资源 id —— 资源在 `:app`，这层只讲"有哪几行、什么状态"）。
 *
 * 为什么要单独抽出来测：这页全部的逻辑就是「快照 → 四行」，而它的错法很隐蔽
 * （漏一行、状态与动作文字接反、顺序变了），编译与肉眼都发现不了。
 */
enum class PermissionKind { Audio, ManageStorage, Overlay, Notification }

data class PermissionSnapshot(
    val audio: Boolean,
    val manageStorage: Boolean,
    val overlay: Boolean,
    val notification: Boolean,
)

data class PermissionRow(val kind: PermissionKind, val granted: Boolean)

/** 行序照 MAUI `PermissionManagementViewModel.RefreshAsync` 里四次 `Permissions.Add` 的顺序 */
object PermissionInventory {

    val ORDER = listOf(
        PermissionKind.Audio,
        PermissionKind.ManageStorage,
        PermissionKind.Overlay,
        PermissionKind.Notification,
    )

    fun rows(snapshot: PermissionSnapshot): List<PermissionRow> = ORDER.map { kind ->
        PermissionRow(kind, grantedOf(snapshot, kind))
    }

    fun grantedOf(snapshot: PermissionSnapshot, kind: PermissionKind): Boolean = when (kind) {
        PermissionKind.Audio -> snapshot.audio
        PermissionKind.ManageStorage -> snapshot.manageStorage
        PermissionKind.Overlay -> snapshot.overlay
        PermissionKind.Notification -> snapshot.notification
    }

    /** 与 MAUI `PermissionItem` 的 `StatusText`/`ActionText` 同一套判据：授予了就只给看，没授予才给"去授权" */
    fun actionIsRequest(row: PermissionRow): Boolean = !row.granted
}
