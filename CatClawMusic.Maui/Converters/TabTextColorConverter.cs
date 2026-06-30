using System.Globalization;

namespace CatClawMusic.Maui.Converters;

public class TabTextColorConverter : IValueConverter
{
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

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
