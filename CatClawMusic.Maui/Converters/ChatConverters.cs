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

/// <summary>根据消息角色返回气泡文字颜色：user 气泡用主题色底，白字可读；
/// assistant 气泡用卡片底色（深色/浅色模式会变化），文字跟随主题 TextPrimaryColor。</summary>
public class ChatBubbleTextColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var role = value?.ToString()?.ToLowerInvariant() ?? string.Empty;
        var resources = Application.Current?.Resources;
        return role switch
        {
            "user" => Colors.White,
            "assistant" => resources?["TextPrimaryColor"] as Color ?? Color.FromArgb("#F5F6FF"),
            _ => resources?["TextPrimaryColor"] as Color ?? Color.FromArgb("#F5F6FF")
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
