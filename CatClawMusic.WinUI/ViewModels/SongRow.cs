using CatClawMusic.Core.Models;

namespace CatClawMusic.WinUI.ViewModels;

/// <summary>
/// 歌曲行的展示模型：预先把格式化文本算好，XAML 里直接 x:Bind，省掉转换器。
/// </summary>
public sealed class SongRow
{
    public SongRow(Song song) => Song = song;

    public Song Song { get; }

    public string Title => string.IsNullOrWhiteSpace(Song.Title)
        ? Path.GetFileNameWithoutExtension(Song.FilePath)
        : Song.Title;

    public string Subtitle
    {
        get
        {
            var artist = string.IsNullOrWhiteSpace(Song.Artist) ? "未知艺术家" : Song.Artist;
            var album = string.IsNullOrWhiteSpace(Song.Album) ? null : Song.Album;
            return album is null ? artist : $"{artist} · {album}";
        }
    }

    public string DurationText => FormatDuration(Song.Duration);

    public string Badge => Title.Length > 0 ? Title[..1].ToUpperInvariant() : "♪";

    private static string FormatDuration(int seconds)
    {
        if (seconds <= 0)
            return "--:--";

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }
}

/// <summary>歌手列表行</summary>
public sealed record ArtistRow(string Name, int SongCount)
{
    public string CountText => $"{SongCount} 首";
}

/// <summary>专辑列表行</summary>
public sealed record AlbumRow(string Name, string Artist, int SongCount)
{
    public string Subtitle => string.IsNullOrWhiteSpace(Artist) ? $"{SongCount} 首" : $"{Artist} · {SongCount} 首";
}

/// <summary>歌单列表行</summary>
public sealed record PlaylistRow(Playlist Playlist)
{
    public string Name => Playlist.Name;

    public string CountText => $"{Playlist.SongCount} 首";

    public bool CanEdit => !Playlist.IsSystem;
}
