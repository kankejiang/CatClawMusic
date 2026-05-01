using System.Collections.ObjectModel;
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

    /// <summary>批量替换歌曲列表，先清空再一次性填充</summary>
    private void BatchReplaceSongs(List<Song> songs)
    {
        Songs.Clear(); // 单次 Reset 事件
        foreach (var s in songs)
            Songs.Add(s);
    }
}
