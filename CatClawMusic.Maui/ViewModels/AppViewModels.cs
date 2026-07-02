using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

public partial class NowPlayingViewModel : ObservableObject
{
    private readonly PlayQueue _queue;
    private readonly ILyricsService _lyrics;
    private readonly MusicDatabase _db;
    private readonly IAudioPlayerService _audioService;
    private readonly IMusicLibraryService _musicLibrary;

    private string _coverCacheDir = "";
    private bool _isSeeking;
    private DateTime _seekStartTime = DateTime.MinValue;
    private int _lastRecordedSongId = -1;
    /// <summary>上次 LoadCurrentSongAsync 加载的歌曲ID，用于判断切页时是否需要重新播放</summary>
    private int _loadedSongId = -1;
    private CancellationTokenSource? _loadCts;

    // === Basic Song Info ===

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _album = "";
    [ObservableProperty] private bool _hasAlbum;

    // === Cover Art ===
    [ObservableProperty] private ImageSource? _coverImage;
    [ObservableProperty] private bool _hasCover;

    // === Playback State ===
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private double _duration;
    [ObservableProperty] private double _volume = 1.0;
    [ObservableProperty] private string _currentTimeDisplay = "0:00";
    [ObservableProperty] private string _totalTimeDisplay = "0:00";

    // === Play Mode ===
    [ObservableProperty] private string _playModeIcon = "\U0001f501"; // 🔁 list repeat
    [ObservableProperty] private string _playModeLabel = "列表循环";
    [ObservableProperty] private string _playModeIconSource = "ic_repeat_all";

    // === Play/Pause ===
    [ObservableProperty] private string _playPauseIcon = "\u25b6"; // ▶
    [ObservableProperty] private string _playPauseIconSource = "ic_play";

    // === Like ===
    [ObservableProperty] private bool _isLiked;
    [ObservableProperty] private string _likeIcon = "\u2661"; // ♡
    [ObservableProperty] private string _likeIconSource = "ic_favorite_border";

    // === Lyrics ===
    [ObservableProperty] private bool _hasLyrics;
    [ObservableProperty] private string _lyricLine0 = "";  // 2 lines before
    [ObservableProperty] private string _lyricLine1 = "";  // 1 line before
    [ObservableProperty] private string _lyricCurrent = ""; // current
    [ObservableProperty] private string _lyricLine2 = "";  // 1 line after
    [ObservableProperty] private string _lyricLine3 = "";  // 2 lines after
    [ObservableProperty] private string _noLyricsText = "暂无歌词";

    // Full lyrics (for FullLyricsPage)
    [ObservableProperty] private int _currentLyricIndexObservable = -1;
    public IReadOnlyList<LrcLyricLine>? AllLyricLines => _currentLyrics?.Lines;

    private LrcLyrics? _currentLyrics;
    private int _currentLyricIndex = -1;

    // === Upcoming Songs (for playlist drawer) ===
    public ObservableCollection<Song> UpcomingSongs { get; } = new();

    public Song? CurrentSong => _queue.CurrentSong;

    public NowPlayingViewModel(
        PlayQueue queue,
        ILyricsService lyrics,
        MusicDatabase db,
        IAudioPlayerService audioService,
        IMusicLibraryService musicLibrary)
    {
        _queue = queue;
        _lyrics = lyrics;
        _db = db;
        _audioService = audioService;
        _musicLibrary = musicLibrary;

        // Initialize cover cache directory
        _coverCacheDir = Path.Combine(FileSystem.CacheDirectory, "covers");
        Directory.CreateDirectory(_coverCacheDir);

        // Subscribe to audio events
        _audioService.PlaybackStateChanged += OnPlaybackStateChanged;
        _audioService.PositionChanged += OnPositionChanged;
        _audioService.PlaybackCompleted += OnPlaybackCompleted;

        // Commands
        TogglePlayPauseCommand = new AsyncRelayCommand(TogglePlayPauseAsync);
        PlayNextCommand = new AsyncRelayCommand(PlayNextAsync);
        PlayPreviousCommand = new AsyncRelayCommand(PlayPreviousAsync);
        CyclePlayModeCommand = new RelayCommand(CyclePlayMode);
        ToggleLikeCommand = new AsyncRelayCommand(ToggleLikeAsync);
        SeekCommand = new RelayCommand<double>(OnSeek);
    }

    // === Commands ===
    public IRelayCommand TogglePlayPauseCommand { get; }
    public IRelayCommand PlayNextCommand { get; }
    public IRelayCommand PlayPreviousCommand { get; }
    public IRelayCommand CyclePlayModeCommand { get; }
    public IRelayCommand ToggleLikeCommand { get; }
    public RelayCommand<double> SeekCommand { get; }

    private void OnPlaybackStateChanged(object? sender, bool isPlaying)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            IsPlaying = isPlaying;
            PlayPauseIcon = isPlaying ? "\u23f8" : "\u25b6"; // ⏸ or ▶
            PlayPauseIconSource = isPlaying ? "ic_pause" : "ic_play";

            // 检测队列当前歌曲是否变化（外部页面播放时触发）
            // 此时 _loadedSongId 还是旧值，需要加载新歌信息更新迷你播放器
            var queueSong = _queue.CurrentSong;
            if (isPlaying && queueSong != null && queueSong.Id != _loadedSongId)
            {
                await LoadCurrentSongAsync(autoPlay: false);
            }
        });
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Update Duration from audio service if not yet set (e.g. before PlayAsync completes)
            if (Duration <= 0 && _audioService.Duration > 0)
                Duration = _audioService.Duration;

            // Auto-release _isSeeking after 10s in case DragCompleted never fires
            if (!_isSeeking)
            {
                Progress = position.TotalSeconds;
            }
            else if ((DateTime.UtcNow - _seekStartTime).TotalSeconds >= 10)
            {
                _isSeeking = false;
                Progress = position.TotalSeconds;
            }
            CurrentTimeDisplay = FormatTime(position.TotalSeconds);
            UpdateLyricPosition(position);
            // Also refresh TotalTimeDisplay when Duration becomes known
            if (Duration > 0)
                TotalTimeDisplay = FormatTime(Duration);
        });
    }

    private void OnPlaybackCompleted(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _queue.Next();
            await LoadCurrentSongAsync();
        });
    }

    // === Play/Pause ===

    private async Task TogglePlayPauseAsync()
    {
        if (IsPlaying)
        {
            await _audioService.PauseAsync();
        }
        else
        {
            if (_queue.CurrentSong != null && !string.IsNullOrEmpty(_queue.CurrentSong.FilePath))
            {
                await _audioService.PlayAsync(_queue.CurrentSong.FilePath);
            }
        }
    }

    // === Next / Previous ===

    private async Task PlayNextAsync()
    {
        _queue.Next();
        await LoadCurrentSongAsync();
    }

    private async Task PlayPreviousAsync()
    {
        _queue.Previous();
        await LoadCurrentSongAsync();
    }

    // === Play Mode Cycling: ListRepeat → SingleRepeat → Shuffle → ListRepeat ===

    private void CyclePlayMode()
    {
        _queue.PlayMode = _queue.PlayMode switch
        {
            PlayMode.ListRepeat => PlayMode.SingleRepeat,
            PlayMode.SingleRepeat => PlayMode.Shuffle,
            PlayMode.Shuffle => PlayMode.ListRepeat,
            _ => PlayMode.ListRepeat
        };

        if (_queue.PlayMode == PlayMode.Shuffle)
            _queue.EnableShuffle();

        RefreshPlayModeDisplay();
    }

    private void RefreshPlayModeDisplay()
    {
        (PlayModeIcon, PlayModeLabel, PlayModeIconSource) = _queue.PlayMode switch
        {
            PlayMode.ListRepeat => ("\U0001f501", "列表循环", "ic_repeat_all"),
            PlayMode.SingleRepeat => ("\U0001f502", "单曲循环", "ic_repeat_one"),
            PlayMode.Shuffle => ("\U0001f500", "随机播放", "ic_shuffle"),
            PlayMode.Sequential => ("\u27a1", "顺序播放", "ic_repeat_all"),
            _ => ("\U0001f501", "列表循环", "ic_repeat_all")
        };
    }

    // === Like / Favorite ===

    private async Task ToggleLikeAsync()
    {
        var song = _queue.CurrentSong;
        if (song == null) return;

        var newFav = !IsLiked;
        await _db.SetFavoriteAsync(song.Id, newFav);
        IsLiked = newFav;
        LikeIcon = newFav ? "\u2665" : "\u2661"; // ♥ or ♡
        LikeIconSource = newFav ? "ic_favorite" : "ic_favorite_border";
    }

    // === Seek ===

    private async void OnSeek(double positionSeconds)
    {
        _isSeeking = false;
        await _audioService.SeekAsync(TimeSpan.FromSeconds(positionSeconds));
    }

    /// <summary>Called from UI when user starts dragging the slider</summary>
    public void OnSeekStarted()
    {
        _isSeeking = true;
        _seekStartTime = DateTime.UtcNow;
    }

    /// <summary>Called from UI when user releases the slider</summary>
    public async Task OnSeekCompleted(double positionSeconds)
    {
        _isSeeking = false;
        await _audioService.SeekAsync(TimeSpan.FromSeconds(positionSeconds));
    }

    // === Load Song (called when page appears or song changes) ===

    public async Task LoadCurrentSongAsync(bool autoPlay = true)
    {
        var song = _queue.CurrentSong;

        // 启动恢复：如果队列为空，尝试从 Preferences 恢复上次播放的歌曲
        if (song == null)
        {
            try
            {
                var savedId = Preferences.Default.Get("last_playing_song_id", -1);
                if (savedId > 0)
                {
                    await _db.EnsureInitializedAsync();
                    var restoredSong = await _db.GetSongByIdAsync(savedId);
                    if (restoredSong != null)
                    {
                        // 填充 Artist/Album 名称
                        var artist = await _db.FindArtistByIdAsync(restoredSong.ArtistId);
                        var album = await _db.FindAlbumByIdAsync(restoredSong.AlbumId);
                        restoredSong.Artist = artist?.Name ?? "未知艺术家";
                        restoredSong.Album = album?.Title ?? "未知专辑";
                        restoredSong.AllArtists = restoredSong.Artist;

                        _queue.SetSongs([restoredSong]);
                        _queue.SelectSong(restoredSong.Id);
                        song = _queue.CurrentSong;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NowPlaying] 恢复上次歌曲失败: {ex.Message}");
            }
        }

        if (song == null)
        {
            Title = "";
            Artist = "";
            Album = "";
            HasAlbum = false;
            CoverImage = null;
            HasCover = false;
            HasLyrics = false;
            ClearLyrics();
            _lastRecordedSongId = -1;
            _loadedSongId = -1;
            Duration = 0;
            Progress = 0;
            return;
        }

        // 同一首歌已经在播放，只需要同步显示信息，不重新播放
        var isSameSong = _loadedSongId == song.Id;
        _loadedSongId = song.Id;

        // Cancel previous load
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        // Basic info (always update — artist/title might change for WebDAV etc.)
        Title = song.Title ?? "未知歌曲";
        Artist = song.Artist ?? "未知艺术家";
        Album = song.Album ?? "未知专辑";
        HasAlbum = !string.IsNullOrEmpty(song.Album) && song.Album != "未知专辑";

        if (!isSameSong)
        {
            // 切歌：重置进度和歌词
            // Song.Duration 单位是毫秒，Slider 需要秒
            Duration = song.Duration > 0 ? song.Duration / 1000.0 : _audioService.Duration;
            TotalTimeDisplay = FormatTime(Duration);
            Progress = 0;
            CurrentTimeDisplay = "0:00";
            ClearLyrics();

            // 持久化当前歌曲 ID，下次启动可恢复
            Preferences.Default.Set("last_playing_song_id", song.Id);
        }

        // Check favorite
        try { IsLiked = await _db.IsFavoriteAsync(song.Id); }
        catch { IsLiked = false; }
        LikeIcon = IsLiked ? "\u2665" : "\u2661";
        LikeIconSource = IsLiked ? "ic_favorite" : "ic_favorite_border";

#if ANDROID
        // 更新前台播放通知
        try { (_audioService as Services.AudioPlayerService)?.UpdateSongInfo(Title, Artist); }
        catch { }
#endif

        // Update play mode display (read current state, don't cycle)
        RefreshPlayModeDisplay();

        // Update upcoming songs
        RefreshUpcomingSongs();

        if (!isSameSong && autoPlay)
        {
            // 换歌时且允许自动播放才启动播放
            if (!string.IsNullOrEmpty(song.FilePath))
            {
                try
                {
                    await _audioService.PlayAsync(song.FilePath);
                    if (_lastRecordedSongId != song.Id)
                    {
                        _lastRecordedSongId = song.Id;
                        _ = RecordPlayAsync(song.Id);
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Play error: {ex.Message}"); }
            }

            // 换歌时重新加载封面和歌词
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.WhenAll(
                        LoadCoverAsync(song, ct),
                        LoadLyricsAsync(song, ct)
                    );
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Load cover/lyrics error: {ex.Message}");
                }
            }, ct);
        }
        else if (!isSameSong && !autoPlay)
        {
            // 首次加载不自动播放，但加载封面和歌词
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.WhenAll(
                        LoadCoverAsync(song, ct),
                        LoadLyricsAsync(song, ct)
                    );
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Load cover/lyrics error: {ex.Message}");
                }
            }, ct);
        }
        else
        {
            // 同一首歌回到播放页：恢复正确的播放/暂停状态图标
            PlayPauseIcon = _audioService.IsPlaying ? "\u23f8" : "\u25b6";
            PlayPauseIconSource = _audioService.IsPlaying ? "ic_pause" : "ic_play";
        }
    }

    private void RefreshUpcomingSongs()
    {
        UpcomingSongs.Clear();
        foreach (var s in _queue.GetUpcomingSongs(10))
            UpcomingSongs.Add(s);
    }

    // === Cover Art Loading ===

    private async Task LoadCoverAsync(Song song, CancellationToken ct)
    {
        string? coverPath = null;

        // 1. Check existing CoverArtPath
        if (!string.IsNullOrEmpty(song.CoverArtPath) && File.Exists(song.CoverArtPath))
        {
            coverPath = song.CoverArtPath;
        }

        // 2. Check cached cover
        if (coverPath == null)
        {
            var cachedPath = Path.Combine(_coverCacheDir, $"cover_{song.Id}.jpg");
            if (File.Exists(cachedPath))
                coverPath = cachedPath;
        }

        // 3. Extract embedded cover
        if (coverPath == null && !string.IsNullOrEmpty(song.FilePath))
        {
            ct.ThrowIfCancellationRequested();

            // Android SAF content:// 路径：用 MediaMetadataRetriever.GetEmbeddedPicture() 提取
#if ANDROID
            if (song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                coverPath = await Task.Run(() =>
                    ExtractCoverFromContentUri(song.FilePath, song.Id), ct);
            }
            else
#endif
            if (!song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase)
                && File.Exists(song.FilePath))
            {
                coverPath = await Task.Run(() =>
                    TagReader.ExtractCoverArtToFile(song.FilePath, _coverCacheDir), ct);
            }
        }

        // 4. Try IMusicLibraryService (handles network covers etc.)
        if (coverPath == null)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var stream = await _musicLibrary.GetAlbumCoverAsync(song);
                if (stream != null)
                {
                    var cachedPath = Path.Combine(_coverCacheDir, $"cover_{song.Id}.jpg");
                    using (var fs = File.Create(cachedPath))
                        await stream.CopyToAsync(fs, ct);
                    stream.Dispose();
                    coverPath = cachedPath;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* ignore */ }
        }

        ct.ThrowIfCancellationRequested();

        if (coverPath != null)
        {
            var path = coverPath;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // Use FromStream instead of FromFile because FileImageSource on Android
                // may not render absolute paths like /data/.../cache/covers/cover_123.jpg
                CoverImage = ImageSource.FromStream(() => File.OpenRead(path));
                HasCover = true;
            });
        }
        else
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                CoverImage = null;
                HasCover = false;
            });
        }
    }

#if ANDROID
    /// <summary>
    /// 从 Android SAF content:// URI 提取嵌入封面并缓存为 jpg 文件。
    /// 使用 MediaMetadataRetriever.GetEmbeddedPicture()，支持 content:// 媒体路径。
    /// </summary>
    private string? ExtractCoverFromContentUri(string contentUri, int songId)
    {
        try
        {
            var ctx = global::Android.App.Application.Context;
            var retriever = new global::Android.Media.MediaMetadataRetriever();
            try
            {
                retriever.SetDataSource(ctx, global::Android.Net.Uri.Parse(contentUri));
                var bytes = retriever.GetEmbeddedPicture();
                if (bytes == null || bytes.Length == 0) return null;

                Directory.CreateDirectory(_coverCacheDir);
                var outPath = Path.Combine(_coverCacheDir, $"cover_{songId}.jpg");
                File.WriteAllBytes(outPath, bytes);
                return outPath;
            }
            finally
            {
                retriever.Release();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CoverArt] content:// 提取失败: {ex.Message}");
            return null;
        }
    }
#endif

    // === Lyrics Loading ===

    private async Task LoadLyricsAsync(Song song, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        LrcLyrics? lyrics = null;
        try
        {
            System.Diagnostics.Debug.WriteLine($"[Lyrics] 开始加载歌词: {song.Title} (Id={song.Id}, Path={song.FilePath?.Substring(0, Math.Min(60, song.FilePath?.Length ?? 0))}...)");
            System.Diagnostics.Debug.WriteLine($"[Lyrics] LyricsPath={song.LyricsPath ?? "null"}");

            lyrics = await _lyrics.GetLyricsAsync(song);

            System.Diagnostics.Debug.WriteLine($"[Lyrics] 结果: {(lyrics != null ? $"{lyrics.Lines.Count} 行" : "null")}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Lyrics] 加载异常: {ex.GetType().Name}: {ex.Message}");
        }

        ct.ThrowIfCancellationRequested();

        if (lyrics != null && lyrics.Lines.Count > 0)
        {
            _currentLyrics = lyrics;
            _currentLyricIndex = -1;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                HasLyrics = true;
                NoLyricsText = "";
            });
            System.Diagnostics.Debug.WriteLine($"[Lyrics] 歌词已加载，首行: {lyrics.Lines[0].Text}");
        }
        else
        {
            _currentLyrics = null;
            _currentLyricIndex = -1;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                HasLyrics = false;
                NoLyricsText = "暂无歌词";
                ClearLyrics();
            });
            System.Diagnostics.Debug.WriteLine("[Lyrics] 未找到歌词");
        }
    }

    private void UpdateLyricPosition(TimeSpan position)
    {
        if (_currentLyrics == null || _currentLyrics.Lines.Count == 0)
            return;

        var newIndex = _lyrics.GetCurrentLyricIndex(_currentLyrics, position);
        if (newIndex == _currentLyricIndex)
            return;

        _currentLyricIndex = newIndex;
        CurrentLyricIndexObservable = newIndex;
        var lines = _currentLyrics.Lines;

        LyricCurrent = GetLineText(lines, newIndex);
        LyricLine0 = GetLineText(lines, newIndex - 2);
        LyricLine1 = GetLineText(lines, newIndex - 1);
        LyricLine2 = GetLineText(lines, newIndex + 1);
        LyricLine3 = GetLineText(lines, newIndex + 2);
    }

    private static string GetLineText(List<LrcLyricLine> lines, int index)
    {
        if (index < 0 || index >= lines.Count) return "";
        return lines[index].Text;
    }

    private void ClearLyrics()
    {
        LyricCurrent = "";
        LyricLine0 = "";
        LyricLine1 = "";
        LyricLine2 = "";
        LyricLine3 = "";
    }

    private async Task RecordPlayAsync(int songId)
    {
        try
        {
            await _db.EnsureInitializedAsync();
            await _db.RecordPlayAsync(songId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NowPlayingViewModel] 记录播放失败: {ex.Message}");
        }
    }

    // === Utilities ===

    private static string FormatTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }
}
