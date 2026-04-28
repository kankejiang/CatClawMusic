namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 权限请求服务接口
/// </summary>
public interface IPermissionService
{
    /// <summary>检查存储/媒体权限是否已授予</summary>
    Task<bool> CheckStoragePermissionAsync();

    /// <summary>请求存储/媒体权限</summary>
    Task<bool> RequestStoragePermissionAsync();

    /// <summary>获取权限状态描述</summary>
    string GetPermissionStatus();
}
