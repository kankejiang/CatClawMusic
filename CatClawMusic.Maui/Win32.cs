using System.Runtime.InteropServices;
using System;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using WinRT.Interop;

namespace CatClawMusic.Maui;

/// <summary>
/// Win32/DPI 辅助（照抄 TitlbarWinUI3：github.com/BlameTwo/TitlbarWinUI3 的 Win32.cs）。
/// GetScaleAdjustment 返回窗口所在显示器的 DPI 缩放系数（物理像素 = 逻辑像素 × 系数）。
/// </summary>
public static class Win32
{
    [DllImport("Shcore.dll", SetLastError = true)]
    internal static extern int GetDpiForMonitor(IntPtr hmonitor, Monitor_DPI_Type dpiType, out uint dpiX, out uint dpiY);

    internal enum Monitor_DPI_Type : int
    {
        MDT_Effective_DPI = 0,
        MDT_Angular_DPI = 1,
        MDT_Raw_DPI = 2,
        MDT_Default = MDT_Effective_DPI
    }

    public static double GetScaleAdjustment(Microsoft.UI.Xaml.Window window)
    {
        IntPtr hWnd = WindowNative.GetWindowHandle(window);
        WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
        DisplayArea displayArea = DisplayArea.GetFromWindowId(wndId, DisplayAreaFallback.Primary);
        IntPtr hMonitor = Win32Interop.GetMonitorFromDisplayId(displayArea.DisplayId);

        // Get DPI.
        int result = GetDpiForMonitor(hMonitor, Monitor_DPI_Type.MDT_Default, out uint dpiX, out uint _);
        if (result != 0)
        {
            throw new Exception("没有找到DPI缩放值");
        }

        uint scaleFactorPercent = (uint)(((long)dpiX * 100 + (96 >> 1)) / 96);
        return scaleFactorPercent / 100.0;
    }
}
