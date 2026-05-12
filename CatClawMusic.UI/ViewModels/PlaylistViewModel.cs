using System.Collections.ObjectModel;
using System.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.UI.ViewModels;

public partial class PlaylistViewModel : ObservableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private readonly INavigationService _navigationService;
    private readonly IAudioPlayerService? _audioPlayer;
    private readonly Core.Services.PlayQueue? _playQueue;
    private readonly IServiceProvider? _serviceProvider;

    public ObservableCollection<Song> Songs { get; } = new();

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _activeTab = "all";

    [ObservableProperty]
    private string _sortKey = "title";

    [ObservableProperty]
    private bool _sortDescending;

    public PlaylistViewModel(IMusicLibraryService musicLibrary, INavigationService navigationService,
        IAudioPlayerService? audioPlayer = null, Core.Services.PlayQueue? playQueue = null, IServiceProvider? serviceProvider = null)
    {
        _musicLibrary = musicLibrary;
        _navigationService = navigationService;
        _audioPlayer = audioPlayer;
        _playQueue = playQueue;
        _serviceProvider = serviceProvider;
    }

    public async Task LoadAllSongsAsync()
    {
        ActiveTab = "all";
        StatusText = "加载中...";
        try
        {
            var songs = await _musicLibrary.GetMergedSongsAsync();
            BatchReplaceSongs(songs);
            StatusText = Songs.Count > 0 ? $"共 {Songs.Count} 首" : "暂无歌曲";
        }
        catch { StatusText = "加载失败"; }
    }

    public async Task LoadFavoritesAsync()
    {
        ActiveTab = "fav";
        StatusText = "加载中...";
        try
        {
            var favSongs = await _musicLibrary.GetFavoriteSongsAsync();
            BatchReplaceSongs(favSongs);
            StatusText = Songs.Count > 0 ? $"共 {Songs.Count} 首" : "暂无收藏";
        }
        catch { StatusText = "加载失败"; }
    }

    public async Task LoadRecentAsync()
    {
        ActiveTab = "recent";
        StatusText = "加载中...";
        try
        {
            var recentSongs = await _musicLibrary.GetRecentSongsAsync();
            BatchReplaceSongs(recentSongs);
            StatusText = recentSongs.Count > 0 ? $"最近 {recentSongs.Count} 首" : "暂无记录";
        }
        catch { StatusText = "加载失败"; }
    }

    public async Task PlaySongAsync(Song song)
    {
        if (_audioPlayer == null || _playQueue == null) return;
        _playQueue.SetSongs(Songs);
        _playQueue.SelectSong(song.Id);
        if (!string.IsNullOrEmpty(song.FilePath))
            await _audioPlayer.PlayAsync(song.FilePath);
        // 同步迷你播放器
        var npvm = _serviceProvider?.GetService(typeof(NowPlayingViewModel)) as NowPlayingViewModel
            ?? MainApplication.Services.GetService(typeof(NowPlayingViewModel)) as NowPlayingViewModel;
        npvm?.SetCurrentSong(song);
        // 记录播放历史
        var db = _serviceProvider?.GetService(typeof(Data.MusicDatabase)) as Data.MusicDatabase
            ?? MainApplication.Services.GetService(typeof(Data.MusicDatabase)) as Data.MusicDatabase;
        _ = db?.RecordPlayAsync(song.Id);
    }

    public void SetSort(string key)
    {
        if (SortKey == key)
            SortDescending = !SortDescending;
        else
        {
            SortKey = key;
            SortDescending = false;
        }
        ApplySort();
    }

    private void ApplySort()
    {
        var sorted = SortKey switch
        {
            "artist" => SortDescending
                ? Songs.OrderByDescending(s => s.Artist).ToList()
                : Songs.OrderBy(s => s.Artist).ToList(),
            "album" => SortDescending
                ? Songs.OrderByDescending(s => s.Album).ToList()
                : Songs.OrderBy(s => s.Album).ToList(),
            _ => SortDescending
                ? Songs.OrderByDescending(s => s.Title).ToList()
                : Songs.OrderBy(s => s.Title).ToList(),
        };
        Songs.Clear();
        foreach (var s in sorted)
            Songs.Add(s);
    }

    /// <summary>批量替换歌曲列表，先清空再一次性填充</summary>
    private void BatchReplaceSongs(List<Song> songs)
    {
        Songs.Clear(); // 单次 Reset 事件
        var sorted = SortKey switch
        {
            "artist" => SortDescending
                ? songs.OrderByDescending(s => s.Artist).ToList()
                : songs.OrderBy(s => s.Artist).ToList(),
            "album" => SortDescending
                ? songs.OrderByDescending(s => s.Album).ToList()
                : songs.OrderBy(s => s.Album).ToList(),
            _ => SortDescending
                ? songs.OrderByDescending(s => s.Title).ToList()
                : songs.OrderBy(s => s.Title).ToList(),
        };
        foreach (var s in sorted)
            Songs.Add(s);
    }
}
