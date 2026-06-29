using System.Globalization;

namespace CatClawMusic.Maui.Converters;

public class TabIndexToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var currentIndex = value is int i ? i : -1;
        var targetIndex = parameter is int pi ? pi : int.TryParse(parameter?.ToString(), out var parsed) ? parsed : -1;
        return currentIndex == targetIndex ? Color.FromArgb("#9B7ED8") : Color.FromArgb("#E0E0E0");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
