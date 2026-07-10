using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Controls;

public partial class TitleBar : ContentView
{
    public TitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
#if WINDOWS
        // 给拖动区域绑定事件（按钮区域不响应拖动）
        if (DragArea.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement dragNative)
        {
            dragNative.PointerPressed += OnDragAreaPointerPressed;
            dragNative.PointerMoved += OnDragAreaPointerMoved;
            dragNative.PointerReleased += OnDragAreaPointerReleased;
            dragNative.DoubleTapped += OnDragAreaDoubleTapped;
        }
        UpdateMaximizeIcon();
#endif
    }

#if WINDOWS
    // 手动实现按住拖拽：按下捕获指针 → 移动时用 SetWindowPos 跟随 → 松开释放
    private bool _isDragging;
    private int _dragStartMouseX, _dragStartMouseY;
    private int _dragStartWinX, _dragStartWinY, _dragWinW, _dragWinH;
    private Microsoft.UI.Xaml.UIElement? _dragElement;

    private void OnDragAreaPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint((Microsoft.UI.Xaml.UIElement)sender).Properties.IsLeftButtonPressed)
            return;
        if (App.CurrentAppWindow == null) return;

        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(App.CurrentAppWindow.Id);

        // 最大化状态下不允许拖动
        if (App.CurrentAppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p
            && p.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized)
            return;

        // 记录起始鼠标屏幕坐标
        GetCursorPos(out var pt);
        _dragStartMouseX = pt.X;
        _dragStartMouseY = pt.Y;

        // 记录起始窗口坐标与尺寸
        GetWindowRect(hwnd, out var rc);
        _dragStartWinX = rc.Left;
        _dragStartWinY = rc.Top;
        _dragWinW = rc.Right - rc.Left;
        _dragWinH = rc.Bottom - rc.Top;

        _isDragging = true;
        _dragElement = (Microsoft.UI.Xaml.UIElement)sender;
        _dragElement.CapturePointer(e.Pointer); // 捕获指针确保持续接收 PointerMoved
        e.Handled = true;
    }

    private void OnDragAreaPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDragging || App.CurrentAppWindow == null) return;

        GetCursorPos(out var pt);
        var dx = pt.X - _dragStartMouseX;
        var dy = pt.Y - _dragStartMouseY;

        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(App.CurrentAppWindow.Id);
        // 按住移动时窗口跟随鼠标
        SetWindowPos(hwnd, IntPtr.Zero, _dragStartWinX + dx, _dragStartWinY + dy,
            _dragWinW, _dragWinH, SWP_NOZORDER | SWP_NOACTIVATE);
        e.Handled = true;
    }

    private void OnDragAreaPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        _dragElement?.ReleasePointerCapture(e.Pointer);
        _dragElement = null;
        e.Handled = true;
    }

    private void OnDragAreaDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void UpdateMaximizeIcon()
    {
        if (App.CurrentAppWindow?.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter)
            return;

        var isMaximized = presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;
        MaximizeIcon.IsVisible = !isMaximized;
        RestoreIcon.IsVisible = isMaximized;
    }
#endif

    private void OnMinimizeTapped(object? sender, TappedEventArgs e)
    {
#if WINDOWS
        if (App.CurrentAppWindow == null) return;
        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(App.CurrentAppWindow.Id);
        _ = ShowWindow(hwnd, SW_MINIMIZE);
#endif
    }

    private void OnMaximizeTapped(object? sender, TappedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnCloseTapped(object? sender, TappedEventArgs e)
    {
#if WINDOWS
        if (App.CurrentAppWindow == null) return;
        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(App.CurrentAppWindow.Id);
        _ = PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
#endif
    }

    private void ToggleMaximize()
    {
#if WINDOWS
        if (App.CurrentAppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            if (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized)
                presenter.Restore();
            else
                presenter.Maximize();

            UpdateMaximizeIcon();
        }
#endif
    }

#if WINDOWS
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    private const int SW_MINIMIZE = 6;
    private const uint WM_CLOSE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
#endif
}
