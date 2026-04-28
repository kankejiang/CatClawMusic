using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>
/// 权限服务——使用 MAUI 内置权限 API
/// </summary>
public class PermissionService : IPermissionService
{
    public async Task<bool> CheckStoragePermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
        return status == PermissionStatus.Granted;
    }

    public async Task<bool> RequestStoragePermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
        if (status == PermissionStatus.Granted)
            return true;

        status = await Permissions.RequestAsync<Permissions.StorageRead>();
        return status == PermissionStatus.Granted;
    }

    public string GetPermissionStatus()
    {
        var status = Permissions.CheckStatusAsync<Permissions.StorageRead>().Result;
        return status == PermissionStatus.Granted
            ? "已授权"
            : "需要存储权限来扫描本地音乐文件";
    }
}
