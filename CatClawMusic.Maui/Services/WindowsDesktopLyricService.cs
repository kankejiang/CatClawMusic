using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using Microsoft.Maui.Controls.Shapes;
using System.Runtime.InteropServices;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// Windows 桌面歌词服务：基于 MAUI 独立窗口 + Win32/WinUI 原生层实现悬浮歌词。
/// 特性：无边框、置顶（SetWindowPos HWND_TOPMOST）、整窗可拖动（SetDragRectangles，锁定后禁用）、
/// 半透明毛玻璃背景（SetWindowCompositionAttribute，Win10 模糊 / Win11 亚克力）、位置持久化（DesktopPosY）。
/// 歌词显示：当前行大字高亮 + 下一行小字预览。
/// </summary>
public class WindowsDesktopLyricService : IDesktopLyricService
{
    private const string Tag = "WinDesktopLyric";

    private static readonly Lazy<ILogService> Log = new(() =>
        MauiProgram.Services.GetRequiredService<ILogService>());

    private Microsoft.Maui.Controls.Window? _window;
    private ContentPage? _page;
    private Border? _card;
    private Label? _currentLabel;
    private Label? _nextLabel;

    private LrcLyrics? _lyrics;
    private string _currentText = "";
    private int _nextIndex = -1;

    private LyricsSettingsService Settings => LyricsSettingsService.Instance;

    /// <summary>窗口是否正在显示（窗口对象存活即视为显示中）。</summary>
    public bool IsShowing => _window != null;

    public void Show()
    {
        if (_window != null) return;
        BuildWindow();
        Application.Current?.OpenWindow(_window);
    }

    public void Hide()
    {
        if (_window == null) return;
        var win = _window;
        _window = null;
        Application.Current?.CloseWindow(win);
    }

    public void UpdateLyric(string? text)
    {
        _currentText = text ?? "";
        MainThread.BeginInvokeOnMainThread(UpdateLabels);
    }

    public void UpdateFillProgress(double progress)
    {
        // 逐字填充效果暂未实现（Windows 端 v1 为整行高亮）。
    }

    public void SetLyrics(LrcLyrics? lyrics)
    {
        _lyrics = lyrics;
        _nextIndex = -1;
    }

    public void ApplySettings()
    {
        MainThread.BeginInvokeOnMainThread(ApplyStyle);
    }

    public Task<bool> CheckPermissionAsync() => Task.FromResult(true);

    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);

    // ═══════════════════════════════════════════
    // 窗口构建
    // ═══════════════════════════════════════════

    private void BuildWindow()
    {
        var fontSize = Settings.DesktopFontSize;

        // 半透明圆角背景卡：透明度由 DesktopBgOpacity 控制（0=全透明，1=全黑）
        _card = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#26FFFFFF"),
            Padding = new Thickness(28, 14),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
        };

        _currentLabel = new Label
        {
            FontFamily = "OpenSansSemibold",
            HorizontalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Fill,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
        };

        _nextLabel = new Label
        {
            FontSize = Math.Max(10, fontSize * 0.62),
            HorizontalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Fill,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            Opacity = 0.75,
        };

        var stack = new VerticalStackLayout { Spacing = 6 };
        stack.Children.Add(_currentLabel);
        stack.Children.Add(_nextLabel);
        _card.Content = stack;

        _page = new ContentPage
        {
            BackgroundColor = Colors.Transparent,
            Content = _card,
        };

        _window = new Microsoft.Maui.Controls.Window(_page) { Title = "" };
        _window.HandlerChanged += OnWindowHandlerChanged;
        _window.Destroying += (_, _) => { _window = null; };

        ApplyStyle();
    }

    /// <summary>应用当前设置：背景透明度、字号、颜色、锁定状态。</summary>
    private void ApplyStyle()
    {
        if (_card == null || _currentLabel == null || _nextLabel == null) return;

        var opacity = Settings.DesktopBgOpacity;
        // 完全透明时给个极小底色，避免卡片隐形（文字仍可见）
        _card.BackgroundColor = Color.FromRgba(0, 0, 0, Math.Clamp(opacity, 0.08, 1.0));

        var fontSize = Settings.DesktopFontSize;
        _currentLabel.FontSize = fontSize;
        _currentLabel.TextColor = Color.FromArgb(Settings.DesktopHighlightColor);
        _nextLabel.FontSize = Math.Max(10, fontSize * 0.62);
        _nextLabel.TextColor = Color.FromArgb(Settings.DesktopTextColor);

        ApplyDragRegion();
    }

    /// <summary>更新当前行文本与下一行预览（主线程调用）。</summary>
    private void UpdateLabels()
    {
        if (_currentLabel == null || _nextLabel == null) return;

        _currentLabel.Text = _currentText;

        // 通过文本匹配推导下一行（用于预览）；失败则不显示
        _nextIndex = -1;
        if (!string.IsNullOrEmpty(_currentText) && _lyrics != null)
        {
            var idx = _lyrics.Lines.FindIndex(l => l.Text == _currentText);
            if (idx >= 0 && idx + 1 < _lyrics.Lines.Count)
                _nextIndex = idx + 1;
        }
        _nextLabel.Text = _nextIndex >= 0 ? _lyrics!.Lines[_nextIndex].Text : "";
        _nextLabel.IsVisible = _nextIndex >= 0;
    }

    // ═══════════════════════════════════════════
    // WinUI/Win32 原生层：无边框 / 置顶 / 毛玻璃 / 拖动 / 位置
    // ═══════════════════════════════════════════

    private void OnWindowHandlerChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        try
        {
            if (_window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWin) return;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWin);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

            // ① 无边框：内容延伸到标题栏区域（保留标题栏实体——移除会导致 SetDragRectangles 失效）
            appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            nativeWin.SetTitleBar(null);

            // ①+ 完全隐藏系统标题栏（OverlappedPresenter.CreateForContextMenu 模式）
            // 参考: https://www.zhihu.com/tardis/bd/art/604236195
            try
            {
                var customPresenter = Microsoft.UI.Windowing.OverlappedPresenter.CreateForContextMenu();
                appWindow.SetPresenter(customPresenter);
            }
            catch { }
            // ② 置顶（Win32 HWND_TOPMOST，兼容所有 Windows App SDK 版本）
            NativeMethods.SetTopMost(hwnd);

            // ③ 半透明毛玻璃背景（Win11 亚克力，降级 Win10 模糊；失败则实心深色背景）
            NativeMethods.ApplyAcrylicBackdrop(hwnd,
                (int)(Math.Clamp(Settings.DesktopBgOpacity, 0.08, 1.0) * 255));

            // ④ 尺寸与位置：默认屏幕水平居中，垂直按 DesktopPosY（屏幕高度比例）
            var scale = Win32.GetScaleAdjustment(nativeWin);
            var work = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
            var w = (int)(980 * scale);
            var h = (int)(Math.Max(84, Settings.DesktopFontSize * 3.4) * scale);
            var x = work.X + (work.Width - w) / 2;
            var y = work.Y + (int)(work.Height * Math.Clamp(Settings.DesktopPosY, 0.1, 0.95));
            appWindow.MoveAndResize(new global::Windows.Graphics.RectInt32 { X = x, Y = y, Width = w, Height = h });

            // ⑤ 拖动后持久化垂直位置（下次打开/设置恢复用）
            appWindow.Changed += (_, args) =>
            {
                if (!args.DidPositionChange) return;
                try
                {
                    var rel = (appWindow.Position.Y - work.Y) / (double)work.Height;
                    Settings.DesktopPosY = Math.Clamp(rel, 0.1, 0.95);
                }
                catch { /* 位置保存失败忽略 */ }
            };

            ApplyDragRegion();
            Log.Value.Debug(Tag, $"Window ready: {w}x{h} @({x},{y})");
        }
        catch (Exception ex)
        {
            Log.Value.Debug(Tag, $"HandlerChanged failed: {ex.Message}");
        }
#endif
    }

    /// <summary>设置拖动区域：解锁 → 整窗可拖；锁定 → 不可拖。</summary>
    private void ApplyDragRegion()
    {
#if WINDOWS
        try
        {
            if (_window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWin) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWin);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            if (appWindow?.TitleBar == null) return;

            if (Settings.DesktopLocked)
            {
                appWindow.TitleBar.SetDragRectangles(Array.Empty<global::Windows.Graphics.RectInt32>());
            }
            else
            {
                var scale = Win32.GetScaleAdjustment(nativeWin);
                appWindow.TitleBar.SetDragRectangles(new[]
                {
                    new global::Windows.Graphics.RectInt32
                    {
                        X = 0, Y = 0,
                        Width = (int)(980 * scale),
                        Height = (int)(Math.Max(84, Settings.DesktopFontSize * 3.4) * scale),
                    }
                });
            }
        }
        catch { /* 拖动区设置失败不影响显示 */ }
#endif
    }

    /// <summary>Win32 原生方法（与平台 SDK 版本无关，兼容性最好）。</summary>
    private static class NativeMethods
    {
        public static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor; // ABGR
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd,
            ref WindowCompositionAttributeData data);

        public static void ApplyAcrylicBackdrop(IntPtr hwnd, int darkAlpha)
        {
            // 高 8 位 = 背景着色强度（0x00 最透明，0xFF 最实）
            var accentColor = (darkAlpha & 0xFF) << 24;

            // 优先亚克力（Win11），失败降级普通模糊（Win10）
            if (!TryApplyAccent(hwnd, (int)AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, accentColor))
                TryApplyAccent(hwnd, (int)AccentState.ACCENT_ENABLE_BLURBEHIND, accentColor);
        }

        private static bool TryApplyAccent(IntPtr hwnd, int accentState, int color)
        {
            try
            {
                var accent = new AccentPolicy
                {
                    AccentState = accentState,
                    AccentFlags = 2, // DrawAllBorders
                    GradientColor = color,
                };
                var data = new WindowCompositionAttributeData
                {
                    Attribute = 19, // WCA_ACCENT_POLICY
                    Data = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>()),
                    SizeOfData = Marshal.SizeOf<AccentPolicy>(),
                };
                try
                {
                    Marshal.StructureToPtr(accent, data.Data, false);
                    return SetWindowCompositionAttribute(hwnd, ref data) != 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(data.Data);
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SetTopMost(IntPtr hwnd)
            => SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
