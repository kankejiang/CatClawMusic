using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// Tab 文本转文字颜色转换器。
/// 当前 Tab 名称与目标参数（不区分大小写）相等时返回 ChipActiveTextColor，否则返回 ChipInactiveTextColor。
/// </summary>
public class TabTextColorConverter : IValueConverter
{
    /// <summary>根据当前 Tab 名称与目标参数是否相等返回对应文字颜色</summary>
    /// <param name="value">当前 Tab 名称字符串</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">目标 Tab 名称字符串</param>
    /// <param name="culture">区域性信息</param>
    /// <returns>相等返回 ChipActiveTextColor，否则返回 ChipInactiveTextColor</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var currentTab = value?.ToString();
        var resources = Application.Current?.Resources;
        var active = resources?["ChipActiveTextColor"] as Color ?? Colors.White;
        var inactive = resources?["ChipInactiveTextColor"] as Color ?? Colors.LightGray;
        return currentTab?.Equals(parameter?.ToString(), StringComparison.OrdinalIgnoreCase) == true
            ? active
            : inactive;
    }

    /// <summary>反向转换不支持</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
