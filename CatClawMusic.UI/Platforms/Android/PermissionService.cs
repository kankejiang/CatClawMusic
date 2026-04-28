using Android;
using Android.App;
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
        var act = Platform.CurrentActivity;
        if (act == null) return false;

        string permission;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            permission = Manifest.Permission.ReadMediaAudio;
        else if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            permission = Manifest.Permission.ReadExternalStorage;
        else
            return true;

        // 已经授予
        if (ctx.CheckSelfPermission(permission) == Permission.Granted)
            return true;

        var tcs = new TaskCompletionSource<bool>();
        _pendingTcs = tcs;
        _pendingPermission = permission;
        act.RequestPermissions(new[] { permission }, 1001);
        return await tcs.Task;
    }

    private static TaskCompletionSource<bool>? _pendingTcs;
    private static string? _pendingPermission;

    public static void OnPermissionResult(int requestCode, Permission[] grantResults)
    {
        if (requestCode == 1001 && _pendingTcs != null)
        {
            var granted = grantResults.Length > 0 && grantResults[0] == Permission.Granted;
            _pendingTcs.TrySetResult(granted);
            _pendingTcs = null;
        }
    }

    public string GetPermissionStatus()
    {
        var ctx = global::Android.App.Application.Context;
        string perm;
        string label;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            perm = Manifest.Permission.ReadMediaAudio;
            label = "\"音乐和音频\"";
        }
        else
        {
            perm = Manifest.Permission.ReadExternalStorage;
            label = "\"存储空间\"";
        }

        var granted = ctx.CheckSelfPermission(perm) == Permission.Granted;
        return granted ? "已授权" : $"需要{label}权限来访问音乐文件夹";
    }
}
