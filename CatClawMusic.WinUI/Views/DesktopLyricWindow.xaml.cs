using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace CatClawMusic.WinUI.Views;

/// <summary>
/// 桌面歌词窗口（对照 MAUI 的 Windows 第二窗口实现）：
/// 无边框、置顶、不进任务栏；按住任意位置拖动。
/// </summary>
public sealed partial class DesktopLyricWindow : Window
{
    private const int Width = 900;
    private const int Height = 150;

    private bool _dragging;
    private PointInt32 _dragStartWindow;
    private PointInt32 _dragStartCursor;

    public DesktopLyricWindow()
    {
        InitializeComponent();

        Title = "猫爪桌面歌词";

        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        try
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch
        {
            // Win10 早期版本不支持亚克力时退回纯色底。
            RootGrid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xE6, 0x11, 0x14, 0x1D));
        }

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + (workArea.Width - Width) / 2,
            workArea.Y + workArea.Height - Height - 60,
            Width,
            Height));

        RootGrid.PointerPressed += OnPointerPressed;
        RootGrid.PointerMoved += OnPointerMoved;
        RootGrid.PointerReleased += OnPointerReleased;
    }

    public void SetLines(string? current, string? next)
    {
        CurrentText.Text = string.IsNullOrWhiteSpace(current) ? "♪" : current;
        NextText.Text = next ?? string.Empty;
        NextText.Visibility = string.IsNullOrWhiteSpace(next) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!GetCursorPos(out var cursor))
            return;

        _dragging = true;
        _dragStartCursor = new PointInt32(cursor.X, cursor.Y);
        _dragStartWindow = AppWindow.Position;
        RootGrid.CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || !GetCursorPos(out var cursor))
            return;

        AppWindow.Move(new PointInt32(
            _dragStartWindow.X + cursor.X - _dragStartCursor.X,
            _dragStartWindow.Y + cursor.Y - _dragStartCursor.Y));
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        RootGrid.ReleasePointerCapture(e.Pointer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);
}
