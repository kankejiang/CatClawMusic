using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// 数量转高度转换器，按每项 52px 计算区域高度，最大 300px。
/// 用于设置搜索下拉框内 CollectionView 各分组的尺寸。
/// </summary>
public class SearchSectionHeightConverter : IValueConverter
{
    private const double ItemHeight = 52;
    private const double MaxHeight = 300;

    /// <summary>根据条目数量计算区域高度</summary>
    /// <param name="value">条目数量（int）</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">额外参数（未使用）</param>
    /// <param name="culture">区域性信息</param>
    /// <returns>区域高度（double），最大不超过 300；非正数返回 0</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count && count > 0)
            return Math.Min(count * ItemHeight, MaxHeight);
        return 0.0;
    }

    /// <summary>反向转换不支持</summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// 数量转布尔转换器，当 int 数量大于 0 时返回 true。
/// 用于控制搜索下拉框中各分组的显示/隐藏。
/// </summary>
public class CountToBoolConverter : IValueConverter
{
    /// <summary>判断条目数量是否大于 0</summary>
    /// <param name="value">条目数量（int）</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">额外参数（未使用）</param>
    /// <param name="culture">区域性信息</param>
    /// <returns>数量大于 0 返回 true；否则返回 false</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
            return count > 0;
        return false;
    }

    /// <summary>反向转换不支持</summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
