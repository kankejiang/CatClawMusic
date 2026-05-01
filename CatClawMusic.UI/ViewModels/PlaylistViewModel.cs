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
    private string _activeTab = "all"; // all / fav / recent

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
            Songs.Clear();
            foreach (var s in songs) Songs.Add(s);
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
            // TODO: 从数据库加载收藏歌曲
            Songs.Clear();
            StatusText = "暂无收藏";
        }
        catch { StatusText = "加载失败"; }
    }

    public async Task LoadRecentAsync()
    {
        ActiveTab = "recent";
        StatusText = "加载中...";
        try
        {
            // TODO: 从数据库加载最近播放
            Songs.Clear();
            StatusText = "暂无记录";
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
    }
}
