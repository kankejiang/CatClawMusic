namespace CatClawMusic.Maui.Services;

/// <summary>
/// 应用重启工具：插件安装/更新后提供「立即重启」能力（插件程序集已随进程加载，
/// 只有完整重启才能保证新/旧插件代码完全生效）。
/// - Android：AlarmManager 定时拉起启动 Intent + 退出进程（MIUI/HyperOS 等 ROM 下最可靠）；
/// - Windows：启动新进程 + 退出当前应用；
/// - 其余平台：返回 false，由调用方提示用户手动重启。
/// </summary>
public static class AppRestarter
{
    /// <summary>当前平台是否支持应用内自动重启</summary>
    public static bool CanRestart =>
#if ANDROID || WINDOWS
        true;
#else
        false;
#endif

    /// <summary>重启应用（调度重新拉起 + 结束当前进程）</summary>
    public static void Restart()
    {
#if ANDROID
        try
        {
            var ctx = Android.App.Application.Context;
            var launch = ctx.PackageManager?.GetLaunchIntentForPackage(ctx.PackageName!);
            if (launch != null)
            {
                launch.AddFlags(Android.Content.ActivityFlags.ClearTop);
                launch.AddFlags(Android.Content.ActivityFlags.NewTask);
                var pi = Android.App.PendingIntent.GetActivity(ctx, 1001, launch,
                    Android.App.PendingIntentFlags.CancelCurrent
                    | (OperatingSystem.IsAndroidVersionAtLeast(31) ? Android.App.PendingIntentFlags.Immutable : 0));
                var am = (Android.App.AlarmManager?)ctx.GetSystemService(Android.Content.Context.AlarmService);
                am?.Set(Android.App.AlarmType.Rtc, Java.Lang.JavaSystem.CurrentTimeMillis() + 300, pi);
            }
            // 结束进程；AlarmManager 到点后由系统重新拉起主界面
            Java.Lang.JavaSystem.Exit(0);
        }
        catch
        {
            try { Java.Lang.JavaSystem.Exit(0); } catch { }
        }
#elif WINDOWS
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch { }
        try { Application.Current?.Quit(); } catch { }
#endif
    }
}
