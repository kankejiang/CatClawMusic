using System.Globalization;

namespace CatClawMusic.Maui.Converters;

public class TabIndexToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var currentIndex = value is int i ? i : -1;
        var targetIndex = parameter is int pi ? pi : int.TryParse(parameter?.ToString(), out var parsed) ? parsed : -1;
        var resources = Application.Current?.Resources;
        var active = resources?["ChipActiveColor"] as Color ?? Color.FromArgb("#8C7BFF");
        var inactive = resources?["ChipInactiveColor"] as Color ?? Color.FromArgb("#18FFFFFF");
        return currentIndex == targetIndex ? active : inactive;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
