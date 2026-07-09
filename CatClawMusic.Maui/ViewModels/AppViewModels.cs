using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 正在播放页 ViewModel：承载当前播放歌曲信息、播放控制（播放/暂停/上一首/下一首/进度跳转）、
/// 播放模式切换、收藏、封面加载、歌词同步、播放队列持久化与启动恢复等核心交互逻辑。
/// </summary>
public partial class NowPlayingViewModel : ObservableObject
{
    private readonly PlayQueue _queue;
    private readonly ILyricsService _lyrics;
    private readonly MusicDatabase _db;
    private readonly IAudioPlayerService _audioService;
    private readonly IMusicLibraryService _musicLibrary;
    private readonly Services.DesktopLyricManager? _desktopLyricManager;

    private string _coverCacheDir = "";
    private bool _isSeeking;
    private DateTime _seekStartTime = DateTime.MinValue;
    private int _lastRecordedSongId = -1;
    /// <summary>上次 LoadCurrentSongAsync 加载的歌曲ID，用于判断切页时是否需要重新播放</summary>
    private int _loadedSongId = -1;
    private CancellationTokenSource? _loadCts;
    /// <summary>标记启动恢复，避免恢复后自动播放</summary>
    private bool _isStartupRestore;

    // === Basic Song Info ===

    /// <summary>当前歌曲标题</summary>
    [ObservableProperty] private string _title = "";
    /// <summary>当前歌曲艺术家</summary>
    [ObservableProperty] private string _artist = "";
    /// <summary>当前歌曲专辑名</summary>
    [ObservableProperty] private string _album = "";
    /// <summary>是否存在有效的专辑信息</summary>
    [ObservableProperty] private bool _hasAlbum;

    // === Cover Art ===
    /// <summary>当前歌曲封面图片源</summary>
    [ObservableProperty] private ImageSource? _coverImage;
    /// <summary>是否存在可用封面</summary>
    [ObservableProperty] private bool _hasCover;
    /// <summary>当前封面图片的本地文件路径（供取色和跨实例缓存共享用）</summary>
    [ObservableProperty] private string? _currentCoverPath;

    // === Playback State ===
    /// <summary>是否正在播放</summary>
    [ObservableProperty] private bool _isPlaying;
    /// <summary>当前播放进度（秒）</summary>
    [ObservableProperty] private double _progress;
    /// <summary>歌曲总时长（秒）</summary>
    [ObservableProperty] private double _duration;
    /// <summary>音量（0.0 - 1.0）</summary>
    [ObservableProperty] private double _volume = 1.0;
    /// <summary>当前播放时间显示文本</summary>
    [ObservableProperty] private string _currentTimeDisplay = "0:00";
    /// <summary>总时长显示文本</summary>
    [ObservableProperty] private string _totalTimeDisplay = "0:00";

    // === Play Mode ===
    /// <summary>播放模式图标字符（Unicode 符号）</summary>
    [ObservableProperty] private string _playModeIcon = "\U0001f501"; // 🔁 list repeat
    /// <summary>播放模式显示文本</summary>
    [ObservableProperty] private string _playModeLabel = "列表循环";
    /// <summary>播放模式图标资源名称</summary>
    [ObservableProperty] private string _playModeIconSource = "ic_repeat_all";

    // === Play/Pause ===
    /// <summary>播放/暂停按钮图标字符（▶ 或 ⏸）</summary>
    [ObservableProperty] private string _playPauseIcon = "\u25b6"; // ▶
    /// <summary>播放/暂停按钮图标资源名称</summary>
    [ObservableProperty] private string _playPauseIconSource = "ic_play";

    // === Like ===
    /// <summary>当前歌曲是否已收藏</summary>
    [ObservableProperty] private bool _isLiked;
    /// <summary>收藏按钮图标字符（♡ 或 ♥）</summary>
    [ObservableProperty] private string _likeIcon = "\u2661"; // ♡
    /// <summary>收藏按钮图标资源名称</summary>
    [ObservableProperty] private string _likeIconSource = "ic_favorite_border";

    // === Lyrics ===
    /// <summary>是否存在可用歌词</summary>
    [ObservableProperty] private bool _hasLyrics;
    /// <summary>歌词 CollectionView 占位数据源（歌词内容放在 Header 中，使用 CollectionView 获得更好的手势处理）</summary>
    public ObservableCollection<int> LyricPlaceholderItems { get; } = new() { 0 };
    /// <summary>歌词显示行：当前行前第 4 行</summary>
    [ObservableProperty] private string _lyricLine0 = "";  // 4 lines before
    /// <summary>歌词显示行：当前行前第 3 行</summary>
    [ObservableProperty] private string _lyricLine1 = "";  // 3 lines before
    /// <summary>歌词显示行：当前行前第 2 行</summary>
    [ObservableProperty] private string _lyricLine2 = "";  // 2 lines before
    /// <summary>歌词显示行：当前行前第 1 行</summary>
    [ObservableProperty] private string _lyricLine3 = "";  // 1 line before
    /// <summary>歌词显示行：当前行</summary>
    [ObservableProperty] private string _lyricCurrent = ""; // current
    /// <summary>歌词显示行：当前行后第 1 行</summary>
    [ObservableProperty] private string _lyricLine4 = "";  // 1 line after
    /// <summary>歌词显示行：当前行后第 2 行</summary>
    [ObservableProperty] private string _lyricLine5 = "";  // 2 lines after
    /// <summary>歌词显示行：当前行后第 3 行</summary>
    [ObservableProperty] private string _lyricLine6 = "";  // 3 lines after
    /// <summary>歌词显示行：当前行后第 4 行</summary>
    [ObservableProperty] private string _lyricLine7 = "";  // 4 lines after
    /// <summary>无歌词时的提示文本</summary>
    [ObservableProperty] private string _noLyricsText = "暂无歌词";

    // Full lyrics (for FullLyricsPage)
    /// <summary>当前高亮的歌词行索引（供全屏歌词页使用）</summary>
    [ObservableProperty] private int _currentLyricIndexObservable = -1;
    /// <summary>全部歌词行（供全屏歌词页使用，只读）</summary>
    public IReadOnlyList<LrcLyricLine>? AllLyricLines => _currentLyrics?.Lines;
    /// <summary>当前行的逐字填充进度（0~1），供 KaraokeLabel 使用以实现 Apple Music 风格逐字渐进填充</summary>
    [ObservableProperty] private double _currentLineFillProgress = 0.0;

    private LrcLyrics? _currentLyrics;
    private int _currentLyricIndex = -1;
    /// <summary>上次播放位置缓存，用于设置切换时重新计算逐字进度</summary>
    private TimeSpan _lastPosition = TimeSpan.Zero;

    /// <summary>
    /// 刷新逐字填充进度（设置切换逐行/逐字模式后调用）。
    /// 用上次播放位置重新计算当前行填充进度。
    /// </summary>
    public void RefreshFillProgress()
    {
        if (_currentLyrics == null || _currentLyricIndex < 0)
        {
            CurrentLineFillProgress = 0.0;
            return;
        }
        UpdateFillProgress(_currentLyricIndex, _lastPosition);
    }

    // === Upcoming Songs (for playlist drawer) ===
    /// <summary>即将播放的歌曲列表（用于播放队列抽屉展示）</summary>
    public ObservableCollection<Song> UpcomingSongs { get; } = new();

    /// <summary>当前播放队列中的歌曲</summary>
    public Song? CurrentSong => _queue.CurrentSong;

    /// <summary>暴露 AudioService 的 Duration 供 NowPlayingPage timer 直接拉取</summary>
    public double AudioServiceDuration => _audioService.Duration;

    /// <summary>
    /// 初始化 <see cref="NowPlayingViewModel"/> 实例，订阅音频播放事件并创建播放控制命令。
    /// </summary>
    /// <param name="queue">播放队列</param>
    /// <param name="lyrics">歌词服务</param>
    /// <param name="db">音乐数据库访问对象</param>
    /// <param name="audioService">音频播放服务</param>
    /// <param name="musicLibrary">音乐库服务，用于获取封面等</param>
    public NowPlayingViewModel(
        PlayQueue queue,
        ILyricsService lyrics,
        MusicDatabase db,
        IAudioPlayerService audioService,
        IMusicLibraryService musicLibrary,
        Services.DesktopLyricManager? desktopLyricManager = null)
    {
        _queue = queue;
        _lyrics = lyrics;
        _db = db;
        _audioService = audioService;
        _musicLibrary = musicLibrary;
        _desktopLyricManager = desktopLyricManager;

        // Initialize cover cache directory
        _coverCacheDir = Path.Combine(FileSystem.CacheDirectory, "covers");
        Directory.CreateDirectory(_coverCacheDir);

        // Subscribe to audio events
        _audioService.PlaybackStateChanged += OnPlaybackStateChanged;
        _audioService.PositionChanged += OnPositionChanged;
        _audioService.PlaybackCompleted += OnPlaybackCompleted;

#if ANDROID
        // 订阅通知栏媒体控件回调（下一首/上一首/收藏），由 ForegroundPlayerService 触发
        if (_audioService is Services.AudioPlayerService androidAudio)
        {
            androidAudio.PlayNextRequested += OnNotifPlayNext;
            androidAudio.PlayPreviousRequested += OnNotifPlayPrevious;
            androidAudio.FavoriteToggled += OnNotifFavoriteToggled;
            // 通知栏桌面歌词按钮切换
            androidAudio.DesktopLyricToggled += OnNotifDesktopLyricToggled;
        }
#endif

        // Commands
        TogglePlayPauseCommand = new AsyncRelayCommand(TogglePlayPauseAsync);
        PlayNextCommand = new AsyncRelayCommand(PlayNextAsync);
        PlayPreviousCommand = new AsyncRelayCommand(PlayPreviousAsync);
        CyclePlayModeCommand = new RelayCommand(CyclePlayMode);
        ToggleLikeCommand = new AsyncRelayCommand(ToggleLikeAsync);
        SeekCommand = new RelayCommand<double>(OnSeek);
    }

#if ANDROID
    /// <summary>通知栏"下一首"回调：切到下一首并加载（通知栏已自行刷新，这里只管队列与 UI）</summary>
    private async Task OnNotifPlayNext()
    {
        try
        {
            _queue.Next();
            await LoadCurrentSongAsync();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[NowPlayingVM] NotifNext: {ex.Message}"); }
    }

    /// <summary>通知栏"上一首"回调：切到上一首并加载</summary>
    private async Task OnNotifPlayPrevious()
    {
        try
        {
            _queue.Previous();
            await LoadCurrentSongAsync();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[NowPlayingVM] NotifPrev: {ex.Message}"); }
    }

    /// <summary>通知栏"收藏"回调：将目标收藏状态持久化并同步 UI（不再回传通知栏，避免循环）</summary>
    /// <param name="isFavorite">目标收藏状态</param>
    private void OnNotifFavoriteToggled(bool isFavorite)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var song = _queue.CurrentSong;
            if (song == null) return;
            try
            {
                await _db.SetFavoriteAsync(song.Id, isFavorite);
                IsLiked = isFavorite;
                LikeIcon = isFavorite ? "\u2665" : "\u2661";
                LikeIconSource = isFavorite ? "ic_favorite" : "ic_favorite_border";
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[NowPlayingVM] NotifFav: {ex.Message}"); }
        });
    }

    /// <summary>通知栏"桌面歌词"按钮回调：切换桌面歌词开关</summary>
    private async void OnNotifDesktopLyricToggled(bool isEnabled)
    {
        if (_desktopLyricManager == null) return;
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (isEnabled)
                await _desktopLyricManager.EnableAsync();
            else
                _desktopLyricManager.Disable();
        });
    }
#endif

    // === Commands ===
    /// <summary>切换播放/暂停命令</summary>
    public IRelayCommand TogglePlayPauseCommand { get; }
    /// <summary>播放下一首命令</summary>
    public IRelayCommand PlayNextCommand { get; }
    /// <summary>播放上一首命令</summary>
    public IRelayCommand PlayPreviousCommand { get; }
    /// <summary>循环切换播放模式命令（列表循环 → 单曲循环 → 随机播放）</summary>
    public IRelayCommand CyclePlayModeCommand { get; }
    /// <summary>切换当前歌曲收藏状态命令</summary>
    public IRelayCommand ToggleLikeCommand { get; }
    /// <summary>进度跳转命令，参数为目标位置（秒）</summary>
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
            // Duration 小于 1 秒视为无效，从 audio service 重新拉取
            // (某些歌曲数据库存储的 Duration 不正确，需要依赖 ExoPlayer 的真实时长)
            if (Duration < 1 && _audioService.Duration > 1)
            {
                Duration = _audioService.Duration;
                TotalTimeDisplay = FormatTime(Duration);
                System.Diagnostics.Debug.WriteLine($"[NowPlayingVM] Duration updated from audio service: {Duration}s");
            }

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
            return;
        }

        var song = _queue.CurrentSong;
        if (song == null || string.IsNullOrEmpty(song.FilePath)) return;

        // 同一首歌已加载：使用 Resume 从暂停位置恢复，避免 PlayAsync 重新加载媒体导致从头播放
        if (_audioService.CurrentSongFilePath == song.FilePath)
        {
            await _audioService.ResumeAsync();
        }
        else
        {
            await _audioService.PlayAsync(song.FilePath);
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

#if ANDROID || WINDOWS
        try { (_audioService as Services.AudioPlayerService)?.UpdateFavoriteState(newFav); }
        catch { }
#endif
    }

    // === Seek ===

    private async void OnSeek(double positionSeconds)
    {
        _isSeeking = false;
        try { await _audioService.SeekAsync(TimeSpan.FromSeconds(positionSeconds)); }
        catch { }
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

    /// <summary>
    /// 加载播放队列中的当前歌曲：刷新基础信息、封面、歌词、播放模式与即将播放列表，
    /// 并在 <paramref name="autoPlay"/> 为 true 时自动播放（启动恢复除外）。
    /// </summary>
    /// <param name="autoPlay">是否在切换歌曲后自动播放</param>
    public async Task LoadCurrentSongAsync(bool autoPlay = true)
    {
        var song = _queue.CurrentSong;

        // 启动恢复：如果队列为空，尝试从 Preferences 恢复上次的整个播放队列
        if (song == null)
        {
            try
            {
                await _db.EnsureInitializedAsync();
                var (restoredSongs, restoredCurrentId) = await RestoreQueueStateAsync();
                if (restoredSongs.Count > 0 && restoredCurrentId > 0)
                {
                    _queue.SetSongs(restoredSongs);
                    _queue.SelectSong(restoredCurrentId);
                    song = _queue.CurrentSong;
                    // 标记启动恢复，避免自动播放
                    _isStartupRestore = true;
                    System.Diagnostics.Debug.WriteLine($"[NowPlaying] 恢复播放队列: {restoredSongs.Count} 首, 当前歌曲ID={restoredCurrentId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NowPlaying] 恢复播放队列失败: {ex.Message}");
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

        _loadCts?.Cancel();
        _loadCts?.Dispose();
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
            // 部分歌曲 Duration 可能存储不正确（过小），使用 _audioService.Duration 作为回退
            var songDurationSec = song.Duration > 1000 ? song.Duration / 1000.0 : 0;
            Duration = songDurationSec > 0 ? songDurationSec : _audioService.Duration;
            TotalTimeDisplay = Duration > 0 ? FormatTime(Duration) : "--:--";
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

#if ANDROID || WINDOWS
        // 更新前台播放通知 / Windows SMTC 显示
        try { (_audioService as Services.AudioPlayerService)?.UpdateSongInfo(Title, Artist); }
        catch { }
        try { (_audioService as Services.AudioPlayerService)?.UpdateFavoriteState(IsLiked); }
        catch { }
#endif

        // Update play mode display (read current state, don't cycle)
        RefreshPlayModeDisplay();

        // Update upcoming songs
        RefreshUpcomingSongs();

        if (!isSameSong && autoPlay && !_isStartupRestore)
        {
            // 换歌时且允许自动播放才启动播放（启动恢复除外）
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
        else if (!isSameSong && (!autoPlay || _isStartupRestore))
        {
            // 首次加载或启动恢复：加载封面和歌词，但不播放
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

        // 重置启动恢复标志
        _isStartupRestore = false;

        // 保存队列状态（歌曲ID列表 + 当前歌曲ID）
        SaveQueueState();
    }

    // === 队列状态持久化 ===

    /// <summary>保存当前播放队列状态到 Preferences</summary>
    private void SaveQueueState()
    {
        try
        {
            var songs = _queue.GetSongs();
            var songIds = songs.Select(s => s.Id).ToArray();
            var currentSong = _queue.CurrentSong;

            Preferences.Default.Set("queue_song_ids", string.Join(",", songIds));
            Preferences.Default.Set("queue_current_song_id", currentSong?.Id ?? -1);
            Preferences.Default.Set("queue_play_mode", (int)_queue.PlayMode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NowPlaying] 保存队列状态失败: {ex.Message}");
        }
    }

    /// <summary>从 Preferences 恢复播放队列状态（在线程池线程执行以避免阻塞主线程）</summary>
    private async Task<(List<Song> songs, int currentSongId)> RestoreQueueStateAsync()
    {
        try
        {
            // 将所有 I/O 与 SQLite 查询放到线程池线程，避免 sync-over-async 阻塞主线程
            return await Task.Run(async () =>
            {
                var songIdsStr = Preferences.Default.Get("queue_song_ids", "");
                var currentSongId = Preferences.Default.Get("queue_current_song_id", -1);
                var playMode = Preferences.Default.Get("queue_play_mode", (int)PlayMode.ListRepeat);

                if (string.IsNullOrEmpty(songIdsStr) || currentSongId <= 0)
                    return (new List<Song>(), -1);

                var songIds = songIdsStr.Split(',')
                    .Where(s => int.TryParse(s, out _))
                    .Select(int.Parse)
                    .ToList();

                if (songIds.Count == 0)
                    return (new List<Song>(), -1);

                // 并行查询所有歌曲（避免串行 await）
                var songTasks = songIds.Select(async id =>
                {
                    var song = await _db.GetSongByIdAsync(id);
                    if (song == null) return null;
                    // 并行查询 artist 和 album
                    var artistTask = _db.FindArtistByIdAsync(song.ArtistId);
                    var albumTask = _db.FindAlbumByIdAsync(song.AlbumId);
                    await Task.WhenAll(artistTask, albumTask);
                    song.Artist = artistTask.Result?.Name ?? "未知艺术家";
                    song.Album = albumTask.Result?.Title ?? "未知专辑";
                    song.AllArtists = song.Artist;
                    return song;
                }).ToList();
                var results = await Task.WhenAll(songTasks);
                var songs = results.Where(s => s != null).Cast<Song>().ToList();

                _queue.PlayMode = (PlayMode)playMode;
                return (songs, currentSongId);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NowPlaying] 恢复队列状态失败: {ex.Message}");
            return (new List<Song>(), -1);
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

            // Android SAF content:// 路径、远程 http(s):// URL 和 smb://（通过本地代理转 http）：用 MediaMetadataRetriever.GetEmbeddedPicture() 提取
#if ANDROID
            string? extractUri = null;
            if (song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                extractUri = song.FilePath;
            }
            else if (song.FilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || song.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var networkSvc = MauiProgram.Services.GetService<INetworkMusicService>();
                    if (networkSvc != null)
                    {
                        var resolved = await networkSvc.ResolveWebDavPlaybackUrlAsync(song.FilePath);
                        extractUri = string.IsNullOrEmpty(resolved) ? song.FilePath : resolved;
                    }
                    else
                    {
                        extractUri = song.FilePath;
                    }
                }
                catch
                {
                    extractUri = song.FilePath;
                }
            }
            else if (song.FilePath.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
            {
                var proxy = SmbStreamProxy.Current;
                proxy?.Start();
                extractUri = proxy?.ToProxyUrl(song.FilePath);
            }
            if (extractUri != null)
            {
                coverPath = await Task.Run(() =>
                    ExtractCoverFromContentUri(extractUri, song.Id), ct);
            }
            else
#endif
            if (!song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase)
                && !song.FilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !song.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !song.FilePath.StartsWith("smb://", StringComparison.OrdinalIgnoreCase)
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
            CurrentCoverPath = path;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // 使用 FileImageSource 而非 StreamImageSource：
                // 1. 避免 StreamImageSource 内部取消机制导致 FrostedBackground 加载失败
                // 2. 让 CachingFileImageSourceService 命中内存缓存，减少重复解码
                CoverImage = ImageSource.FromFile(path);
                HasCover = true;
            });

#if ANDROID || WINDOWS
            try { (_audioService as Services.AudioPlayerService)?.UpdateCoverPath(path); }
            catch { }
#endif
        }
        else
        {
            CurrentCoverPath = null;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                CoverImage = null;
                HasCover = false;
            });

#if ANDROID || WINDOWS
            try { (_audioService as Services.AudioPlayerService)?.UpdateCoverPath(null); }
            catch { }
#endif
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
                CurrentLyricIndexObservable = -1;
                OnPropertyChanged(nameof(AllLyricLines));
            });
            _desktopLyricManager?.SetLyrics(lyrics);
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
                OnPropertyChanged(nameof(AllLyricLines));
            });
            _desktopLyricManager?.SetLyrics(null);
            System.Diagnostics.Debug.WriteLine("[Lyrics] 未找到歌词");
        }
    }

    private void UpdateLyricPosition(TimeSpan position)
    {
        if (_currentLyrics == null || _currentLyrics.Lines.Count == 0)
            return;

        var newIndex = _lyrics.GetCurrentLyricIndex(_currentLyrics, position);

        // 即使行索引不变，也要更新逐字填充进度（Apple Music 风格逐字渐进填充）
        UpdateFillProgress(newIndex, position);

        if (newIndex == _currentLyricIndex)
            return;

        _currentLyricIndex = newIndex;
        CurrentLyricIndexObservable = newIndex;
        var lines = _currentLyrics.Lines;

        LyricCurrent = GetLineText(lines, newIndex);
        LyricLine0 = GetLineText(lines, newIndex - 4);
        LyricLine1 = GetLineText(lines, newIndex - 3);
        LyricLine2 = GetLineText(lines, newIndex - 2);
        LyricLine3 = GetLineText(lines, newIndex - 1);
        LyricLine4 = GetLineText(lines, newIndex + 1);
        LyricLine5 = GetLineText(lines, newIndex + 2);
        LyricLine6 = GetLineText(lines, newIndex + 3);
        LyricLine7 = GetLineText(lines, newIndex + 4);
    }

    /// <summary>
    /// 计算并更新当前行的逐字填充进度（Apple Music 风格）。
    /// 逐行模式：整行实心（1.0）；逐字模式：按音节时间精确映射或线性填充。
    /// </summary>
    private void UpdateFillProgress(int lineIndex, TimeSpan position)
    {
        _lastPosition = position;

        if (lineIndex < 0 || lineIndex >= _currentLyrics!.Lines.Count)
        {
            CurrentLineFillProgress = 0.0;
            return;
        }

        var settingsMode = Services.LyricsSettingsService.Instance.LyricsMode;
        if (settingsMode == Services.LyricsSettingsService.Mode.Line)
        {
            // 逐行模式：当前行整行实心
            CurrentLineFillProgress = 1.0;
            return;
        }

        // 逐字模式
        var line = _currentLyrics.Lines[lineIndex];
        var lineStart = line.Timestamp;
        var lineEnd = lineIndex + 1 < _currentLyrics.Lines.Count
            ? _currentLyrics.Lines[lineIndex + 1].Timestamp
            : lineStart + TimeSpan.FromSeconds(5);

        if (position <= lineStart)
        {
            CurrentLineFillProgress = 0;
            return;
        }
        if (position >= lineEnd)
        {
            CurrentLineFillProgress = 1.0;
            return;
        }

        // 逐字 LRC：按音节时间精确加权
        if (line.WordTimestamps != null && line.WordTimestamps.Count > 0)
        {
            CurrentLineFillProgress = CalculateSyllableProgress(line.WordTimestamps, position, lineStart, lineEnd);
        }
        else
        {
            // 标准 LRC：按时间线性填充（Apple Music 对无逐字时间戳的歌词也采用此回退）
            var totalMs = (lineEnd - lineStart).TotalMilliseconds;
            var elapsedMs = (position - lineStart).TotalMilliseconds;
            CurrentLineFillProgress = totalMs > 0 ? Math.Clamp(elapsedMs / totalMs, 0.0, 1.0) : 1.0;
        }
    }

    /// <summary>
    /// 按音节时间戳计算填充进度：已唱完的音节贡献其字符比例，当前音节按时间进度部分贡献。
    /// 用字符数加权近似字符宽度（变宽字符可能有微小偏差，视觉效果接近 Apple Music）。
    /// </summary>
    private static double CalculateSyllableProgress(
        List<CatClawMusic.Core.Models.WordTimestamp> syllables,
        TimeSpan position, TimeSpan lineStart, TimeSpan lineEnd)
    {
        var totalChars = syllables.Sum(s => s.Word?.Length ?? 0);
        if (totalChars == 0) return 0;

        double filledChars = 0;
        foreach (var syl in syllables)
        {
            if (string.IsNullOrEmpty(syl.Word)) continue;
            var sylStart = syl.Start;
            var sylDur = syl.Duration;
            if (sylDur <= TimeSpan.Zero)
                sylDur = TimeSpan.FromMilliseconds(300);
            var sylEnd = sylStart + sylDur;

            if (position >= sylEnd)
            {
                // 已唱完
                filledChars += syl.Word.Length;
            }
            else if (position > sylStart)
            {
                // 当前音节：按时间进度部分填充
                var sylProgress = (position - sylStart).TotalMilliseconds / sylDur.TotalMilliseconds;
                sylProgress = Math.Clamp(sylProgress, 0.0, 1.0);
                filledChars += syl.Word.Length * sylProgress;
            }
        }

        return Math.Clamp(filledChars / totalChars, 0.0, 1.0);
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
        LyricLine4 = "";
        LyricLine5 = "";
        LyricLine6 = "";
        LyricLine7 = "";
        OnPropertyChanged(nameof(AllLyricLines));
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
