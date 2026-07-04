using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>布尔取反转换器：true -> false，false -> true</summary>
public class InvertedBoolConverter : IValueConverter
{
    /// <summary>将布尔值取反</summary>
    /// <param name="value">原始布尔值</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">额外参数（未使用）</param>
    /// <param name="culture">区域性信息</param>
    /// <returns>取反后的布尔值；非布尔值返回 true</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return true;
    }

    /// <summary>反向转换，同样对布尔值取反</summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return false;
    }
}

/// <summary>布尔值转颜色转换器：true 返回 LikeColor（粉色），false 返回 TextSecondaryColor</summary>
public class BoolToColorConverter : IValueConverter
{
    /// <summary>将布尔值映射为对应颜色</summary>
    /// <param name="value">布尔值</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">额外参数（未使用）</param>
    /// <param name="culture">区域性信息</param>
    /// <returns>true 时返回 LikeColor（粉色），false 时返回 TextSecondaryColor</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var resources = Application.Current?.Resources;
        if (value is bool b && b)
            return resources?["LikeColor"] as Color ?? Color.FromArgb("#FF4081");
        return resources?["TextSecondaryColor"] as Color ?? Colors.Gray;
    }

    /// <summary>反向转换不支持</summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
