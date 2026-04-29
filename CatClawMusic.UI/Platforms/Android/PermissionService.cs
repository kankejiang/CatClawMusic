using Android;
using Android.Content.PM;
using Android.OS;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>
/// Android 权限服务
/// Android 13+: READ_MEDIA_AUDIO / READ_MEDIA_IMAGES / READ_MEDIA_VIDEO
/// Android 12-: READ_EXTERNAL_STORAGE（统一覆盖所有媒体文件）
/// </summary>
public class PermissionService : IPermissionService
{
    private static TaskCompletionSource<bool>? _pendingTcs;
    private static TaskCompletionSource<bool>? _pendingTcsAll;

    // ─── 仅音频（原有方法，保持兼容） ────────────────────────

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

    // ─── 全媒体权限（音频 + 照片 + 视频）─────────────────────

    /// <summary>获取当前系统版本需要授权的所有媒体权限</summary>
    private static string[] GetAllMediaPermissions()
    {
#pragma warning disable CA1416 // 运行时已做版本检查
        return Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? new[] { Manifest.Permission.ReadMediaAudio, Manifest.Permission.ReadMediaImages, Manifest.Permission.ReadMediaVideo }
            : new[] { Manifest.Permission.ReadExternalStorage };
#pragma warning restore CA1416
    }

    public Task<bool> CheckAllMediaPermissionsAsync()
    {
#pragma warning disable CA1416 // 运行时已做版本检查
        var ctx = global::Android.App.Application.Context;
        var permissions = GetAllMediaPermissions();
        return Task.FromResult(permissions.All(p => ctx.CheckSelfPermission(p) == Permission.Granted));
#pragma warning restore CA1416
    }

    public Task<bool> RequestAllMediaPermissionsAsync()
    {
#pragma warning disable CA1416 // 运行时已做版本检查
        var activity = MainActivity.Instance;
        if (activity == null)
        {
            System.Diagnostics.Debug.WriteLine("[CatClaw] MainActivity.Instance is null!");
            return Task.FromResult(false);
        }

        // 排除已授权的，只请求缺失的
        var allPermissions = GetAllMediaPermissions();
        var missing = allPermissions.Where(p => activity.CheckSelfPermission(p) != Permission.Granted).ToArray();

        if (missing.Length == 0)
            return Task.FromResult(true);

        var tcs = new TaskCompletionSource<bool>();
        _pendingTcsAll = tcs;

        System.Diagnostics.Debug.WriteLine($"[CatClaw] Requesting all-media permissions: [{string.Join(", ", missing)}]");
        activity.RequestPermissions(missing, 1002);
#pragma warning restore CA1416

        return tcs.Task;
    }

    // ─── 权限结果回调 ─────────────────────────────────────────

    /// <summary>MainActivity 中调用以传递权限结果</summary>
    public static void HandlePermissionResult(int requestCode, Permission[] grantResults)
    {
        if (requestCode == 1001 && _pendingTcs != null)
        {
            var granted = grantResults.Length > 0 && grantResults[0] == Permission.Granted;
            System.Diagnostics.Debug.WriteLine($"[CatClaw] Permission result (1001): {(granted ? "Granted" : "Denied")}");
            _pendingTcs.TrySetResult(granted);
            _pendingTcs = null;
        }
        else if (requestCode == 1002 && _pendingTcsAll != null)
        {
            var allGranted = grantResults.Length > 0 && grantResults.All(r => r == Permission.Granted);
            System.Diagnostics.Debug.WriteLine($"[CatClaw] All-media result (1002): {(allGranted ? "All Granted" : "Some Denied")}");
            _pendingTcsAll.TrySetResult(allGranted);
            _pendingTcsAll = null;
        }
    }

    // ─── 状态查询 ─────────────────────────────────────────────

    public string GetPermissionStatus()
    {
#pragma warning disable CA1416 // 运行时已做版本检查
        var ctx = global::Android.App.Application.Context;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            bool audio = ctx.CheckSelfPermission(Manifest.Permission.ReadMediaAudio) == Permission.Granted;
            bool images = ctx.CheckSelfPermission(Manifest.Permission.ReadMediaImages) == Permission.Granted;
            bool video = ctx.CheckSelfPermission(Manifest.Permission.ReadMediaVideo) == Permission.Granted;

            if (audio && images && video) return "已授权（音乐、照片、视频）";
            var missing = new List<string>();
            if (!audio) missing.Add("音乐和音频");
            if (!images) missing.Add("照片");
            if (!video) missing.Add("视频");
            return $"部分授权（缺少: {string.Join("、", missing)}）";
        }
        else
        {
            var granted = ctx.CheckSelfPermission(Manifest.Permission.ReadExternalStorage) == Permission.Granted;
            return granted ? "已授权（存储空间）" : "需要\"存储空间\"权限来访问媒体文件";
        }
#pragma warning restore CA1416
    }

    /// <summary>是否有任一权限被永久拒绝（勾选了"不再询问"）</summary>
    public bool IsPermanentlyDenied()
    {
#pragma warning disable CA1416 // 运行时已做版本检查
        var activity = MainActivity.Instance;
        if (activity == null) return false;

        var permissions = GetAllMediaPermissions();
        foreach (var perm in permissions)
        {
            if (activity.CheckSelfPermission(perm) != Permission.Granted
                && !activity.ShouldShowRequestPermissionRationale(perm))
                return true;
        }
        return false;
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
