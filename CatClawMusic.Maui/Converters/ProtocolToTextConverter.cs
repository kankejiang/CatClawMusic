using System.Globalization;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// 将 <see cref="ProtocolType"/> 枚举转换为展示文本（WebDAV / Navidrome / SMB）。
/// 也支持将 int 索引（0/1/2）映射到对应文本。
/// </summary>
public class ProtocolToTextConverter : IValueConverter
{
    /// <summary>将协议枚举或 int 索引转换为展示文本</summary>
    /// <param name="value">ProtocolType 枚举或 int 索引</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">额外参数（未使用）</param>
    /// <param name="culture">区域性信息</param>
    /// <returns>协议展示文本；无效值返回原值的字符串形式</returns>
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

    /// <summary>反向转换，直接返回原值</summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value;
}
