using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// 日志级别配色转换器集合：根据级别代码（i/w/e）返回相应的背景色或前景色。
/// 与 Colors.xaml 中 InfoColor/WarningColor/ErrorColor 资源保持一致。
/// </summary>
public class LogLevelBackgroundConverter : IValueConverter
{
    /// <summary>根据级别代码返回半透明背景色</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString()?.ToLowerInvariant() switch
        {
            "e" => Color.FromArgb("#29FF7AAE"), // 错误：粉色半透明
            "w" => Color.FromArgb("#29FFB36B"), // 警告：橙色半透明
            _ => Color.FromArgb("#2955D6FF"),  // 信息：青色半透明
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class LogLevelForegroundConverter : IValueConverter
{
    /// <summary>根据级别代码返回前景文字色</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString()?.ToLowerInvariant() switch
        {
            "e" => Color.FromArgb("#FF7AAE"), // 错误：粉色
            "w" => Color.FromArgb("#FFB36B"), // 警告：橙色
            _ => Color.FromArgb("#55D6FF"),  // 信息：青色
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
