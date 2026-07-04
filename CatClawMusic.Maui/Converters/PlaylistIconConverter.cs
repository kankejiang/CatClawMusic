using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// 播放列表名称转图标 Emoji 转换器。
/// 为系统内置播放列表（全部歌曲/收藏歌曲/最近播放）返回对应图标，其它返回光盘图标。
/// </summary>
public class PlaylistIconConverter : IValueConverter
{
    /// <summary>根据播放列表名称返回对应的图标 Emoji</summary>
    /// <param name="value">播放列表名称</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">额外参数（未使用）</param>
    /// <param name="culture">区域性信息</param>
    /// <returns>对应图标 Emoji 字符串</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string;
        return name switch
        {
            "全部歌曲" => "\U0001f3b5", // 🎵
            "收藏歌曲" => "\u2665",    // ♥
            "最近播放" => "\U0001f552", // 🕒
            _ => "\U0001f3c0"           // 📀
        };
    }

    /// <summary>反向转换不支持</summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
