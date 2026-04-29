namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 权限请求服务接口
/// </summary>
public interface IPermissionService
{
    /// <summary>检查存储/媒体权限是否已授予（仅音频）</summary>
    Task<bool> CheckStoragePermissionAsync();

    /// <summary>请求存储/媒体权限（仅音频）</summary>
    Task<bool> RequestStoragePermissionAsync();

    /// <summary>检查所有媒体权限（音频 + 照片 + 视频）是否已授予</summary>
    Task<bool> CheckAllMediaPermissionsAsync();

    /// <summary>请求所有媒体权限（音频 + 照片 + 视频），Android 13+ 一次请求三个细化权限</summary>
    Task<bool> RequestAllMediaPermissionsAsync();

    /// <summary>获取权限状态描述</summary>
    string GetPermissionStatus();

    /// <summary>权限是否被永久拒绝（不再询问），需引导用户到系统设置</summary>
    bool IsPermanentlyDenied();

    /// <summary>打开应用系统设置页面（让用户手动授权）</summary>
    void OpenAppSettings();
}
