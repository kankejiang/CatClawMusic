using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// 下载状态可见性转换器：value 为 DownloadStatus，parameter 为逗号分隔的期望状态集合，
/// 值命中任一状态时返回 true（控件可见）。
/// 用于按任务状态显示暂停/继续/重试/删除等操作按钮。
/// </summary>
public class DownloadStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        var states = parameter.ToString()?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            ?? Array.Empty<string>();
        return states.Contains(value.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
