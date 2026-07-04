using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// 时长格式化转换器，将 TimeSpan、秒数（double/int）或可解析字符串转换为 mm:ss 格式文本。
/// 无效值或非正时长返回 "--:--"。
/// </summary>
public class DurationConverter : IValueConverter
{
    /// <summary>将时长值转换为 mm:ss 格式字符串</summary>
    /// <param name="value">原始值，支持 TimeSpan / double / int / string</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">额外参数（未使用）</param>
    /// <param name="culture">区域性信息</param>
    /// <returns>mm:ss 格式字符串；无效时返回 "--:--"</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        TimeSpan? duration = value switch
        {
            TimeSpan ts => ts,
            double d => TimeSpan.FromSeconds(d),
            int i => TimeSpan.FromSeconds(i),
            string s when TimeSpan.TryParse(s, out var ts) => ts,
            _ => null
        };

        if (!duration.HasValue || duration.Value.TotalSeconds <= 0)
            return "--:--";

        var totalMinutes = (int)duration.Value.TotalMinutes;
        return $"{totalMinutes:D2}:{duration.Value.Seconds:D2}";
    }

    /// <summary>反向转换不支持</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
