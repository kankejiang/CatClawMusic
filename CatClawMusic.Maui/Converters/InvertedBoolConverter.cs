using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>Inverts a boolean value: true -> false, false -> true</summary>
public class InvertedBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return false;
    }
}

/// <summary>Converts bool to a color: true -> LikeColor (pink), false -> TextSecondaryColor</summary>
public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var resources = Application.Current?.Resources;
        if (value is bool b && b)
            return resources?["LikeColor"] as Color ?? Color.FromArgb("#FF4081");
        return resources?["TextSecondaryColor"] as Color ?? Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
