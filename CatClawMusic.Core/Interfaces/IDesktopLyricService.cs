using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 桌面歌词服务接口（跨平台抽象）。
/// Android 平台通过 WindowManager 悬浮窗实现，其他平台空实现。
/// </summary>
public interface IDesktopLyricService
{
    /// <summary>桌面歌词是否正在显示</summary>
    bool IsShowing { get; }

    /// <summary>显示桌面歌词悬浮窗</summary>
    void Show();

    /// <summary>隐藏桌面歌词悬浮窗</summary>
    void Hide();

    /// <summary>更新歌词内容（当前行文本）</summary>
    void UpdateLyric(string? text);

    /// <summary>更新逐字填充进度（0~1，-1 表示不使用逐字效果）</summary>
    void UpdateFillProgress(double progress);

    /// <summary>更新完整歌词数据（用于行切换时预取）</summary>
    void SetLyrics(LrcLyrics? lyrics);

    /// <summary>应用设置变更（字号、颜色、锁定等）</summary>
    void ApplySettings();

    /// <summary>检查是否有悬浮窗权限</summary>
    Task<bool> CheckPermissionAsync();

    /// <summary>请求悬浮窗权限</summary>
    Task<bool> RequestPermissionAsync();
}
