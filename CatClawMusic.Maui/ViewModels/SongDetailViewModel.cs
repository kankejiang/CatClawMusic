using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 歌曲详情页 ViewModel：加载单首歌曲的完整信息（基本信息、文件与音质、歌词），
/// 按 song-detail-prototype.html 原型设计的字段组织数据，供 SongDetailPage 绑定展示。
/// </summary>
public partial class SongDetailViewModel : ObservableObject
{
    private readonly MusicDatabase _db;
    private readonly ILyricsService _lyrics;

    /// <summary>当前歌曲对象（含已填充的 Artist/Album/AllArtists/PlayCount 运行时字段）</summary>
    [ObservableProperty]
    private Song _song = new();

    /// <summary>封面图片源（文件路径包装为 FileImageSource）</summary>
    [ObservableProperty]
    private ImageSource? _coverImage;

    /// <summary>是否存在可用封面</summary>
    [ObservableProperty]
    private bool _hasCover;

    /// <summary>是否存在可用歌词</summary>
    [ObservableProperty]
    private bool _hasLyrics;

    /// <summary>是否正在加载</summary>
    [ObservableProperty]
    private bool _isLoading;

    // ── 基本信息卡字段（格式化后的字符串） ──

    /// <summary>专辑名（来自 Album 表）</summary>
    [ObservableProperty]
    private string _albumName = "";

    /// <summary>艺术家名（多艺术家用 " / " 分隔）</summary>
    [ObservableProperty]
    private string _artistName = "";

    /// <summary>流派</summary>
    [ObservableProperty]
    private string _genre = "未知";

    /// <summary>发行年份</summary>
    [ObservableProperty]
    private string _year = "未知";

    /// <summary>曲目序号</summary>
    [ObservableProperty]
    private string _track = "—";

    /// <summary>时长（mm:ss 格式）</summary>
    [ObservableProperty]
    private string _duration = "0:00";

    // ── 文件与音质卡字段 ──

    /// <summary>音质（kbps）</summary>
    [ObservableProperty]
    private string _bitrate = "—";

    /// <summary>文件格式（扩展名大写）</summary>
    [ObservableProperty]
    private string _fileFormat = "—";

    /// <summary>文件大小（MB）</summary>
    [ObservableProperty]
    private string _fileSize = "—";

    /// <summary>来源（本地/网络/本地+网络/未知）</summary>
    [ObservableProperty]
    private string _source = "未知";

    /// <summary>播放次数</summary>
    [ObservableProperty]
    private string _playCount = "0 次";

    /// <summary>歌词状态（有/无）</summary>
    [ObservableProperty]
    private string _lyricsStatus = "无";

    /// <summary>添加时间</summary>
    [ObservableProperty]
    private string _dateAdded = "—";

    /// <summary>修改时间</summary>
    [ObservableProperty]
    private string _dateModified = "—";

    /// <summary>文件路径</summary>
    [ObservableProperty]
    private string _filePath = "";

    // ── 专辑信息芯片 ──

    /// <summary>专辑信息芯片文本（"专辑名 · 年份"）</summary>
    [ObservableProperty]
    private string _albumChip = "";

    // ── 歌词数据 ──

    /// <summary>歌词行文本集合（纯文本，供可滚动列表展示）</summary>
    public ObservableCollection<string> LyricLines { get; } = new();

    /// <summary>
    /// 初始化 <see cref="SongDetailViewModel"/> 实例。
    /// </summary>
    /// <param name="db">音乐数据库访问对象</param>
    /// <param name="lyrics">歌词服务，用于加载歌词</param>
    public SongDetailViewModel(MusicDatabase db, ILyricsService lyrics)
    {
        _db = db;
        _lyrics = lyrics;
    }

    /// <summary>
    /// 异步加载指定歌曲的完整详情：基础字段、艺术家/专辑名、播放次数、封面、歌词。
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    public async Task LoadAsync(int songId)
    {
        if (songId <= 0) return;
        try
        {
            IsLoading = true;

            var song = await _db.GetSongByIdAsync(songId);
            if (song == null)
            {
                System.Diagnostics.Debug.WriteLine($"[SongDetailVM] Song {songId} not found");
                return;
            }
            Song = song;

            // 填充 Artist / AllArtists / Album / PlayCount 这些 [Ignore] 运行时字段
            var artist = await _db.FindArtistByIdAsync(song.ArtistId);
            var album = await _db.FindAlbumByIdAsync(song.AlbumId);
            var allArtistsDict = await _db.GetAllArtistsForSongsAsync(new[] { song.Id });

            song.Artist = artist?.Name ?? "未知艺术家";
            song.AllArtists = allArtistsDict.TryGetValue(song.Id, out var aa) ? aa : song.Artist;
            song.Album = album?.Title ?? "未知专辑";

            // 播放次数：从 PlayHistory 表聚合
            try
            {
                song.PlayCount = await _db.GetPlayCountForSongAsync(song.Id);
            }
            catch { }

            // 解析封面
            try
            {
                var coverPath = Services.CoverHelper.ResolveSingleCover(song);
                if (!string.IsNullOrEmpty(coverPath))
                {
                    song.CoverArtPath = coverPath;
                    CoverImage = ImageSource.FromFile(coverPath);
                    HasCover = true;
                }
            }
            catch { }

            // 填充格式化字段
            AlbumName = song.Album;
            ArtistName = song.AllArtists;
            Genre = string.IsNullOrWhiteSpace(song.Genre) ? "未知" : song.Genre;
            Year = song.Year > 0 ? song.Year.ToString() : "未知";
            Track = song.TrackNumber > 0 ? $"第 {song.TrackNumber} 首" : "—";
            Duration = FormatDuration(song.Duration);

            Bitrate = song.Bitrate > 0 ? $"{song.Bitrate} kbps" : "—";
            FileFormat = GetFileFormat(song.FilePath);
            FileSize = song.FileSize > 0 ? $"{song.FileSize / 1024.0 / 1024.0:F1} MB" : "—";
            Source = ResolveSourceLabel(song);
            PlayCount = $"{song.PlayCount} 次";
            LyricsStatus = HasLyrics ? "有" : "无";
            DateAdded = FormatTimestamp(song.DateAdded);
            DateModified = FormatTimestamp(song.DateModified);
            FilePath = song.FilePath ?? "";

            AlbumChip = string.IsNullOrEmpty(AlbumName) ? "" :
                (song.Year > 0 ? $"{AlbumName} · {song.Year}" : AlbumName);

            // 加载歌词
            await LoadLyricsAsync(song);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SongDetailVM] LoadAsync({songId}) failed: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>异步加载歌词并填充 LyricLines 集合</summary>
    private async Task LoadLyricsAsync(Song song)
    {
        LyricLines.Clear();
        try
        {
            var lyrics = await _lyrics.GetLyricsAsync(song);
            if (lyrics?.Lines != null && lyrics.Lines.Count > 0)
            {
                foreach (var line in lyrics.Lines)
                {
                    var text = line.Text;
                    if (string.IsNullOrWhiteSpace(text) && line.Timestamp == TimeSpan.Zero)
                        continue;
                    LyricLines.Add(text);
                }
                HasLyrics = LyricLines.Count > 0;
                LyricsStatus = HasLyrics ? "有" : "无";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SongDetailVM] LoadLyrics failed: {ex.Message}");
        }
    }

    /// <summary>将毫秒时长格式化为 m:ss 或 mm:ss</summary>
    private static string FormatDuration(long durationMs)
    {
        if (durationMs <= 0) return "0:00";
        var ts = TimeSpan.FromMilliseconds(durationMs);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    /// <summary>从文件路径提取扩展名并大写</summary>
    private static string GetFileFormat(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return "—";
        var ext = Path.GetExtension(filePath);
        return string.IsNullOrEmpty(ext) ? "—" : ext.Substring(1).ToUpperInvariant();
    }

    /// <summary>根据 SongSource 与 IsAlsoOnNetwork/IsAlsoLocal 标志解析来源标签</summary>
    private static string ResolveSourceLabel(Song song)
    {
        var isLocal = song.Source == SongSource.Local || song.IsAlsoLocal;
        var isNetwork = song.IsAlsoOnNetwork || song.Source != SongSource.Local;
        if (isLocal && isNetwork) return "本地 + 网络";
        if (isLocal) return "本地";
        if (isNetwork) return "网络";
        return "未知";
    }

    /// <summary>Unix 时间戳格式化为 yyyy-MM-dd</summary>
    private static string FormatTimestamp(long timestamp)
    {
        if (timestamp <= 0) return "—";
        try
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
            return dt.ToString("yyyy-MM-dd");
        }
        catch { return "—"; }
    }
}
