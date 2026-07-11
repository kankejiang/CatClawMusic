using System.Globalization;

namespace CatClawMusic.Maui.Converters;

public class IsAssistantConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString()?.ToLowerInvariant() == "assistant";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class IsUserConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString()?.ToLowerInvariant() == "user";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ChatBubbleColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var role = value?.ToString()?.ToLowerInvariant() ?? string.Empty;
        var resources = Application.Current?.Resources;
        return role switch
        {
            "user" => resources?["PrimaryColor"] as Color ?? Color.FromArgb("#8B5CF6"),
            "assistant" => resources?["CardBackgroundStrongColor"] as Color ?? Color.FromArgb("#30FFFFFF"),
            _ => resources?["CardBackgroundColor"] as Color ?? Color.FromArgb("#22FFFFFF")
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ChatBubbleAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var role = value?.ToString()?.ToLowerInvariant() ?? string.Empty;
        return role == "user" ? LayoutOptions.End : LayoutOptions.Start;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
