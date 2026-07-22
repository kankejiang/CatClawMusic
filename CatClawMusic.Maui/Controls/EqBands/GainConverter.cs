using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Controls.EqBands;

/// <summary>将增益(double dB)格式化为带符号文本（+6 / -3 / 0），供频段数值标签绑定。</summary>
public sealed class GainConverter : IValueConverter
{
    public object? Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
    {
        if (value is double d)
            return (d > 0 ? "+" : "") + d.ToString("0");
        return "0";
    }

    public object? ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture) => null;
}
