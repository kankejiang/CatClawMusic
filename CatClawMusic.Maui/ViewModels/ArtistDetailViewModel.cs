using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 艺术家详情页 ViewModel：加载指定艺术家的基本信息、专辑列表与歌曲列表，
/// 提供专辑/歌曲 Tab 切换、整列表播放与单曲播放/暂停等交互能力。
/// </summary>
public partial class ArtistDetailViewModel : ObservableObject
{
    private readonly MusicDatabase _db;
    private readonly IAudioPlayerService? _audioPlayer;
    private readonly PlayQueue? _playQueue;

    /// <summary>当前艺术家信息</summary>
    [ObservableProperty]
    private Artist _artist = new();

    /// <summary>该艺术家的专辑集合（从其歌曲中聚合去重）</summary>
    [ObservableProperty]
    private ObservableCollection<Album> _albums = new();

    /// <summary>该艺术家的歌曲集合</summary>
    [ObservableProperty]
    private ObservableCollection<Song> _songs = new();

    /// <summary>“专辑”Tab 是否可见（处于选中状态）</summary>
    [ObservableProperty]
    private bool _isAlbumsTabVisible = true;

    /// <summary>“歌曲”Tab 是否可见（处于选中状态）</summary>
    [ObservableProperty]
    private bool _isSongsTabVisible = false;

    /// <summary>“专辑”Tab 的背景色</summary>
    [ObservableProperty]
    private Color _albumsTabColor = Colors.Transparent;

    /// <summary>“歌曲”Tab 的背景色</summary>
    [ObservableProperty]
    private Color _songsTabColor = Colors.Transparent;

    /// <summary>是否正在加载艺术家数据</summary>
    [ObservableProperty]
    private bool _isLoading = false;

    /// <summary>状态文本（用于向用户展示加载进度或结果）</summary>
    [ObservableProperty]
    private string _statusText = "";

    /// <summary>请求播放某首歌曲时触发，供外部页面订阅以同步 UI 状态</summary>
    public event Action<Song>? SongPlayRequested;

    /// <summary>
    /// 初始化 <see cref="ArtistDetailViewModel"/> 实例，并设置默认 Tab 颜色。
    /// </summary>
    /// <param name="db">音乐数据库访问对象</param>
    /// <param name="audioPlayer">音频播放服务，可为空（设计时支持）</param>
    /// <param name="playQueue">播放队列，可为空（设计时支持）</param>
    public ArtistDetailViewModel(MusicDatabase db,
        IAudioPlayerService? audioPlayer = null,
        PlayQueue? playQueue = null)
    {
        _db = db;
        _audioPlayer = audioPlayer;
        _playQueue = playQueue;

        // 默认 Tab 颜色
        AlbumsTabColor = Color.FromArgb("#9B7ED8");
        SongsTabColor = Color.FromArgb("#1E787880");
    }

    /// <summary>加载艺术家详情：从数据库读取艺术家信息、歌曲列表，并按专辑去重聚合</summary>
    /// <param name="artistName">艺术家名称</param>
    [RelayCommand]
    public async Task LoadArtistAsync(string artistName)
    {
        IsLoading = true;
        StatusText = "加载中...";
        try
        {
            // 从数据库获取艺术家信息
            var allArtists = await _db.GetAllArtistsAsync();
            var artist = allArtists.FirstOrDefault(a =>
                string.Equals(a.Name, artistName, StringComparison.OrdinalIgnoreCase));
            if (artist != null)
                Artist = artist;
            else
                Artist = new Artist { Name = artistName };

            // 加载该艺术家的歌曲
            var songs = await _db.GetSongsByArtistAsync(artistName);
            Songs.Clear();
            foreach (var s in songs)
                Songs.Add(s);

            // 从歌曲中提取专辑列表（去重）
            var albumDict = new Dictionary<string, Album>();
            foreach (var s in songs)
            {
                if (!string.IsNullOrEmpty(s.Album) && !albumDict.ContainsKey(s.Album))
                {
                    albumDict[s.Album] = new Album
                    {
                        Title = s.Album,
                        Artist = s.Artist,
                    };
                }
            }
            Albums.Clear();
            foreach (var a in albumDict.Values)
                Albums.Add(a);

            StatusText = $"共 {Albums.Count} 张专辑 · {Songs.Count} 首歌曲";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ArtistDetailVM] LoadArtist({artistName}) failed: {ex}");
            StatusText = "加载失败";
        }
        finally { IsLoading = false; }
    }

    /// <summary>切换详情页 Tab：在“专辑”与“歌曲”之间切换并更新 Tab 颜色</summary>
    /// <param name="tab">目标 Tab 名称（"Albums" 或 "Songs"）</param>
    [RelayCommand]
    public void SwitchTab(string tab)
    {
        IsAlbumsTabVisible = tab == "Albums";
        IsSongsTabVisible = tab == "Songs";
        AlbumsTabColor = IsAlbumsTabVisible ? Color.FromArgb("#9B7ED8") : Color.FromArgb("#1E787880");
        SongsTabColor = IsSongsTabVisible ? Color.FromArgb("#9B7ED8") : Color.FromArgb("#1E787880");
    }

    /// <summary>播放该艺术家的全部歌曲：将歌曲加入播放队列并从首曲开始播放</summary>
    [RelayCommand]
    public async Task PlayAllAsync()
    {
        if (_audioPlayer == null || _playQueue == null || Songs.Count == 0) return;
        _playQueue.SetSongs([.. Songs]);
        var first = Songs[0];
        _playQueue.SelectSong(first.Id);
        if (!string.IsNullOrEmpty(first.FilePath))
            await _audioPlayer.PlayAsync(first.FilePath);
        _ = RecordPlayAsync(first);
        SongPlayRequested?.Invoke(first);
    }

    /// <summary>播放指定歌曲：若为当前曲则切换播放/暂停，否则将其设为播放队列当前曲并播放</summary>
    /// <param name="song">要播放的歌曲，为空则忽略</param>
    [RelayCommand]
    public async Task PlaySongAsync(Song? song)
    {
        if (song == null || _audioPlayer == null || _playQueue == null) return;

        var currentSongInQueue = _playQueue.CurrentSong;
        if (currentSongInQueue != null && currentSongInQueue.Id == song.Id)
        {
            if (_audioPlayer.IsPlaying)
                await _audioPlayer.PauseAsync();
            else
                await _audioPlayer.ResumeAsync();
        }
        else
        {
            _playQueue.SetSongs([.. Songs]);
            _playQueue.SelectSong(song.Id);
            if (!string.IsNullOrEmpty(song.FilePath))
                await _audioPlayer.PlayAsync(song.FilePath);
            _ = RecordPlayAsync(song);
            SongPlayRequested?.Invoke(song);
        }
    }

    private async Task RecordPlayAsync(Song song)
    {
        try { await _db.RecordPlayAsync(song.Id); }
        catch { }
    }
}
