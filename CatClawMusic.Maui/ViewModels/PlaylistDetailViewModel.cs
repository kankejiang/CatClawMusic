using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 歌单详情页 ViewModel：加载指定歌单（含“全部歌曲/收藏/最近播放”等虚拟歌单）的歌曲列表，
/// 提供单曲播放、整列表播放、移除歌曲、切换收藏与按来源筛选等交互能力。
/// </summary>
public partial class PlaylistDetailViewModel : ObservableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly IAudioPlayerService? _audioPlayer;
    private readonly PlayQueue? _playQueue;
    private readonly MusicDatabase _db;

    /// <summary>当前歌单下的歌曲集合（已应用筛选）</summary>
    public ObservableCollection<Song> Songs { get; } = new();

    private List<Song> _allSongsRaw = new();
    private int _playlistId;

    /// <summary>当前歌单信息</summary>
    [ObservableProperty]
    private Playlist _playlist = new();

    /// <summary>当前歌单名称</summary>
    [ObservableProperty]
    private string _playlistName = "";

    /// <summary>状态文本（用于向用户展示加载进度或结果）</summary>
    [ObservableProperty]
    private string _statusText = "";

    /// <summary>歌单封面路径（取歌单内第一首已解析封面的歌曲）</summary>
    [ObservableProperty]
    private string? _playlistCover;

    /// <summary>是否正在加载歌单数据</summary>
    [ObservableProperty]
    private bool _isLoading = false;

    /// <summary>请求播放某首歌曲时触发，供外部页面订阅以同步 UI 状态</summary>
    public event Action<Song>? SongPlayRequested;

    /// <summary>
    /// 初始化 <see cref="PlaylistDetailViewModel"/> 实例。
    /// </summary>
    /// <param name="musicLibrary">音乐库服务，用于读取歌单歌曲</param>
    /// <param name="db">音乐数据库访问对象</param>
    /// <param name="audioPlayer">音频播放服务，可为空（设计时支持）</param>
    /// <param name="playQueue">播放队列，可为空（设计时支持）</param>
    public PlaylistDetailViewModel(IMusicLibraryService musicLibrary,
        MusicDatabase db,
        IAudioPlayerService? audioPlayer = null,
        PlayQueue? playQueue = null)
    {
        _musicLibrary = musicLibrary;
        _db = db;
        _audioPlayer = audioPlayer;
        _playQueue = playQueue;
    }

    /// <summary>
    /// 设置歌单参数并加载：根据歌单 ID 选择不同数据源（全部/收藏/最近/普通歌单），
    /// 并按已启用协议过滤歌曲。
    /// </summary>
    /// <param name="playlistId">歌单 ID（-1=全部, -2=收藏, -3=最近播放, 其他=普通歌单）</param>
    /// <param name="name">歌单名称</param>
    public async Task LoadPlaylistAsync(int playlistId, string name)
    {
        _playlistId = playlistId;
        PlaylistName = name;
        IsLoading = true;
        StatusText = "加载中...";

        try
        {
            List<Song> songs;

            switch (playlistId)
            {
                case -1:
                    songs = await _musicLibrary.GetMergedSongsAsync();
                    break;
                case -2:
                    songs = await _musicLibrary.GetFavoriteSongsAsync();
                    break;
                case -3:
                    songs = await _musicLibrary.GetRecentSongsAsync();
                    break;
                case -4:
                    // 最多播放：按播放次数倒序取前 200 首
                    songs = await _musicLibrary.GetTopPlayedSongsAsync(200);
                    break;
                default:
                    songs = await _musicLibrary.GetPlaylistSongsAsync(playlistId);
                    break;
            }

            var enabledProtocols = await _db.GetEnabledProtocolsAsync();
            _allSongsRaw = _db.FilterByEnabledProtocols(songs, enabledProtocols);

            // 批量解析封面：从音频文件提取嵌入封面到磁盘缓存，回写 Song.CoverArtPath
            if (_allSongsRaw.Count > 0)
                await Task.Run(() => Services.CoverHelper.BatchResolveCovers(_allSongsRaw));

            // 用第一首已解析封面的歌曲作为歌单封面
            PlaylistCover = _allSongsRaw.FirstOrDefault(s => !string.IsNullOrEmpty(s.CoverArtPath))?.CoverArtPath;

            Songs.Clear();
            foreach (var s in _allSongsRaw)
                Songs.Add(s);

            // 同步更新 Playlist 对象，让顶部"共 X 首歌曲"显示正确
            Playlist = new Playlist
            {
                Id = playlistId,
                Name = name,
                SongCount = Songs.Count
            };

            StatusText = Songs.Count > 0 ? $"共 {Songs.Count} 首" : "暂无歌曲";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistDetailVM] LoadAsync({playlistId}) failed: {ex}");
            StatusText = "加载失败";
        }
        finally { IsLoading = false; }
    }

    /// <summary>
    /// 播放歌曲：若为当前曲则切换播放/暂停，否则将其设为播放队列当前曲并播放。
    /// </summary>
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

    /// <summary>播放全部：将歌单全部歌曲加入播放队列并从首曲开始播放</summary>
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

    /// <summary>
    /// 从歌单移除歌曲。
    /// </summary>
    /// <param name="song">要移除的歌曲，为空则忽略</param>
    [RelayCommand]
    public async Task RemoveSongAsync(Song? song)
    {
        if (song == null || _playlistId <= 0) return;
        await RemoveSongsFromPlaylistAsync(new[] { song.Id });
    }

    /// <summary>
    /// 批量移除歌曲：从歌单移除多首歌曲并同步集合。
    /// </summary>
    /// <param name="songIds">要移除的歌曲 ID 集合</param>
    /// <returns>实际移除的歌曲数量</returns>
    public async Task<int> RemoveSongsFromPlaylistAsync(IEnumerable<int> songIds)
    {
        if (_playlistId <= 0) return 0;
        var ids = songIds.ToHashSet();
        if (ids.Count == 0) return 0;

        await _musicLibrary.RemoveSongsFromPlaylistAsync(_playlistId, ids);

        var toRemove = Songs.Where(s => ids.Contains(s.Id)).ToList();
        foreach (var s in toRemove) Songs.Remove(s);
        foreach (var s in _allSongsRaw.Where(s => ids.Contains(s.Id)).ToList()) _allSongsRaw.Remove(s);
        StatusText = Songs.Count > 0 ? $"共 {Songs.Count} 首" : "暂无歌曲";
        return ids.Count;
    }

    /// <summary>
    /// 切换收藏状态。
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    /// <param name="isFav">是否收藏</param>
    public async Task ToggleFavoriteAsync(int songId, bool isFav)
    {
        await _db.SetFavoriteAsync(songId, isFav);
    }

    /// <summary>
    /// 按来源筛选：在原始歌曲集合上按 local / network / all 进行筛选。
    /// </summary>
    /// <param name="filter">筛选方式（"local" / "network" / 其他=全部）</param>
    public void ApplySourceFilter(string filter)
    {
        var filtered = filter switch
        {
            "local" => _allSongsRaw.Where(s => s.Source == SongSource.Local).ToList(),
            "network" => _allSongsRaw.Where(s => s.Source != SongSource.Local).ToList(),
            _ => _allSongsRaw.ToList()
        };
        Songs.Clear();
        foreach (var s in filtered) Songs.Add(s);
        StatusText = Songs.Count > 0 ? $"共 {Songs.Count} 首" : "暂无歌曲";
    }

    private async Task RecordPlayAsync(Song song)
    {
        try
        {
            await _db.RecordPlayAsync(song.Id);
        }
        catch { }
    }
}
