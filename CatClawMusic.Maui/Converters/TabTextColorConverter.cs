using System.Globalization;

namespace CatClawMusic.Maui.Converters;

public class TabTextColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var currentTab = value?.ToString();
        // 当前选项卡激活时显示白色，未激活时显示浅灰色
        return currentTab?.Equals(parameter?.ToString(), StringComparison.OrdinalIgnoreCase) == true
            ? Colors.White
            : Colors.LightGray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
