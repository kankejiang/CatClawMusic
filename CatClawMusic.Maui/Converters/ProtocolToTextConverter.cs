using System.Globalization;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// 将 <see cref="ProtocolType"/> 枚举转换为展示文本（WebDAV / Navidrome / SMB）。
/// </summary>
public class ProtocolToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ProtocolType protocol)
        {
            return protocol switch
            {
                ProtocolType.WebDAV => "WebDAV",
                ProtocolType.Navidrome => "Navidrome",
                ProtocolType.SMB => "SMB",
                _ => protocol.ToString()
            };
        }
        if (value is int idx)
        {
            return idx switch
            {
                0 => "WebDAV",
                1 => "Navidrome",
                2 => "SMB",
                _ => idx.ToString()
            };
        }
        return value?.ToString() ?? "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value;
}
