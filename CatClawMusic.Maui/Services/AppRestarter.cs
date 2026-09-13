namespace CatClawMusic.Maui.Services;

/// <summary>
/// 应用重启工具：插件安装/更新后提供重启能力（插件程序集已随进程加载，
/// 只有完整重启才能保证新/旧插件代码完全生效）。
/// - Windows：启动新进程 + 退出当前应用（可靠，支持自动重启）；
/// - Android：**不支持自动重启** —— Android 10+ 限制后台启动 Activity，MIUI/HyperOS 等
///   ROM 还会额外拦截 AlarmManager 拉起（实测退出后不会自动回来），故改为提示用户手动重开，
///   仅提供「退出应用」动作（ExitForManualRestart），避免给用户「点了就会自己回来」的错觉。
/// </summary>
public static class AppRestarter
{
    /// <summary>当前平台是否支持应用内**自动**重启（Android 为 false）</summary>
    public static bool SupportsAutoRestart =>
#if WINDOWS
        true;
#else
        false;
#endif

    /// <summary>当前平台是否支持应用内重启相关动作（自动重启或退出以便手动重开）</summary>
    public static bool CanRestart =>
#if ANDROID || WINDOWS
        true;
#else
        false;
#endif

    /// <summary>重启应用（仅 Windows 等支持自动重启的平台调用）</summary>
    public static void Restart()
    {
#if WINDOWS
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

    /// <summary>
    /// 结束当前进程，便于用户手动重新打开应用（Android 用）。
    /// 不尝试拉起自身：系统会拦截后台 Activity 启动，静默失败反而误导用户。
    /// </summary>
    public static void ExitForManualRestart()
    {
#if ANDROID
        try { Java.Lang.JavaSystem.Exit(0); } catch { }
#endif
    }
}
