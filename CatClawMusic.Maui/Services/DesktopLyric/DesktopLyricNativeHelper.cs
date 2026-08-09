using System.Runtime.InteropServices;

namespace CatClawMusic.Maui.Services.DesktopLyric;

internal static class DesktopLyricNativeHelper
{
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    // DWM ——扩展到客户区，亚克力模糊的前提
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);
    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int cxLeftWidth; public int cxRightWidth; public int cyTopHeight; public int cyBottomHeight; }

    // Acrylic / Blur
    private enum AccentState { ACCENT_ENABLE_BLURBEHIND = 3, ACCENT_ENABLE_ACRYLICBLURBEHIND = 4 }
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }
    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public static void SetTopMost(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public static void MoveWindowTo(IntPtr hwnd, int x, int y) =>
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    /// <summary>完整透明窗口设置：DWM 扩展 + 亚克力模糊</summary>
    public static void ApplyFullAcrylic(IntPtr hwnd, int alpha)
    {
        // Step 1: DWM 扩展——告诉桌面窗口管理器整窗都可以做模糊
        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        // Step 2: Win32 亚克力
        var accentColor = (alpha & 0xFF) << 24;
        if (!TryApplyAccent(hwnd, (int)AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, accentColor))
            TryApplyAccent(hwnd, (int)AccentState.ACCENT_ENABLE_BLURBEHIND, accentColor);
    }

    /// <summary>
    /// DWM 扩展客户区（margins=-1）：整窗可透明/可模糊（WinUI 3 透明窗口前提，
    /// 网易云式半透明黑底方案核心；对任何窗口形态都生效，不依赖亚克力渲染）。
    /// </summary>
    public static void ExtendFrameIntoClientArea(IntPtr hwnd)
    {
        try
        {
            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        catch { }
    }

#if WINDOWS
    /// <summary>
    /// ★ 去除 WinUI 3 窗口的 Composition Visual 层黑底（透明窗口关键，微软官方答案）：
    /// WinUI 3 窗口背景有两层——Win32 窗口层（DwmExtendFrameIntoClientArea 解决）+
    /// DesktopWindowXamlSource 的 Composition Visual 层（默认黑底，只能通过
    /// ICompositionSupportsSystemBackdrop.SystemBackdrop = 透明画刷移除，
    /// XAML 控件背景再透明也没用）。清掉后窗口才真正透出桌面。
    /// </summary>
    public static void ClearCompositionBlackBackdrop(Microsoft.UI.Xaml.Window nativeWin)
    {
        try
        {
            if (nativeWin is not Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop host) return;
            // SystemBackdrop 属性类型为 Windows.UI.Composition.CompositionBrush，
            // 需用 Windows.UI.Composition.Compositor 创建透明画刷（Microsoft.UI 命名空间不兼容）
            var compositor = new Windows.UI.Composition.Compositor();
            host.SystemBackdrop = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }
        catch { }
    }
#endif

    private static bool TryApplyAccent(IntPtr hwnd, int state, int color)
    {
        try
        {
            var a = new AccentPolicy { AccentState = state, AccentFlags = 2, GradientColor = color };
            var d = new WindowCompositionAttributeData
            {
                Attribute = 19,
                Data = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>()),
                SizeOfData = Marshal.SizeOf<AccentPolicy>()
            };
            try { Marshal.StructureToPtr(a, d.Data, false); return SetWindowCompositionAttribute(hwnd, ref d) != 0; }
            finally { Marshal.FreeHGlobal(d.Data); }
        }
        catch { return false; }
    }

#if WINDOWS
    /// <summary>
    /// 窗口内容根容器设为透明：MAUI 默认根背景不透明（主题色/白），会盖住透明/毛玻璃。
    /// 深度递归清窗口 chrome 容器链（Window.Content → MAUI 根容器 → 页面宿主），
    /// 页面层（RootLayout 半透明黑底）由 ApplyLook/SetBackgroundOpacity 之后重新施加，
    /// 因此这里可以放心深度清（深度 6 足够覆盖 MAUI 窗口根结构）。
    /// </summary>
    public static void MakeRootTransparent(Microsoft.UI.Xaml.Window nativeWin)
    {
        try { ClearBackgroundDeep(nativeWin.Content, 0); }
        catch { }
    }

    private static void ClearBackgroundDeep(Microsoft.UI.Xaml.DependencyObject? el, int depth)
    {
        if (el == null || depth > 6) return;
        try
        {
            var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            switch (el)
            {
                case Microsoft.UI.Xaml.Controls.Panel panel:
                    panel.Background = transparent;
                    foreach (var c in panel.Children) ClearBackgroundDeep(c, depth + 1);
                    return;
                case Microsoft.UI.Xaml.Controls.Border border:
                    border.Background = transparent;
                    if (border.Child is Microsoft.UI.Xaml.DependencyObject bc) ClearBackgroundDeep(bc, depth + 1);
                    return;
                case Microsoft.UI.Xaml.Controls.ContentControl cc:
                    cc.Background = transparent;
                    if (cc.Content is Microsoft.UI.Xaml.DependencyObject ccc) ClearBackgroundDeep(ccc, depth + 1);
                    return;
                case Microsoft.UI.Xaml.Controls.ContentPresenter cp:
                    cp.Background = transparent;
                    return;
            }
        }
        catch { }
    }
#endif

#if WINDOWS
    public static double GetScaleAdjustment(Microsoft.UI.Xaml.Window nativeWin)
    {
        try { return GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(nativeWin)) / 96.0; }
        catch { return 1.0; }
    }

    /// <summary>通过 CreateForContextMenu 隐藏系统标题栏和按钮</summary>
    public static void HideSystemTitleBar(Microsoft.UI.Windowing.AppWindow appWindow)
    {
        try { appWindow.SetPresenter(Microsoft.UI.Windowing.OverlappedPresenter.CreateForContextMenu()); }
        catch { }
    }

    /// <summary>设置拖拽区域（全窗可拖动）</summary>
    public static void SetFullDragRegion(Microsoft.UI.Windowing.AppWindow appWindow,
        Microsoft.UI.Xaml.Window nativeWin, double fontSize)
    {
        try
        {
            var s = GetScaleAdjustment(nativeWin);
            appWindow.TitleBar.SetDragRectangles(new[]
            {
                new global::Windows.Graphics.RectInt32 { X = 0, Y = 0, Width = (int)(980 * s), Height = (int)(Math.Max(160, fontSize * 6.4) * s) }
            });
        }
        catch { }
    }
#endif

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 0x80000;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;

#if WINDOWS
    /// <summary>移除窗口边框：去 WS_CAPTION（标题栏）+ WS_THICKFRAME（resize 边框）+ WS_SYSMENU（系统菜单）。
    /// 标准 OverlappedPresenter 窗口默认带 ~8px resize 边框 + 标题栏区域，在透明窗口上表现为可见边框。
    /// 去除后窗口变为纯客户区（无边框无标题栏），DwmExtendFrameIntoClientArea 仍正常生效（透明不依赖窗口形态）。</summary>
    public static void RemoveSystemButtons(IntPtr hwnd)
    {
        try
        {
            var style = GetWindowLongPtr(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE, style & ~(WS_SYSMENU | WS_CAPTION | WS_THICKFRAME));
            // SWP_FRAMECHANGED (0x0200) 触发 WM_NCCALCSIZE 重算客户区
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | 0x0200);
        }
        catch { }
    }
#endif
}