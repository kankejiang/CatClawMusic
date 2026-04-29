using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.UI.ViewModels;

public partial class NowPlayingViewModel : ObservableObject
{
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ILyricsService _lyricsService;
    private readonly IMusicLibraryService _musicLibrary;
    private readonly PlayQueue _playQueue;
    private readonly MusicDatabase? _database;
    private readonly IMainThreadDispatcher _dispatcher;
    private LrcLyrics? _currentLyrics;
    private bool _isPositionUpdating;

    [ObservableProperty] private Song? _currentSong;
    [ObservableProperty] private string _coverSource = "";
    [ObservableProperty] private string _currentLyricLine = "🐾 猫爪音乐";
    [ObservableProperty] private string _nextLyricLine = "选择一首歌曲开始播放吧~";
    [ObservableProperty] private TimeSpan _currentPosition;
    [ObservableProperty] private TimeSpan _totalDuration;
    [ObservableProperty] private string _playPauseIcon = "▶";
    [ObservableProperty] private string _playModeIcon = "🔁";
    [ObservableProperty] private string _likeIcon = "🤍";
    [ObservableProperty] private bool _isLiked;
    [ObservableProperty] private int _volume = 80;
    [ObservableProperty] private string _queueHint = "";
    public ObservableCollection<Song> UpcomingSongs { get; } = new();

    public double CurrentPositionSeconds
    {
        get => CurrentPosition.TotalSeconds;
        set { if (!_isPositionUpdating) _ = _audioPlayer.SeekAsync(TimeSpan.FromSeconds(value)); }
    }
    public double TotalDurationSeconds => TotalDuration.TotalSeconds;

    partial void OnCurrentSongChanged(Song? value) { _ = LoadLyricsAsync(value); _ = LoadCoverAsync(value); UpdateQueuePeek(); }
    partial void OnIsLikedChanged(bool value) { LikeIcon = value ? "❤️" : "🤍"; }

    public NowPlayingViewModel(IAudioPlayerService audioPlayer, ILyricsService lyricsService,
        IMusicLibraryService musicLibrary, PlayQueue playQueue, MusicDatabase? database = null, IMainThreadDispatcher? dispatcher = null)
    {
        _audioPlayer = audioPlayer;
        _lyricsService = lyricsService;
        _musicLibrary = musicLibrary;
        _playQueue = playQueue;
        _database = database;
        _dispatcher = dispatcher!;
        Volume = _audioPlayer.Volume;
        _audioPlayer.StateChanged += OnPlaybackStateChanged;
        _audioPlayer.PositionChanged += OnPositionChanged;
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (_audioPlayer.IsPlaying) _ = _audioPlayer.PauseAsync();
        else if (CurrentSong != null) { _ = _audioPlayer.PlayAsync(CurrentSong.FilePath); _ = RecordPlayAsync(); }
    }

    [RelayCommand]
    private void Next() { var s = _playQueue.Next(); if (s != null) { CurrentSong = s; _ = _audioPlayer.PlayAsync(s.FilePath); _ = RecordPlayAsync(); } }

    [RelayCommand]
    private void Previous() { var s = _playQueue.Previous(); if (s != null) { CurrentSong = s; _ = _audioPlayer.PlayAsync(s.FilePath); _ = RecordPlayAsync(); } }

    [RelayCommand]
    private void TogglePlayMode()
    {
        _playQueue.PlayMode = _playQueue.PlayMode switch { PlayMode.Sequential => PlayMode.Shuffle, PlayMode.Shuffle => PlayMode.SingleRepeat, PlayMode.SingleRepeat => PlayMode.ListRepeat, PlayMode.ListRepeat => PlayMode.Sequential, _ => PlayMode.Sequential };
        PlayModeIcon = _playQueue.PlayMode switch { PlayMode.Sequential => "➡️", PlayMode.Shuffle => "🔀", PlayMode.SingleRepeat => "🔂", PlayMode.ListRepeat => "🔁", _ => "🔁" };
    }

    [RelayCommand]
    private void ToggleShuffle()
    {
        _playQueue.PlayMode = _playQueue.PlayMode == PlayMode.Shuffle ? PlayMode.Sequential : PlayMode.Shuffle;
        if (_playQueue.PlayMode == PlayMode.Shuffle) _playQueue.EnableShuffle();
        PlayModeIcon = _playQueue.PlayMode == PlayMode.Shuffle ? "🔀" : "➡️";
        UpdateQueuePeek();
    }

    [RelayCommand]
    private void ToggleLike() { IsLiked = !IsLiked; _ = SaveFavoriteAsync(); }

    [RelayCommand]
    private void Swipe(string direction)
    {
        if (direction == "Left") OnNext();
        else if (direction == "Right") OnPrevious();
    }

    private void OnNext() { var s = _playQueue.Next(); if (s != null) { CurrentSong = s; _ = _audioPlayer.PlayAsync(s.FilePath); _ = RecordPlayAsync(); } }
    private void OnPrevious() { var s = _playQueue.Previous(); if (s != null) { CurrentSong = s; _ = _audioPlayer.PlayAsync(s.FilePath); _ = RecordPlayAsync(); } }

    public void SyncWithQueue()
    {
        var queueSong = _playQueue.CurrentSong;
        if (queueSong != null && (CurrentSong == null || CurrentSong.Id != queueSong.Id))
        {
            _currentSong = queueSong;
            OnPropertyChanged(nameof(CurrentSong));
            _ = LoadLyricsAsync(queueSong);
            _ = LoadCoverAsync(queueSong);
            UpdateQueuePeek();
        }
        if (_audioPlayer.IsPlaying) PlayPauseIcon = "⏸";
    }

    private async Task LoadCoverAsync(Song? song)
    {
        CoverSource = "";
        if (song == null) return;
        try
        {
            var stream = await _musicLibrary.GetAlbumCoverAsync(song);
            if (stream != null)
            {
                var cacheDir = Path.Combine(global::Android.App.Application.Context.CacheDir!.AbsolutePath, "covers");
                Directory.CreateDirectory(cacheDir);
                var coverPath = Path.Combine(cacheDir, $"cover_{song.Id}.jpg");
                using (var fs = File.Create(coverPath)) await stream.CopyToAsync(fs);
                _dispatcher.Post(() => CoverSource = coverPath);
            }
        }
        catch { }
    }

    private void UpdateQueuePeek()
    {
        UpcomingSongs.Clear();
        foreach (var s in _playQueue.GetUpcomingSongs(3)) UpcomingSongs.Add(s);
        QueueHint = UpcomingSongs.Count > 0 ? $"下一首: {UpcomingSongs[0].Title}" : "";
        OnPropertyChanged(nameof(UpcomingSongs));
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        _dispatcher.Post(() =>
        {
            if (e.State == PlaybackState.Stopped) Next();
            else if (e.State == PlaybackState.Playing) PlayPauseIcon = "⏸";
            else if (e.State is PlaybackState.Paused or PlaybackState.Error) PlayPauseIcon = "▶";
        });
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        _dispatcher.Post(() =>
        {
            _isPositionUpdating = true;
            CurrentPosition = position;
            TotalDuration = _audioPlayer.Duration;
            if (_currentLyrics?.Lines is { Count: > 0 })
            {
                var idx = _lyricsService.GetCurrentLyricIndex(_currentLyrics, position);
                if (idx >= 0 && idx < _currentLyrics.Lines.Count)
                {
                    CurrentLyricLine = _currentLyrics.Lines[idx].Text;
                    NextLyricLine = idx + 1 < _currentLyrics.Lines.Count ? _currentLyrics.Lines[idx + 1].Text : "";
                }
            }
            _isPositionUpdating = false;
        });
    }

    private async Task LoadLyricsAsync(Song? song)
    {
        if (song == null) { CurrentLyricLine = "🐾 猫爪音乐"; NextLyricLine = "选择一首歌曲开始播放吧~"; _currentLyrics = null; return; }
        CurrentLyricLine = "正在加载歌词..."; NextLyricLine = "";
        _currentLyrics = await _lyricsService.GetLyricsAsync(song);
        if (_currentLyrics == null) { CurrentLyricLine = "暂无歌词"; NextLyricLine = ""; }
    }

    private async Task RecordPlayAsync()
    {
        if (_database == null || CurrentSong == null) return;
        try { await _database.EnsureInitializedAsync(); await _database.UpdatePlaybackStatsAsync(CurrentSong.Id); await _database.RecordRecentPlayAsync(CurrentSong.Id); } catch { }
    }

    private async Task SaveFavoriteAsync()
    {
        if (_database == null || CurrentSong == null) return;
        try { await _database.EnsureInitializedAsync(); await _database.UpdatePlaybackStatsAsync(CurrentSong.Id, isLiked: IsLiked, isComplete: true); } catch { }
    }
}
