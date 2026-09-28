package com.catclaw.music.kt.data.permission

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.Settings
import androidx.core.content.ContextCompat
import com.catclaw.music.kt.data.lyrics.OverlayPermission
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 读四项权限的**实况**并给出对应的系统跳转。
 *
 * 只报"清单里声明过、且系统真会给状态"的项：这四项在 `AndroidManifest.xml` 里都实际声明了
 * （含 `MANAGE_EXTERNAL_STORAGE`），所以列出来不是假项。反过来说，本端扫描走 MediaStore，
 * 「管理所有文件」这一行是**状态展示**而不是功能前提 —— 别把它当成"不授权就扫不全"。
 */
@Singleton
class AndroidPermissionProbe @Inject constructor(
    @ApplicationContext private val appContext: Context,
) {

    fun snapshot(): PermissionSnapshot = PermissionSnapshot(
        audio = granted(audioPermission()),
        manageStorage = Environment.isExternalStorageManager(),
        overlay = OverlayPermission.granted(appContext),
        notification = granted(Manifest.permission.POST_NOTIFICATIONS),
    )

    /**
     * 未授予时的去处：运行时权限回给 null，由界面走 `requestPermissions`（系统弹窗）；
     * 特殊权限（悬浮窗 / 所有文件 / 通知）只能去系统设置页，MAUI 那边同理。
     */
    fun requestIntent(kind: PermissionKind): Intent? = when (kind) {
        PermissionKind.Overlay -> OverlayPermission.requestIntent(appContext)
        PermissionKind.ManageStorage -> Intent(
            Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
            Uri.parse("package:" + appContext.packageName),
        ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        PermissionKind.Notification -> Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS)
            .putExtra(Settings.EXTRA_APP_PACKAGE, appContext.packageName)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        PermissionKind.Audio -> null
    }

    /** 需要走运行时弹窗的项（目前只有音频读取） */
    fun runtimePermissions(kind: PermissionKind): Array<String> =
        if (kind == PermissionKind.Audio) arrayOf(audioPermission()) else emptyArray()

    fun appSettingsIntent(): Intent = Intent(
        Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
        Uri.parse("package:" + appContext.packageName),
    ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)

    /**
     * 33+ 只有 READ_MEDIA_AUDIO 可授予，32 及以下只有 READ_EXTERNAL_STORAGE。
     * 两个都查再取与是错的：低版本查新权限恒 DENIED，会把已授权的项报成未授权。
     */
    private fun audioPermission(): String =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) Manifest.permission.READ_MEDIA_AUDIO
        else Manifest.permission.READ_EXTERNAL_STORAGE

    private fun granted(permission: String): Boolean =
        ContextCompat.checkSelfPermission(appContext, permission) == PackageManager.PERMISSION_GRANTED

}
