using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// Converts playlist name to an icon emoji for system playlists.
/// </summary>
public class PlaylistIconConverter : IValueConverter
{
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

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
