using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// Tab 索引转背景色转换器。
/// 当前索引等于目标索引时返回 ChipActiveColor，否则返回 ChipInactiveColor。
/// </summary>
public class TabIndexToColorConverter : IValueConverter
{
    /// <summary>根据当前索引与目标索引是否相等返回对应背景色</summary>
    /// <param name="value">当前索引（int）</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">目标索引（int 或可解析为 int 的字符串）</param>
    /// <param name="culture">区域性信息</param>
    /// <returns>相等返回 ChipActiveColor，否则返回 ChipInactiveColor</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var currentIndex = value is int i ? i : -1;
        var targetIndex = parameter is int pi ? pi : int.TryParse(parameter?.ToString(), out var parsed) ? parsed : -1;
        var resources = Application.Current?.Resources;
        var active = resources?["ChipActiveColor"] as Color ?? Color.FromArgb("#8C7BFF");
        var inactive = resources?["ChipInactiveColor"] as Color ?? Color.FromArgb("#18FFFFFF");
        return currentIndex == targetIndex ? active : inactive;
    }

    /// <summary>反向转换不支持</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
