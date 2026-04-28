using Android;
using Android.Content.PM;
using Android.OS;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.UI.Platforms.Android;

public class PermissionService : IPermissionService
{
    public Task<bool> CheckStoragePermissionAsync()
    {
        var ctx = global::Android.App.Application.Context;
        bool granted;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            granted = ctx.CheckSelfPermission(Manifest.Permission.ReadMediaAudio) == Permission.Granted;
        else if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            granted = ctx.CheckSelfPermission(Manifest.Permission.ReadExternalStorage) == Permission.Granted;
        else
            granted = true;

        return Task.FromResult(granted);
    }

    public async Task<bool> RequestStoragePermissionAsync()
    {
        var ctx = global::Android.App.Application.Context;

        // 直接使用 MAUI 的权限 API 来处理
        var status = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            // 先检查
            var s = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
            if (s == PermissionStatus.Granted) return s;

            // 请求
            s = await Permissions.RequestAsync<Permissions.StorageRead>();
            return s;
        });

        return status == PermissionStatus.Granted;
    }

    public string GetPermissionStatus()
    {
        var ctx = global::Android.App.Application.Context;
        string checkPerm;
        string label;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            checkPerm = Manifest.Permission.ReadMediaAudio;
            label = "\"音乐和音频\"";
        }
        else
        {
            checkPerm = Manifest.Permission.ReadExternalStorage;
            label = "\"存储空间\"";
        }

        var granted = ctx.CheckSelfPermission(checkPerm) == Permission.Granted;
        return granted ? "已授权" : $"需要{label}权限来访问音乐文件夹";
    }
}
