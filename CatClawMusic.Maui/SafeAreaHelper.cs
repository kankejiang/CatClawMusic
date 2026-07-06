using Microsoft.Maui.ApplicationModel;

namespace CatClawMusic.Maui;

/// <summary>
/// 跨平台安全区域辅助类，提供系统栏（状态栏/导航栏）高度查询与变更通知。
/// 用于替代 MAUI 默认的 EdgeToEdgeInsets 自动 padding，让背景层延伸到状态栏区域，
/// 仅在内容层手动应用 SafeArea padding。
/// </summary>
public static class SafeAreaHelper
{
    /// <summary>系统栏高度变化时触发（如设备旋转、沉浸式模式切换）</summary>
    public static event EventHandler? SafeAreaChanged;

    private static double _topInset;
    private static double _bottomInset;

    /// <summary>顶部状态栏高度（逻辑像素）</summary>
    public static double TopInset => _topInset;

    /// <summary>底部导航栏高度（逻辑像素）</summary>
    public static double BottomInset => _bottomInset;

    /// <summary>更新安全区域 insets 并在变化时触发事件</summary>
    /// <param name="top">顶部状态栏高度（逻辑像素）</param>
    /// <param name="bottom">底部导航栏高度（逻辑像素）</param>
    public static void UpdateInsets(double top, double bottom)
    {
        var changed = !Math.Abs(_topInset - top).Equals(0) || !Math.Abs(_bottomInset - bottom).Equals(0);
        _topInset = top;
        _bottomInset = bottom;
        if (changed)
        {
            MainThread.BeginInvokeOnMainThread(() => SafeAreaChanged?.Invoke(null, EventArgs.Empty));
        }
    }
}
