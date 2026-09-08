using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 插件安装/更新成功后的统一重启提示（插件管理页与插件市场两条安装链路共用）：
/// 说明重启后插件才完全生效，并视平台（AppRestarter.CanRestart）提供「立即重启」按钮。
/// 使用系统对话框（DisplayAlert 双按钮）：当前进程即将退出，自绘弹层没有存续意义。
/// </summary>
public static class PluginRestartPrompt
{
    public static async Task ShowAsync(string successText)
    {
        const string restart = "立即重启";
        const string later = "稍后自行重启";
        string message = AppRestarter.CanRestart
            ? $"{successText}\n\n重启应用后插件将完全生效。"
            : $"{successText}\n\n请手动重启应用以使插件完全生效。";

        Page? page = null;
        try { page = Application.Current?.Windows.FirstOrDefault()?.Page; } catch { }
        if (page == null) return;

        if (!AppRestarter.CanRestart)
        {
            try { await page.DisplayAlertAsync("安装成功", message, "知道了"); }
            catch { ShowToast(message); }
            return;
        }

        string choice;
        try
        {
            choice = await page.DisplayAlertAsync("安装成功", message, restart, later) ? restart : later;
        }
        catch
        {
            // 系统对话框在该设备上可能不可用（MIUI 上曾静默失效）：退化为提示文本
            ShowToast(message);
            return;
        }

        if (choice == restart)
        {
            ShowToast("正在重启应用...");
            // 给 Toast 一帧时间显示，再退出进程
            await Task.Delay(400);
            AppRestarter.Restart();
        }
    }

    /// <summary>轻提示（Android 原生 Toast，其余平台静默）</summary>
    private static void ShowToast(string message)
    {
#if ANDROID
        try
        {
            var ctx = Android.App.Application.Context;
            Android.Widget.Toast.MakeText(ctx, message, Android.Widget.ToastLength.Long)?.Show();
        }
        catch { }
#endif
    }
}
