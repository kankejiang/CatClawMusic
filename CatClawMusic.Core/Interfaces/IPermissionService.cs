namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 权限请求服务接口（保留骨架，当前版本走 SAF Picker 路线）
/// </summary>
public interface IPermissionService
{
    /// <summary>检查存储/媒体权限是否已授予</summary>
    Task<bool> CheckStoragePermissionAsync();

    /// <summary>请求存储/媒体权限</summary>
    Task<bool> RequestStoragePermissionAsync();

    /// <summary>检查全文件管理权限 MANAGE_EXTERNAL_STORAGE（Android 11+ 绕过 Scoped Storage）</summary>
    Task<bool> CheckManageStoragePermissionAsync();

    /// <summary>跳转到系统设置页面让用户手动开启全文件管理权限</summary>
    Task<bool> RequestManageStoragePermissionAsync();

    /// <summary>获取权限状态描述</summary>
    string GetPermissionStatus();

    /// <summary>权限是否被永久拒绝（不再询问），需引导用户到系统设置</summary>
    bool IsPermanentlyDenied();

    /// <summary>打开应用系统设置页面（让用户手动授权）</summary>
    void OpenAppSettings();
}
