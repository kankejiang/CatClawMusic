using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// MAUI 权限服务实现，封装 MAUI Permissions API。
/// Android 上使用 Permissions.RequestAsync，Windows 上大部分权限自动授予。
/// </summary>
public class PermissionService : IPermissionService
{
    /// <summary>检查存储/媒体读取权限是否已授予（Android 13+ 检查 READ_MEDIA_AUDIO）</summary>
    /// <returns>已授予返回 true；Windows 平台始终返回 true</returns>
    public async Task<bool> CheckStoragePermissionAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            // Android 13+：READ_EXTERNAL_STORAGE 已废弃，检查 READ_MEDIA_AUDIO
            var status = await Permissions.CheckStatusAsync<Platforms.Android.AudioPermission>();
            return status == PermissionStatus.Granted;
        }
        // Android 12 及以下：检查 READ_EXTERNAL_STORAGE
        var legacy = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
        return legacy == PermissionStatus.Granted;
#else
        return true; // Windows 不需要显式存储权限
#endif
    }

    /// <summary>请求存储/媒体读取权限（Android 13+ 请求 READ_MEDIA_AUDIO）</summary>
    /// <returns>授权成功返回 true；已授权或 Windows 平台返回 true</returns>
    public async Task<bool> RequestStoragePermissionAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            // Android 13+：请求 READ_MEDIA_AUDIO
            var status = await Permissions.CheckStatusAsync<Platforms.Android.AudioPermission>();
            if (status == PermissionStatus.Granted) return true;
            status = await Permissions.RequestAsync<Platforms.Android.AudioPermission>();
            return status == PermissionStatus.Granted;
        }
        // Android 12 及以下：请求 READ_EXTERNAL_STORAGE
        var legacy = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
        if (legacy == PermissionStatus.Granted) return true;
        legacy = await Permissions.RequestAsync<Permissions.StorageRead>();
        return legacy == PermissionStatus.Granted;
#else
        return true;
#endif
    }

    /// <summary>检查媒体音频（音乐和音频）权限是否已授予</summary>
    /// <returns>已授予返回 true；Windows 平台始终返回 true</returns>
    public async Task<bool> CheckAudioPermissionAsync()
    {
#if ANDROID
        var status = await Permissions.CheckStatusAsync<Platforms.Android.AudioPermission>();
        return status == PermissionStatus.Granted;
#else
        return true;
#endif
    }

    /// <summary>
    /// 请求媒体音频（音乐和音频）权限。
    /// Android 13+ 请求 READ_MEDIA_AUDIO；低版本请求 READ_EXTERNAL_STORAGE。
    /// </summary>
    /// <returns>授权成功返回 true；已授权或 Windows 平台返回 true</returns>
    public async Task<bool> RequestAudioPermissionAsync()
    {
#if ANDROID
        var status = await Permissions.CheckStatusAsync<Platforms.Android.AudioPermission>();
        if (status == PermissionStatus.Granted) return true;

        status = await Permissions.RequestAsync<Platforms.Android.AudioPermission>();
        return status == PermissionStatus.Granted;
#else
        return true;
#endif
    }

    /// <summary>检查"管理所有文件"权限（Android 11+ 的 IsExternalStorageManager）</summary>
    /// <returns>已授予或不需要返回 true；否则返回 false</returns>
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

    /// <summary>
    /// 请求"管理所有文件"权限。
    /// 跳转到系统设置页面由用户手动授权，调用方需监听应用恢复后再检查权限状态。
    /// </summary>
    /// <returns>始终返回 false，需要用户在系统设置中手动授权</returns>
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

    /// <summary>获取当前权限状态的可读文本描述</summary>
    /// <returns>权限状态文本</returns>
    public string GetPermissionStatus()
    {
#if ANDROID
        var granted = Android.OS.Environment.IsExternalStorageManager;
        return granted ? "已授予全部文件管理权限" : "未授予，请在系统设置中开启";
#else
        return "Windows 无需额外权限";
#endif
    }

    /// <summary>检查权限是否被永久拒绝（MAUI 不直接支持此检测，固定返回 false）</summary>
    /// <returns>始终返回 false</returns>
    public bool IsPermanentlyDenied()
    {
#if ANDROID
        // MAUI 不直接支持此检测，简单返回 false
        return false;
#else
        return false;
#endif
    }

    /// <summary>打开当前应用的系统设置详情页</summary>
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

    /// <summary>检查悬浮窗权限是否已授予</summary>
    /// <returns>已授予或 Windows 平台返回 true；否则返回 false</returns>
    public async Task<bool> CheckOverlayPermissionAsync()
    {
#if ANDROID
        return Android.Provider.Settings.CanDrawOverlays(Android.App.Application.Context);
#else
        return true;
#endif
    }

    /// <summary>
    /// 请求悬浮窗权限。
    /// 跳转到系统设置页面由用户手动授权，调用方需监听应用恢复后再检查权限状态。
    /// </summary>
    /// <returns>始终返回 false，需要用户在系统设置中手动授权</returns>
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
