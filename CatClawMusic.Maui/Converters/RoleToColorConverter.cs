using System.Globalization;

namespace CatClawMusic.Maui.Converters;

public class RoleToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var role = value?.ToString()?.ToLowerInvariant() ?? string.Empty;
        return role switch
        {
            "user" => Color.FromArgb("#9B7ED8"),
            "assistant" => Color.FromArgb("#F5F5F5"),
            "system" => Color.FromArgb("#FF6B9D"),
            _ => Color.FromArgb("#E0E0E0")
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
