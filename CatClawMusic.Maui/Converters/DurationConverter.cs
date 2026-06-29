using System.Globalization;

namespace CatClawMusic.Maui.Converters;

public class DurationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        TimeSpan? duration = value switch
        {
            TimeSpan ts => ts,
            double d => TimeSpan.FromSeconds(d),
            int i => TimeSpan.FromSeconds(i),
            string s when TimeSpan.TryParse(s, out var ts) => ts,
            _ => null
        };

        if (!duration.HasValue || duration.Value.TotalSeconds <= 0)
            return "--:--";

        var totalMinutes = (int)duration.Value.TotalMinutes;
        return $"{totalMinutes:D2}:{duration.Value.Seconds:D2}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
