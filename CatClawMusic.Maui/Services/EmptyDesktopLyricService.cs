using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 桌面歌词服务空实现（Windows 等不支持悬浮窗的平台使用）。
/// </summary>
public class EmptyDesktopLyricService : IDesktopLyricService
{
    public bool IsShowing => false;

    public void Show() { }
    public void Hide() { }
    public void UpdateLyric(string? text) { }
    public void UpdateFillProgress(double progress) { }
    public void SetLyrics(LrcLyrics? lyrics) { }
    public void ApplySettings() { }
    public Task<bool> CheckPermissionAsync() => Task.FromResult(true);
    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);
}
