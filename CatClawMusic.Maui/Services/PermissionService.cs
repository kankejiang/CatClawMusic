using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// MAUI 权限服务实现，封装 MAUI Permissions API。
/// Android 上使用 Permissions.RequestAsync，Windows 上大部分权限自动授予。
/// </summary>
public class PermissionService : IPermissionService
{
    public async Task<bool> CheckStoragePermissionAsync()
    {
#if ANDROID
        var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
        return status == PermissionStatus.Granted;
#else
        return true; // Windows 不需要显式存储权限
#endif
    }

    public async Task<bool> RequestStoragePermissionAsync()
    {
#if ANDROID
        var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
        if (status == PermissionStatus.Granted) return true;

        status = await Permissions.RequestAsync<Permissions.StorageRead>();
        return status == PermissionStatus.Granted;
#else
        return true;
#endif
    }

    public async Task<bool> CheckManageStoragePermissionAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            return Android.OS.Environment.IsExternalStorageManager;
        }
        return true;
#else
        return true;
#endif
    }

    public Task<bool> RequestManageStoragePermissionAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            try
            {
                var intent = new Android.Content.Intent(
                    Android.Provider.Settings.ActionManageAppAllFilesAccessPermission,
                    Android.Net.Uri.Parse($"package:{Android.App.Application.Context.PackageName}"));
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
            }
            catch
            {
                try
                {
                    var intent = new Android.Content.Intent(
                        Android.Provider.Settings.ActionManageAllFilesAccessPermission);
                    intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                    Android.App.Application.Context.StartActivity(intent);
                }
                catch { }
            }
        }
#endif
        return Task.FromResult(false); // 需要用户手动授权
    }

    public string GetPermissionStatus()
    {
#if ANDROID
        var granted = Android.OS.Environment.IsExternalStorageManager;
        return granted ? "已授予全部文件管理权限" : "未授予，请在系统设置中开启";
#else
        return "Windows 无需额外权限";
#endif
    }

    public bool IsPermanentlyDenied()
    {
#if ANDROID
        // MAUI 不直接支持此检测，简单返回 false
        return false;
#else
        return false;
#endif
    }

    public void OpenAppSettings()
    {
#if ANDROID
        try
        {
            var intent = new Android.Content.Intent(
                Android.Provider.Settings.ActionApplicationDetailsSettings,
                Android.Net.Uri.Parse($"package:{Android.App.Application.Context.PackageName}"));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            Android.App.Application.Context.StartActivity(intent);
        }
        catch { }
#endif
    }

    public async Task<bool> CheckOverlayPermissionAsync()
    {
#if ANDROID
        return Android.Provider.Settings.CanDrawOverlays(Android.App.Application.Context);
#else
        return true;
#endif
    }

    public Task<bool> RequestOverlayPermissionAsync()
    {
#if ANDROID
        try
        {
            var intent = new Android.Content.Intent(
                Android.Provider.Settings.ActionManageOverlayPermission,
                Android.Net.Uri.Parse($"package:{Android.App.Application.Context.PackageName}"));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            Android.App.Application.Context.StartActivity(intent);
        }
        catch { }
#endif
        return Task.FromResult(false);
    }
}
