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
    private int _saveCounter; // 定时保存计数器

    [ObservableProperty] private Song? _currentSong;
    [ObservableProperty] private string _coverSource = "";
    [ObservableProperty] private string _prevLyricLine = "";
    [ObservableProperty] private string _currentLyricLine = "🐾 猫爪音乐";
    [ObservableProperty] private string _nextLyricLine = "选择一首歌曲开始播放吧~";
    [ObservableProperty] private TimeSpan _currentPosition;
    [ObservableProperty] private TimeSpan _totalDuration;
    [ObservableProperty] private string _playPauseIcon = "▶";
    [ObservableProperty] private string _playModeIcon = "🔁"; // 默认列表循环
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

    partial void OnCurrentSongChanged(Song? value) { _ = LoadLyricsAsync(value); _ = LoadCoverAsync(value); UpdateQueuePeek(); _ = CheckFavoriteAsync(); }
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

    /// <summary>恢复上次播放（供 PlaybackStateManager 调用）</summary>
    public void SetCurrentSong(Song song)
    {
        CurrentSong = song;
        UpdateQueuePeek();
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (_audioPlayer.IsPlaying)
        {
            _ = _audioPlayer.PauseAsync();
            PlayPauseIcon = "▶";
        }
        else if (CurrentSong != null)
        {
            _ = _audioPlayer.ResumeAsync();
            PlayPauseIcon = "⏸";
        }
    }

    [RelayCommand]
    private void Next() { var s = _playQueue.Next(); if (s != null) { CurrentSong = s; _ = _audioPlayer.PlayAsync(s.FilePath); _ = RecordPlayAsync(); } }

    [RelayCommand]
    private void Previous() { var s = _playQueue.Previous(); if (s != null) { CurrentSong = s; _ = _audioPlayer.PlayAsync(s.FilePath); _ = RecordPlayAsync(); } }

    [RelayCommand]
    private void CyclePlayMode()
    {
        // 循环切换：🔁列表循环 → 🔂单曲循环 → 🔀随机播放
        _playQueue.PlayMode = _playQueue.PlayMode switch
        {
            PlayMode.ListRepeat => PlayMode.SingleRepeat,
            PlayMode.SingleRepeat => PlayMode.Shuffle,
            PlayMode.Shuffle => PlayMode.ListRepeat,
            _ => PlayMode.ListRepeat
        };
        if (_playQueue.PlayMode == PlayMode.Shuffle) _playQueue.EnableShuffle();
        PlayModeIcon = _playQueue.PlayMode switch
        {
            PlayMode.ListRepeat => "🔁",
            PlayMode.SingleRepeat => "🔂",
            PlayMode.Shuffle => "🔀",
            _ => "➡️"
        };
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
            CurrentSong = queueSong;
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
            byte[]? coverBytes = null;

            // content:// URI 需要走 ContentResolver 来读标签
            if (song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                coverBytes = ExtractCoverFromContentUri(song.FilePath);
            }

            var stream = coverBytes != null
                ? new MemoryStream(coverBytes)
                : await _musicLibrary.GetAlbumCoverAsync(song);

            if (stream != null)
            {
                var cacheDir = Path.Combine(global::Android.App.Application.Context.CacheDir!.AbsolutePath, "covers");
                Directory.CreateDirectory(cacheDir);
                var coverPath = Path.Combine(cacheDir, $"cover_{song.Id}.jpg");
                using (var fs = File.Create(coverPath)) await stream.CopyToAsync(fs);
                if (coverBytes != null) stream.Dispose();
                _dispatcher.Post(() => CoverSource = coverPath);
            }
        }
        catch { }
    }

    private static byte[]? ExtractCoverFromContentUri(string uri)
    {
        try
        {
            var ctx = global::Android.App.Application.Context;
            var parsedUri = global::Android.Net.Uri.Parse(uri);
            if (parsedUri == null) return null;
            using var stream = ctx.ContentResolver!.OpenInputStream(parsedUri);
            if (stream == null) return null;

            var abstraction = new CatClawMusic.Core.Services.ReadOnlyFileAbstraction(uri, stream);
            using var file = TagLib.File.Create(abstraction);
            if (file.Tag.Pictures is { Length: > 0 })
                return file.Tag.Pictures[0].Data.Data;
        }
        catch { }
        return null;
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
                    PrevLyricLine = idx > 0 ? _currentLyrics.Lines[idx - 1].Text : "";
                    NextLyricLine = idx + 1 < _currentLyrics.Lines.Count ? _currentLyrics.Lines[idx + 1].Text : "";
                    CurrentLyricLine = _currentLyrics.Lines[idx].Text; // 最后设，触发 UI 刷新
                }
            }
            _isPositionUpdating = false;
            // 每 ~5 秒保存一次播放位置（500ms 定时器 × 10）
            if (++_saveCounter % 10 == 0)
                CatClawMusic.UI.Services.PlaybackStateManager.Save(_audioPlayer);
        });
    }

    private async Task LoadLyricsAsync(Song? song)
    {
        if (song == null) { CurrentLyricLine = "🐾 猫爪音乐"; NextLyricLine = "选择一首歌曲开始播放吧~"; _currentLyrics = null; return; }
        CurrentLyricLine = "正在加载歌词..."; NextLyricLine = "";
        _currentLyrics = await _lyricsService.GetLyricsAsync(song);
        if (_currentLyrics == null) { CurrentLyricLine = "暂无歌词"; NextLyricLine = ""; }
        else if (_currentLyrics.Lines.Count > 0)
        {
            // 根据当前播放位置显示初始歌词行
            var pos = _audioPlayer.CurrentPosition;
            var idx = _lyricsService.GetCurrentLyricIndex(_currentLyrics, pos);
            if (idx >= 0 && idx < _currentLyrics.Lines.Count)
            {
                PrevLyricLine = idx > 0 ? _currentLyrics.Lines[idx - 1].Text : "";
                NextLyricLine = idx + 1 < _currentLyrics.Lines.Count ? _currentLyrics.Lines[idx + 1].Text : "";
                CurrentLyricLine = _currentLyrics.Lines[idx].Text;
            }
        }
    }

    private async Task RecordPlayAsync()
    {
        if (_database == null || CurrentSong == null) return;
        try { await _database.EnsureInitializedAsync(); await _database.RecordPlayAsync(CurrentSong.Id); } catch { }
    }

    private async Task SaveFavoriteAsync()
    {
        if (_database == null || CurrentSong == null) return;
        try { await _database.EnsureInitializedAsync(); await _database.SetFavoriteAsync(CurrentSong.Id, IsLiked); } catch { }
    }

    /// <summary>切换歌曲时从数据库同步收藏状态，避免上一首歌的♥状态残留</summary>
    private async Task CheckFavoriteAsync()
    {
        if (_database == null || CurrentSong == null)
        {
            IsLiked = false;
            return;
        }
        try
        {
            await _database.EnsureInitializedAsync();
            IsLiked = await _database.IsFavoriteAsync(CurrentSong.Id);
        }
        catch { IsLiked = false; }
    }
}
