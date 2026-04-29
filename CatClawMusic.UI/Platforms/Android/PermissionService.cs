using Android;
using Android.Content.PM;
using Android.OS;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>
/// Android 权限服务
/// Android 13+: READ_MEDIA_AUDIO
/// Android 12:   READ_EXTERNAL_STORAGE
/// </summary>
public class PermissionService : IPermissionService
{
    private static TaskCompletionSource<bool>? _pendingTcs;

    public Task<bool> CheckStoragePermissionAsync()
    {
#pragma warning disable CA1416 // 运行时已做版本检查
        var ctx = global::Android.App.Application.Context;
        var permission = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? Manifest.Permission.ReadMediaAudio
            : Manifest.Permission.ReadExternalStorage;

        return Task.FromResult(ctx.CheckSelfPermission(permission) == Permission.Granted);
#pragma warning restore CA1416
    }

    public Task<bool> RequestStoragePermissionAsync()
    {
#pragma warning disable CA1416 // 运行时已做版本检查
        var activity = MainActivity.Instance;
        if (activity == null)
        {
            System.Diagnostics.Debug.WriteLine("[CatClaw] MainActivity.Instance is null!");
            return Task.FromResult(false);
        }

        var permission = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? Manifest.Permission.ReadMediaAudio
            : Manifest.Permission.ReadExternalStorage;

        if (activity.CheckSelfPermission(permission) == Permission.Granted)
            return Task.FromResult(true);

        var tcs = new TaskCompletionSource<bool>();
        _pendingTcs = tcs;

        System.Diagnostics.Debug.WriteLine($"[CatClaw] Requesting permission: {permission}");
        activity.RequestPermissions(new[] { permission }, 1001);
#pragma warning restore CA1416

        return tcs.Task;
    }

    /// <summary>MainActivity 中调用以传递结果</summary>
    public static void HandlePermissionResult(int requestCode, Permission[] grantResults)
    {
        if (requestCode == 1001 && _pendingTcs != null)
        {
            var granted = grantResults.Length > 0 && grantResults[0] == Permission.Granted;
            System.Diagnostics.Debug.WriteLine($"[CatClaw] Permission result: {(granted ? "Granted" : "Denied")}");
            _pendingTcs.TrySetResult(granted);
            _pendingTcs = null;
        }
    }

    public string GetPermissionStatus()
    {
#pragma warning disable CA1416 // 运行时已做版本检查
        var ctx = global::Android.App.Application.Context;
        var (perm, label) = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? (Manifest.Permission.ReadMediaAudio, "\"音乐和音频\"")
            : (Manifest.Permission.ReadExternalStorage, "\"存储空间\"");

        var granted = ctx.CheckSelfPermission(perm) == Permission.Granted;
        return granted ? "已授权" : $"需要{label}权限来访问音乐文件夹";
#pragma warning restore CA1416
    }

    /// <summary>检查权限是否被永久拒绝（用户勾选了"不再询问"）</summary>
    public bool IsPermanentlyDenied()
    {
#pragma warning disable CA1416 // 运行时已做版本检查
        var activity = MainActivity.Instance;
        if (activity == null) return false;

        var permission = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? Manifest.Permission.ReadMediaAudio
            : Manifest.Permission.ReadExternalStorage;

        return activity.CheckSelfPermission(permission) != Permission.Granted
               && !activity.ShouldShowRequestPermissionRationale(permission);
#pragma warning restore CA1416
    }

    /// <summary>打开应用系统设置页面（让用户手动授权）</summary>
    public void OpenAppSettings()
    {
        var activity = MainActivity.Instance;
        if (activity == null) return;

        var intent = new global::Android.Content.Intent(
            global::Android.Provider.Settings.ActionApplicationDetailsSettings);
        intent.SetData(global::Android.Net.Uri.Parse($"package:{activity.PackageName}"));
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
        activity.StartActivity(intent);
    }
}
