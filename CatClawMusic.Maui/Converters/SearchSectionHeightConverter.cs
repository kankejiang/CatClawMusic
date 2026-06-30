using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// Converts item count to section height (52px per item, max 300px).
/// Used to size CollectionView sections inside the search dropdown.
/// </summary>
public class SearchSectionHeightConverter : IValueConverter
{
    private const double ItemHeight = 52;
    private const double MaxHeight = 300;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count && count > 0)
            return Math.Min(count * ItemHeight, MaxHeight);
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Returns true if int count > 0, false otherwise.
/// Used to show/hide search dropdown sections.
/// </summary>
public class CountToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
            return count > 0;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
