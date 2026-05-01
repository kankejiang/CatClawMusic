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
    private readonly INetworkMusicService? _networkMusic;
    private readonly ISubsonicService? _subsonic;
    private readonly IMainThreadDispatcher _dispatcher;
    private LrcLyrics? _currentLyrics;
    private bool _isPositionUpdating;
    private int _saveCounter; // 定时保存计数器
    private ConnectionProfile? _cachedNetworkProfile;
    private bool _profileLookedUp;

    [ObservableProperty] private Song? _currentSong;
    [ObservableProperty] private string _coverSource = "";
    [ObservableProperty] private string _prevLyricLine = "";
    [ObservableProperty] private string _currentLyricLine = "🐾 猫爪音乐";
    [ObservableProperty] private string _nextLyricLine = "选择一首歌曲开始播放吧~";
    [ObservableProperty] private TimeSpan _currentPosition;
    [ObservableProperty] private TimeSpan _totalDuration;
    [ObservableProperty] private string _playPauseIcon = "▶";
    [ObservableProperty] private string _playModeIcon = ""; // 构造函数中同步
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
        IMusicLibraryService musicLibrary, PlayQueue playQueue, MusicDatabase? database = null,
        IMainThreadDispatcher? dispatcher = null, INetworkMusicService? networkMusic = null,
        ISubsonicService? subsonic = null)
    {
        _audioPlayer = audioPlayer;
        _lyricsService = lyricsService;
        _musicLibrary = musicLibrary;
        _playQueue = playQueue;
        _database = database;
        _networkMusic = networkMusic;
        _subsonic = subsonic;
        _dispatcher = dispatcher!;
        Volume = _audioPlayer.Volume;
        // 同步播放模式图标与 PlayQueue 的实际模式
        PlayModeIcon = _playQueue.PlayMode switch
        {
            PlayMode.ListRepeat => "🔁",
            PlayMode.SingleRepeat => "🔂",
            PlayMode.Shuffle => "🔀",
            _ => "➡️"
        };
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

            Stream? stream = coverBytes != null
                ? new MemoryStream(coverBytes)
                : await _musicLibrary.GetAlbumCoverAsync(song);

            // 网络歌曲：通过 Subsonic API 获取远端封面
            if (stream == null && song.Source == SongSource.WebDAV
                && !string.IsNullOrEmpty(song.CoverArtPath))
            {
                stream = await GetNetworkCoverAsync(song);
            }

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
            // 仅在播放中读取 Duration，避免切歌时的竞态触发原生 getDuration 错误
            if (_audioPlayer.IsPlaying)
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

        // 网络歌曲：尝试从 Subsonic 获取远程歌词
        if (_currentLyrics == null && song.Source == SongSource.WebDAV)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] LoadLyrics: 网络歌曲, Source={song.Source}, 尝试远程歌词");
            _currentLyrics = await GetNetworkLyricsAsync(song);
        }
        else if (_currentLyrics == null)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] LoadLyrics: 本地无歌词, Source={song.Source}, 跳过远程");
        }

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

    /// <summary>查找已启用的网络连接配置（Navidrome）</summary>
    private async Task<ConnectionProfile?> GetNetworkProfileAsync()
    {
        if (_cachedNetworkProfile != null) return _cachedNetworkProfile;
        if (_networkMusic == null || _profileLookedUp) return null;
        _profileLookedUp = true;
        try
        {
            var profiles = await _networkMusic.GetProfilesAsync();
            _cachedNetworkProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.Navidrome && p.IsEnabled);
            return _cachedNetworkProfile;
        }
        catch { return null; }
    }

    /// <summary>通过网络 API 获取远程封面</summary>
    private async Task<Stream?> GetNetworkCoverAsync(Song song)
    {
        if (_networkMusic == null || string.IsNullOrEmpty(song.CoverArtPath)) return null;
        var profile = await GetNetworkProfileAsync();
        if (profile == null) return null;
        try { return await _networkMusic.GetCoverAsync(song.CoverArtPath, profile); }
        catch { return null; }
    }

    /// <summary>通过网络 API 获取远程歌词</summary>
    private async Task<LrcLyrics?> GetNetworkLyricsAsync(Song song)
    {
        if (_subsonic == null)
        {
            System.Diagnostics.Debug.WriteLine("[CatClaw] GetNetworkLyrics: _subsonic 为 null");
            return null;
        }
        var profile = await GetNetworkProfileAsync();
        if (profile == null)
        {
            System.Diagnostics.Debug.WriteLine("[CatClaw] GetNetworkLyrics: 未找到 Navidrome 配置");
            return null;
        }
        try
        {
            var songId = ExtractSubsonicSongId(song);
            System.Diagnostics.Debug.WriteLine($"[CatClaw] GetNetworkLyrics: songId={songId}, title={song.Title}");
            if (string.IsNullOrEmpty(songId)) return null;
            var lrcText = await _subsonic.GetLyricsAsync(songId, profile);
            if (string.IsNullOrEmpty(lrcText))
            {
                System.Diagnostics.Debug.WriteLine("[CatClaw] GetNetworkLyrics: API 返回空歌词");
                return null;
            }
            System.Diagnostics.Debug.WriteLine($"[CatClaw] GetNetworkLyrics: 获取到 {lrcText.Length} 字符");
            // 先尝试 LRC 同步歌词
            var parsed = _lyricsService.ParseLrc(lrcText);
            if (parsed != null) return parsed;
            // 回退：非同步歌词（纯文本），每行视为一条歌词
            return BuildUnsynchronizedLyrics(lrcText);
        }
        catch { return null; }
    }

    /// <summary>将纯文本歌词转换为伪 LRC（无时间戳，按行显示）</summary>
    private static LrcLyrics BuildUnsynchronizedLyrics(string text)
    {
        var lyrics = new LrcLyrics();
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            lyrics.Lines.Add(new LrcLyricLine { Timestamp = TimeSpan.Zero, Text = trimmed });
        }
        return lyrics.Lines.Count > 0 ? lyrics : null!;
    }

    /// <summary>从 Subsonic stream URL 中提取歌曲 ID</summary>
    private static string ExtractSubsonicSongId(Song song)
    {
        // FilePath 已替换为 stream URL: http://host/rest/stream.view?id=XXX&u=...
        var path = song.FilePath;
        if (string.IsNullOrEmpty(path)) return "";
        var idx = path.IndexOf("id=", StringComparison.Ordinal);
        if (idx < 0) return "";
        idx += 3;
        var end = path.IndexOf('&', idx);
        return end > idx ? path[idx..end] : path[idx..];
    }
}
