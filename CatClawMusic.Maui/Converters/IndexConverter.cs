using System.Globalization;
using System.Reflection;

namespace CatClawMusic.Maui.Converters;

public class IndexConverter : IValueConverter
{
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

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
