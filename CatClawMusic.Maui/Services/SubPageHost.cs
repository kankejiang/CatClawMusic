using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Pages;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// <see cref="ISubPageHost"/> 的桌面壳层实现：把外部模块（插件）构造的页面内嵌到
/// <see cref="DesktopBlankPage"/> 的主内容区（Windows 无 Shell 的窗口直连模式）。
/// 原先插件在桌面端只能整窗模态推页 → 详情页会盖住侧栏与底部播放条。
/// </summary>
public sealed class SubPageHost : ISubPageHost
{
    /// <summary>桌面壳层页面存在即可内嵌</summary>
    public bool CanEmbed => DesktopBlankPage.Instance != null;

    public Task OpenEmbeddedAsync(object page)
    {
        if (page is not ContentPage contentPage) return Task.CompletedTask;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { DesktopBlankPage.Instance?.OpenEmbeddedPage(contentPage); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SubPageHost] embed failed: {ex.Message}"); }
        });
        return Task.CompletedTask;
    }

    public Task CloseEmbeddedAsync()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { DesktopBlankPage.Instance?.CloseEmbeddedPage(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SubPageHost] close failed: {ex.Message}"); }
        });
        return Task.CompletedTask;
    }
}
