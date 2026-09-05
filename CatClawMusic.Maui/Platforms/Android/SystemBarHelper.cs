using System;
using Android.Views;
using AndroidX.Core.View;
using Microsoft.Maui.ApplicationModel;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// 系统状态栏显隐统一入口。
/// MIUI/HyperOS 实测对纯 InsetsController.Hide 不生效（2026-09-05），故双管齐下：
/// WindowManagerFlags.Fullscreen（MIUI 最稳）+ WindowInsetsControllerCompat（原生标准）。
/// 取代 DesktopMainPage 里 legacy SystemUiVisibility + ImmersiveSticky 方案——
/// sticky 标志在旋转/切回竖屏时残留，导致竖屏状态栏消失（2026-09-05 修复）。
/// </summary>
public static class SystemBarHelper
{
    /// <summary>显示或隐藏系统状态栏。隐藏后边缘下滑仍可临时呼出（transitory insets）。</summary>
    public static void SetStatusBarVisible(bool visible)
    {
        try
        {
            var window = Platform.CurrentActivity?.Window;
            if (window == null) return;

            if (visible)
                window.ClearFlags(WindowManagerFlags.Fullscreen);
            else
                window.AddFlags(WindowManagerFlags.Fullscreen);

            var controller = WindowCompat.GetInsetsController(window, window.DecorView);
            if (controller == null) return;
            // 边缘手势临时呼出后自动隐藏（对应旧 ImmersiveSticky 语义）
            controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowBarsBySwipe;
            if (visible)
                controller.Show(WindowInsetsCompat.Type.StatusBars());
            else
                controller.Hide(WindowInsetsCompat.Type.StatusBars());
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("SystemBar", $"SetStatusBarVisible({visible}) failed: {ex.Message}");
        }
    }
}
