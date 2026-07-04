using System.Globalization;
using System.Reflection;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// 序号转换器，从对象中按顺序反射读取 Index、Id、TrackNumber、Sequence 属性并返回其字符串形式。
/// 用于在列表中展示行号或轨道序号。
/// </summary>
public class IndexConverter : IValueConverter
{
    /// <summary>从对象中查找常见序号属性并返回其字符串值</summary>
    /// <param name="value">待提取序号的对象</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">额外参数（未使用）</param>
    /// <param name="culture">区域性信息</param>
    /// <returns>序号字符串；未找到时返回 "-"</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "-";

        // 尝试获取常见序号属性
        var properties = new[] { "Index", "Id", "TrackNumber", "Sequence" };
        var type = value.GetType();
        foreach (var propName in properties)
        {
            var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                var propValue = prop.GetValue(value);
                if (propValue != null) return propValue.ToString() ?? "-";
            }
        }

        return "-";
    }

    /// <summary>反向转换不支持</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
