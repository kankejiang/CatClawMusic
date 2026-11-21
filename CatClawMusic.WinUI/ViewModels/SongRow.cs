using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CatClawMusic.WinUI.ViewModels;

/// <summary>
/// 歌曲行的展示模型：预先把格式化文本算好，XAML 里直接 x:Bind，省掉转换器。
/// </summary>
/// <summary>
/// 注意：这里刻意**不用** CommunityToolkit 的 [ObservableProperty] 源生成器。
/// WinUI 的 XAML 编译器在 MarkupCompilePass2 生成 x:Bind 代码时要反射读目标属性，
/// 源生成的 partial 属性在那一步还不可见，会抛 NullReferenceException（只表现为 WMC9999）。
/// 因此 Cover 手写 INotifyPropertyChanged。
/// </summary>
public sealed class SongRow : System.ComponentModel.INotifyPropertyChanged
{
    private BitmapImage? _cover;

    public SongRow(Song song)
    {
        Song = song;
        Artist = string.IsNullOrWhiteSpace(song.Artist) ? "未知艺术家" : song.Artist;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public Song Song { get; }

    /// <summary>歌手名（运行时字段，空值落「未知艺术家」）</summary>
    public string Artist { get; }

    /// <summary>
    /// 封面图。由页面在加载后异步填：Song.CoverArtPath 由 CoverService 抽出，
    /// 这里只负责把路径变成 Image 能用的 BitmapImage（XAML 里 Image.Source 不能直接吃文件路径）。
    /// </summary>
    public BitmapImage? Cover
    {
        get => _cover;
        set
        {
            if (ReferenceEquals(_cover, value))
                return;

            _cover = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Cover)));
        }
    }

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

    /// <summary>圆形头像里的首字（没有艺人图可用，Android 端也是用首字）</summary>
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1];
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
