using System.Globalization;

namespace CatClawMusic.Maui.Converters;

public class RoleToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var role = value?.ToString()?.ToLowerInvariant() ?? string.Empty;
        var resources = Application.Current?.Resources;
        var user = resources?["ChipActiveColor"] as Color ?? Color.FromArgb("#8C7BFF");
        var assistant = resources?["CardBackgroundStrongColor"] as Color ?? Color.FromArgb("#30FFFFFF");
        var system = resources?["LikeColor"] as Color ?? Color.FromArgb("#FF7AAE");
        var fallback = resources?["CardBackgroundColor"] as Color ?? Color.FromArgb("#22FFFFFF");
        return role switch
        {
            "user" => user,
            "assistant" => assistant,
            "system" => system,
            _ => fallback
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
