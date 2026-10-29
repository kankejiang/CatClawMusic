using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace CatClawMusic.WinUI.Converters;

/// <summary>bool → Visibility；<see cref="Invert"/> 为 true 时取反。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is true;
        if (Invert)
            visible = !visible;

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
