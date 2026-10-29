using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.WinUI.Views;

namespace CatClawMusic.WinUI.Services;

/// <summary>
/// 桌面歌词服务（WinUI 端）：第二窗口承载，实现 Core 的跨平台契约。
/// 权限概念只在 Android 悬浮窗存在，这里恒为已授权。
/// </summary>
public sealed class WinUiDesktopLyricService : IDesktopLyricService
{
    private DesktopLyricWindow? _window;

    public bool IsShowing => _window is not null;

    public void Show()
    {
        if (_window is null)
        {
            _window = new DesktopLyricWindow();
            _window.Closed += (_, _) => _window = null;
        }

        _window.Activate();
    }

    public void Hide()
    {
        _window?.Close();
        _window = null;
    }

    public void UpdateLyric(string? text) => _window?.SetLines(text, null);

    public void UpdateLyricLines(string? currentText, string? nextText, double progress) =>
        _window?.SetLines(currentText, nextText);

    public void UpdateFillProgress(double progress)
    {
        // 逐字填充在窗口里暂未实现（P2 只做整行）。
    }

    public void SetLyrics(LrcLyrics? lyrics)
    {
        // 行数据由调用方按播放位置直接推送，窗口侧不缓存整份歌词。
    }

    public void ApplySettings()
    {
        // 字号/颜色设置项尚未接入。
    }

    public Task<bool> CheckPermissionAsync() => Task.FromResult(true);

    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);
}
