using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// 反向字符串转布尔转换器。
/// 字符串为空白或 null 时返回 true；非空时返回 false。
/// 常用于在无封面时显示回退图标。
/// </summary>
public class InvertedStringToBoolConverter : IValueConverter
{
    /// <summary>将值转换为反向布尔值</summary>
    /// <param name="value">原始值，通常为字符串</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">额外参数（未使用）</param>
    /// <param name="culture">区域性信息</param>
    /// <returns>字符串为空白或 null 时返回 true；否则返回 false</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return string.IsNullOrWhiteSpace(str);
        }

        return value == null;
    }

    /// <summary>反向转换不支持</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
