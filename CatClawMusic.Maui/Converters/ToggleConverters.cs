using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// 根据布尔值返回开关背景色（开=渐变紫，关=灰）。
/// </summary>
public class AutoScanToggleColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            // 返回渐变色无法做到，用紫色替代
            return Color.FromArgb("#8C7BFF");
        }
        return Color.FromArgb("#2A3870");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 根据布尔值返回开关圆点的水平位置（开=End，关=Start）。
/// </summary>
public class ToggleKnobPositionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return LayoutOptions.End;
        return LayoutOptions.Start;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 将进度值转换为宽度请求（用于进度条）。
/// 自动检测 0-1 或 0-100 范围。
/// </summary>
public class ProgressToWidthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double ratio = 0;
        if (value is double d)
        {
            ratio = d > 1 ? d / 100.0 : d;
        }
        else if (value is int i)
        {
            ratio = i > 1 ? (double)i / 100.0 : i;
        }
        
        if (ratio > 0)
        {
            var maxWidth = parameter is double p ? p : 320.0;
            return Math.Max(4, ratio * maxWidth);
        }
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
