using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>Android 权限服务，管理存储权限和悬浮窗权限的检查与申请，适配各厂商 ROM</summary>
public class PermissionService : IPermissionService
{
    private static TaskCompletionSource<bool>? _pendingTcs;

    /// <summary>检查存储权限是否已授予</summary>
    public Task<bool> CheckStoragePermissionAsync()
    {
        var ctx = global::Android.App.Application.Context;
        var permission = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? Manifest.Permission.ReadMediaAudio
            : Manifest.Permission.ReadExternalStorage;

        return Task.FromResult(ctx.CheckSelfPermission(permission) == Permission.Granted);
    }

    /// <summary>请求存储权限</summary>
    public Task<bool> RequestStoragePermissionAsync()
    {
        var activity = MainActivity.Instance;
        if (activity == null) return Task.FromResult(false);

        var permission = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? Manifest.Permission.ReadMediaAudio
            : Manifest.Permission.ReadExternalStorage;

        if (activity.CheckSelfPermission(permission) == Permission.Granted)
            return Task.FromResult(true);

        var tcs = new TaskCompletionSource<bool>();
        _pendingTcs = tcs;
        activity.RequestPermissions(new[] { permission }, 1001);
        return tcs.Task;
    }

    /// <summary>处理权限请求结果回调</summary>
    public static void HandlePermissionResult(int requestCode, Permission[] grantResults)
    {
        if (requestCode == 1001 && _pendingTcs != null)
        {
            var granted = grantResults.Length > 0 && grantResults[0] == Permission.Granted;
            _pendingTcs.TrySetResult(granted);
            _pendingTcs = null;
        }
    }

    /// <summary>获取当前权限状态描述文本</summary>
    public string GetPermissionStatus()
    {
        var ctx = global::Android.App.Application.Context;
        var (perm, label) = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? (Manifest.Permission.ReadMediaAudio, "\"音乐和音频\"")
            : (Manifest.Permission.ReadExternalStorage, "\"存储空间\"");

        var granted = ctx.CheckSelfPermission(perm) == Permission.Granted;
        return granted ? "已授权" : $"需要{label}权限来访问音乐文件夹";
    }

    /// <summary>检查权限是否被永久拒绝</summary>
    public bool IsPermanentlyDenied()
    {
        var activity = MainActivity.Instance;
        if (activity == null) return false;

        var permission = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? Manifest.Permission.ReadMediaAudio
            : Manifest.Permission.ReadExternalStorage;

        return activity.CheckSelfPermission(permission) != Permission.Granted
               && !activity.ShouldShowRequestPermissionRationale(permission);
    }

    /// <summary>打开应用详细设置页面</summary>
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

    /// <summary>检查"所有文件访问"权限（Android 11+）</summary>
    public Task<bool> CheckManageStoragePermissionAsync()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            return Task.FromResult(global::Android.OS.Environment.IsExternalStorageManager);
        return Task.FromResult(true);
    }

    /// <summary>请求"所有文件访问"权限</summary>
    public Task<bool> RequestManageStoragePermissionAsync()
    {
        var activity = MainActivity.Instance;
        if (activity == null) return Task.FromResult(false);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            try
            {
                var intent = new global::Android.Content.Intent(
                    global::Android.Provider.Settings.ActionManageAppAllFilesAccessPermission);
                intent.SetData(global::Android.Net.Uri.Parse($"package:{activity.PackageName}"));
                intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
                activity.StartActivity(intent);
            }
            catch
            {
                OpenAppSettings();
            }
        }
        return Task.FromResult(false);
    }

    // ═══════════════════════════════════════════════════════════
    //  悬浮窗权限 — 全厂商适配
    // ═══════════════════════════════════════════════════════════

    /// <summary>检查悬浮窗权限（含厂商 AppOps 适配）</summary>
    public Task<bool> CheckOverlayPermissionAsync()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.M)
            return Task.FromResult(true);

        var ctx = global::Android.App.Application.Context;

        var canDraw = global::Android.Provider.Settings.CanDrawOverlays(ctx);
        if (!canDraw)
        {
            global::Android.Util.Log.Info("PermissionService", $"CheckOverlay({DeviceInfo}): CanDrawOverlays=false");
            return Task.FromResult(false);
        }

        global::Android.Util.Log.Info("PermissionService", $"CheckOverlay({DeviceInfo}): CanDrawOverlays=true, checking AppOps...");

        var appOpsGranted = CheckAppOpsOverlayPermission(ctx);
        global::Android.Util.Log.Info("PermissionService", $"CheckOverlay({DeviceInfo}): AppOps={appOpsGranted}");

        return Task.FromResult(appOpsGranted);
    }

    /// <summary>通过 AppOps 更精确地检查悬浮窗权限</summary>
    private static bool CheckAppOpsOverlayPermission(Context ctx)
    {
        try
        {
            var appOps = ctx.GetSystemService(Context.AppOpsService) as AppOpsManager;
            if (appOps == null) return true;

            var appOpsClass = Java.Lang.Class.FromType(typeof(AppOpsManager));

            if (IsHuawei())
            {
                return CheckHuaweiAppOps(ctx, appOps);
            }
            if (IsVivo())
            {
                return CheckVivoAppOps(ctx, appOps);
            }

            var packageName = ctx.PackageName!;
            var uid = ctx.ApplicationInfo!.Uid;

            var checkOpMethod = appOpsClass.GetMethod(
                "checkOpNoThrow",
                Java.Lang.Class.FromType(typeof(int)),
                Java.Lang.Class.FromType(typeof(int)),
                Java.Lang.Class.FromType(typeof(string)));

            var opField = appOpsClass.GetDeclaredField("OP_SYSTEM_ALERT_WINDOW");
            var opValue = opField.GetInt(null);

            var result = (int)checkOpMethod!.Invoke(appOps, opValue, uid, packageName)!;
            var allowed = result == (int)AppOpsManagerMode.Allowed;

            global::Android.Util.Log.Info("PermissionService", $"AppOps OP_SYSTEM_ALERT_WINDOW={result}, allowed={allowed}");
            return allowed;
        }
        catch (System.Exception ex)
        {
            global::Android.Util.Log.Warn("PermissionService", $"AppOps check failed: {ex.Message}, assuming granted");
            return true;
        }
    }

    /// <summary>华为设备 AppOps 悬浮窗权限检查</summary>
    private static bool CheckHuaweiAppOps(Context ctx, AppOpsManager appOps)
    {
        try
        {
            const int HuaweiAppOpsCode = 100000;
            var huaweiClass = Java.Lang.Class.ForName("com.huawei.android.app.AppOpsManagerEx");
            var checkMethod = huaweiClass.GetDeclaredMethod(
                "checkHwOpNoThrow",
                Java.Lang.Class.FromType(typeof(AppOpsManager)),
                Java.Lang.Class.FromType(typeof(int)),
                Java.Lang.Class.FromType(typeof(int)),
                Java.Lang.Class.FromType(typeof(string)));

            var result = (int)checkMethod!.Invoke(
                huaweiClass.NewInstance(),
                appOps,
                HuaweiAppOpsCode,
                global::Android.OS.Process.MyUid(),
                ctx.PackageName)!;

            var allowed = result == (int)AppOpsManagerMode.Allowed;
            global::Android.Util.Log.Info("PermissionService", $"Huawei AppOpsEx checkHwOpNoThrow={result}, allowed={allowed}");
            return allowed;
        }
        catch (System.Exception ex)
        {
            global::Android.Util.Log.Warn("PermissionService", $"Huawei AppOpsEx failed: {ex.Message}");
            return true;
        }
    }

    /// <summary>vivo 设备 AppOps 悬浮窗权限检查</summary>
    private static bool CheckVivoAppOps(Context ctx, AppOpsManager appOps)
    {
        try
        {
            var checkOpMethod = appOps.Class.GetMethod(
                "checkOpNoThrow",
                Java.Lang.Class.FromType(typeof(int)),
                Java.Lang.Class.FromType(typeof(int)),
                Java.Lang.Class.FromType(typeof(string)));

            var opField = appOps.Class.GetDeclaredField("OP_SYSTEM_ALERT_WINDOW");
            if (opField != null)
            {
                var opValue = opField.GetInt(null);
                var result = (int)checkOpMethod!.Invoke(appOps, opValue, global::Android.OS.Process.MyUid(), ctx.PackageName)!;
                global::Android.Util.Log.Info("PermissionService", $"Vivo AppOps={result}");
                return result == (int)AppOpsManagerMode.Allowed;
            }
        }
        catch (System.Exception ex)
        {
            global::Android.Util.Log.Warn("PermissionService", $"Vivo AppOps failed: {ex.Message}");
        }
        return true;
    }

    /// <summary>请求悬浮窗权限，尝试跳转到各厂商权限设置页面</summary>
    public Task<bool> RequestOverlayPermissionAsync()
    {
        var activity = MainActivity.Instance;
        if (activity == null) return Task.FromResult(false);
        if (Build.VERSION.SdkInt < BuildVersionCodes.M) return Task.FromResult(false);

        global::Android.Util.Log.Info("PermissionService", $"RequestOverlay: device={DeviceInfo}");

        TryOpenOemPermissionPage(activity);
        return Task.FromResult(false);
    }

    /// <summary>尝试打开各厂商 OEM 悬浮窗权限页面</summary>
    private static void TryOpenOemPermissionPage(Activity activity)
    {
        if (IsXiaomi() && TryXiaomiPage(activity)) return;
        if (IsHuawei() && TryHuaweiPage(activity)) return;
        if (IsHonor() && TryHonorPage(activity)) return;
        if (IsOppo() && TryOppoPage(activity)) return;
        if (IsVivo() && TryVivoPage(activity)) return;
        if (IsMeizu() && TryMeizuPage(activity)) return;

        TryGenericOverlayPage(activity);
    }

    // ═══════════════════════════════════════════════════════════
    //  设备识别
    // ═══════════════════════════════════════════════════════════

    /// <summary>设备信息描述文本</summary>
    private static string DeviceInfo =>
        $"{Build.Manufacturer}/{Build.Brand} SDK{Build.VERSION.SdkInt}";

    private static bool IsXiaomi() =>
        Matches("xiaomi") || Matches("redmi");

    private static bool IsHuawei() =>
        Matches("huawei") && !Matches("honor");

    private static bool IsHonor() =>
        Matches("honor");

    private static bool IsOppo() =>
        Matches("oppo") || Matches("realme") || Matches("oneplus");

    private static bool IsVivo() =>
        Matches("vivo") || Matches("iqoo");

    private static bool IsMeizu() =>
        Matches("meizu");

    /// <summary>根据关键字匹配当前设备厂商</summary>
    private static bool Matches(string keyword)
    {
        var m = Build.Manufacturer?.ToLowerInvariant() ?? "";
        var b = Build.Brand?.ToLowerInvariant() ?? "";
        return m.Contains(keyword) || b.Contains(keyword);
    }

    // ═══════════════════════════════════════════════════════════
    //  各厂商权限页跳转
    // ═══════════════════════════════════════════════════════════

    /// <summary>尝试打开小米安全中心权限页</summary>
    private static bool TryXiaomiPage(Activity activity) =>
        TryIntent(activity, "com.miui.securitycenter",
            "com.miui.permcenter.permissions.AppPermissionsEditorActivity")
        || TryIntent(activity, "com.miui.securitycenter",
            "com.miui.permcenter.permissions.PermissionsEditorActivity");

    /// <summary>尝试打开华为悬浮窗管理页</summary>
    private static bool TryHuaweiPage(Activity activity) =>
        TryIntent(activity, "com.huawei.systemmanager",
            "com.huawei.systemmanager.addviewmonitor.AddViewMonitorActivity")
        || TryIntent(activity, "com.huawei.systemmanager",
            "com.huawei.systemmanager.appcontrol.activity.StartupAppControlActivity")
        || TryIntent(activity, "com.huawei.systemmanager",
            "com.huawei.permissionmanager.ui.MainActivity");

    /// <summary>尝试打开荣耀悬浮窗管理页</summary>
    private static bool TryHonorPage(Activity activity) =>
        TryIntent(activity, "com.hihonor.systemmanager",
            "com.hihonor.systemmanager.addviewmonitor.AddViewMonitorActivity")
        || TryHuaweiPage(activity);

    /// <summary>尝试打开 OPPO 悬浮窗管理页</summary>
    private static bool TryOppoPage(Activity activity) =>
        TryIntent(activity, "com.coloros.safecenter",
            "com.coloros.safecenter.permission.floatwindow.FloatWindowListActivity")
        || TryIntent(activity, "com.oppo.safe",
            "com.oppo.safe.permission.PermissionTopActivity")
        || TryIntent(activity, "com.coloros.safecenter",
            "com.coloros.safecenter.permission.permissionlist.PermissionListActivity");

    /// <summary>尝试打开 vivo 悬浮窗管理页</summary>
    private static bool TryVivoPage(Activity activity) =>
        TryIntent(activity, "com.vivo.permissionmanager",
            "com.vivo.permissionmanager.activity.SoftPermissionDetailActivity")
        || TryIntent(activity, "com.iqoo.secure",
            "com.iqoo.secure.MainActivity");

    /// <summary>尝试打开魅族悬浮窗管理页</summary>
    private static bool TryMeizuPage(Activity activity)
    {
        try
        {
            var intent = new global::Android.Content.Intent("com.meizu.safe.security.SHOW_APPSEC");
            intent.AddCategory(global::Android.Content.Intent.CategoryDefault);
            intent.PutExtra("packageName", activity.PackageName);
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            activity.StartActivity(intent);
            global::Android.Util.Log.Info("PermissionService", "Meizu SHOW_APPSEC opened");
            return true;
        }
        catch (System.Exception ex)
        {
            global::Android.Util.Log.Warn("PermissionService", $"Meizu page failed: {ex.Message}");
        }
        return TryIntent(activity, "com.meizu.safe",
            "com.meizu.safe.security.AppSecActivity");
    }

    /// <summary>尝试打开通用悬浮窗权限设置页</summary>
    private static void TryGenericOverlayPage(Activity activity)
    {
        try
        {
            var intent = new global::Android.Content.Intent(
                global::Android.Provider.Settings.ActionManageOverlayPermission);
            intent.SetData(global::Android.Net.Uri.Parse($"package:{activity.PackageName}"));
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            activity.StartActivity(intent);
            global::Android.Util.Log.Info("PermissionService", "Generic overlay page opened");
        }
        catch
        {
            try { OpenAppSettingsStatic(activity); }
            catch { /* exhausted */ }
        }
    }

    /// <summary>尝试通过指定包名/类名打开 Activity</summary>
    private static bool TryIntent(Activity activity, string pkg, string cls)
    {
        try
        {
            var intent = new global::Android.Content.Intent();
            intent.SetComponent(new global::Android.Content.ComponentName(pkg, cls));
            intent.PutExtra("extra_pkgname", activity.PackageName);
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            activity.StartActivity(intent);
            global::Android.Util.Log.Info("PermissionService", $"Opened {pkg}/{cls}");
            return true;
        }
        catch (System.Exception ex)
        {
            global::Android.Util.Log.Warn("PermissionService", $"{pkg}/{cls} failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>打开应用详情设置页（静态方法）</summary>
    private static void OpenAppSettingsStatic(Activity activity)
    {
        var intent = new global::Android.Content.Intent(
            global::Android.Provider.Settings.ActionApplicationDetailsSettings);
        intent.SetData(global::Android.Net.Uri.Parse($"package:{activity.PackageName}"));
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
        activity.StartActivity(intent);
    }
}
