using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;

using WColor = Windows.UI.Color;

namespace Vitrum.Windows.Handlers;

internal static class WindowsBlurExtensions
{
    /// <summary>MAUI Color → WinUI Color。</summary>
    public static WColor ToWindowsColor(this Microsoft.Maui.Graphics.Color color)
        => new WColor
        {
            A = (byte)MathF.Round(color.Alpha * 255f),
            R = (byte)MathF.Round(color.Red * 255f),
            G = (byte)MathF.Round(color.Green * 255f),
            B = (byte)MathF.Round(color.Blue * 255f),
        };

    /// <summary>批量更新合成颜色画刷。</summary>
    public static void UpdateColor(this CompositionColorBrush brush, Microsoft.Maui.Graphics.Color color)
    {
        if (brush == null || color == null) return;
        brush.Color = color.ToWindowsColor();
    }
}