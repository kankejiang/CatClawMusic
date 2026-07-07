#if WINDOWS
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace CatClawMusic.Maui.Platforms.Windows;

/// <summary>
/// Windows 平台文件夹选择器：调用 WinUI 的 <see cref="Windows.Storage.Pickers.FolderPicker"/>，
/// 返回所选文件夹的真实文件系统路径。该路径可直接交由本地扫描服务按路径递归扫描。
/// 由于本应用为未打包（WindowsPackageType=None）的 WinUI 程序，必须先用
/// <see cref="InitializeWithWindow.Initialize(object, IntPtr)"/> 绑定拥有者窗口句柄，否则选择器会抛异常。
/// 本实现对窗口句柄的获取做了多重回退（MAUI 窗口 → Win32 前台/激活窗口），避免句柄为空导致选择器静默失败。
/// </summary>
public static class WindowsFolderPicker
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>打开系统文件夹选择器，返回所选文件夹的真实路径；用户取消时返回 null</summary>
    /// <returns>所选文件夹的完整路径，取消或出错时为 null</returns>
    public static async Task<string?> PickFolderAsync()
    {
        try
        {
            var picker = new global::Windows.Storage.Pickers.FolderPicker();

            var hwnd = GetWindowHandle();
            if (hwnd != IntPtr.Zero)
            {
                InitializeWithWindow.Initialize(picker, hwnd);
            }
            else
            {
                // 未打包 WinUI 应用若无窗口句柄，PickSingleFolderAsync 会抛异常；
                // 这里仅记录，交给外层 catch 处理并返回 null，由调用方提示用户。
                System.Diagnostics.Debug.WriteLine("[WindowsFolderPicker] 无法获取窗口句柄，文件夹选择器可能无法正常弹出。");
            }

            var folder = await picker.PickSingleFolderAsync();
            System.Diagnostics.Debug.WriteLine($"[WindowsFolderPicker] 选择结果: {(folder == null ? "null(取消)" : folder.Path)}");
            return folder?.Path;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsFolderPicker] Pick error: {ex}");
            return null;
        }
    }

    /// <summary>
    /// 获取当前窗口的 HWND，用于绑定 FolderPicker 的拥有者窗口。
    /// 优先使用 MAUI 当前激活窗口对应的 WinUI 窗口；若失败，则回退到 Win32 的
    /// 前台/激活窗口句柄（在按钮点击时本应用即为前台窗口，结果可靠）。
    /// </summary>
    private static IntPtr GetWindowHandle()
    {
        // 1) 优先：MAUI 当前窗口 → WinUI 窗口 → HWND
        try
        {
            var mauiWindow = Microsoft.Maui.Controls.Application.Current?
                .Windows?
                .FirstOrDefault();
            if (mauiWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window win)
            {
                return WindowNative.GetWindowHandle(win);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsFolderPicker] MAUI 窗口句柄获取失败: {ex}");
        }

        // 2) 回退：Win32 前台 / 激活窗口句柄
        try
        {
            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero) return hwnd;

            hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero) return hwnd;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsFolderPicker] Win32 窗口句柄获取失败: {ex}");
        }

        return IntPtr.Zero;
    }
}
#endif
