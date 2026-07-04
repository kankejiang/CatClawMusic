using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// 整数转布尔转换器，比较当前索引（value）与目标索引（parameter）是否相等。
/// 常用于 Tab 选中状态判断。
/// </summary>
public class IntToBoolConverter : IValueConverter
{
    /// <summary>比较当前索引与目标索引是否相等</summary>
    /// <param name="value">当前索引（int）</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">目标索引（int 或可解析为 int 的字符串）</param>
    /// <param name="culture">区域性信息</param>
    /// <returns>相等返回 true；否则返回 false</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var currentIndex = value is int i ? i : -1;
        var targetIndex = parameter is int pi ? pi : int.TryParse(parameter?.ToString(), out var parsed) ? parsed : -1;
        return currentIndex == targetIndex;
    }

    /// <summary>反向转换不支持</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
