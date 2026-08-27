using System.Collections;

namespace CatClawMusic.Core.ColorQuantize;

public interface IColorPalette : IReadOnlyCollection<PixelRGB>
{
    PixelRGB Map(PixelRGB color);
}

/// <summary>
/// 고유 색상이 목표치보다 적을 때 사용되는 간단한 색상 맵입니다.
/// </summary>
internal class SimpleColorMap : IColorPalette
{
    private readonly IReadOnlyCollection<PixelRGB> _colors;
    public int Count => _colors.Count;

    public SimpleColorMap(IReadOnlyCollection<PixelRGB> colors)
    {
        _colors = colors;
    }

    public PixelRGB Map(PixelRGB color)
    {
        return RGBAlgorithms.Nearest(_colors, color);
    }

    public IEnumerator<PixelRGB> GetEnumerator()
    {
        return _colors.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

/// <summary>
/// MMCQ 알고리즘으로 생성된 양자화된 색상 맵입니다.
/// </summary>
internal class CMap : IColorPalette
{
    private readonly List<VBox> _vboxes;
    private readonly List<PixelRGB> _palette;
    public int Count => _palette.Count;

    public CMap(List<VBox> vboxes)
    {
        _vboxes = vboxes;
        _palette = new List<PixelRGB>();
        foreach (var vbox in vboxes)
        {
            _palette.Add(vbox.Avg);
        }
    }

    public PixelRGB Map(PixelRGB color)
    {
        foreach (var vbox in _vboxes)
        {
            if (vbox.Contains(color))
            {
                return vbox.Avg;
            }
        }
        return RGBAlgorithms.Nearest(_palette, color);
    }

    public IEnumerator<PixelRGB> GetEnumerator()
    {
        return _palette.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

internal static class RGBAlgorithms
{
    public static PixelRGB Nearest(IEnumerable<PixelRGB> palette, PixelRGB color)
    {
        var minDistance = double.MaxValue;
        PixelRGB closestColor = default;

        foreach (var pColor in palette)
        {
            double distance = Math.Sqrt(
                Math.Pow(color.R - pColor.R, 2) +
                Math.Pow(color.G - pColor.G, 2) +
                Math.Pow(color.B - pColor.B, 2)
            );

            if (distance < minDistance)
            {
                minDistance = distance;
                closestColor = pColor;
            }
        }

        return closestColor;
    }
}