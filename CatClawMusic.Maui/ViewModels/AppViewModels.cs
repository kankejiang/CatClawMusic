using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
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
    private readonly IInteractionStateService? _interactionState;
    private readonly Services.SleepTimerService? _sleepTimer;

    private string _coverCacheDir = "";
    private bool _isSeeking;
    private DateTime _seekStartTime = DateTime.MinValue;
    private int _lastRecordedSongId = -1;
    /// <summary>上次格式化的播放秒数（整数），用于跳过未变秒数的 FormatTime 调用</summary>
    private int _lastDisplayedSecond = -1;
    /// <summary>上次 LoadCurrentSongAsync 加载的歌曲ID，用于判断切页时是否需要重新播放</summary>
    private int _loadedSongId = -1;
    private CancellationTokenSource? _loadCts;
    /// <summary>标记启动恢复，避免恢复后自动播放</summary>
    private bool _isStartupRestore;
    /// <summary>已触发预缓冲的歌曲ID，避免重复触发</summary>
    private int _preBufferedSongId = -1;

    // === 听歌时长追踪 ===
    /// <summary>当前正在追踪时长的歌曲ID</summary>
    private int _trackedSongId = -1;
    /// <summary>本次连续播放开始时间（UTC），用于计算实时长</summary>
    private DateTime _listeningStartUtc = DateTime.MinValue;
    /// <summary>已累积但尚未写入数据库的聆听时长（毫秒）</summary>
    private long _pendingListenMs;
    /// <summary>上次定时 flush 的整数秒，用于 30 秒周期判断</summary>
    private int _lastFlushSecond = -1;
    /// <summary>听歌记录写入锁，避免并发重复写入</summary>
    private readonly object _listenRecordLock = new();

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
    /// <summary>用户是否正在滑动列表（绑定到 FrostedBackground.IsScrolling，滑动时暂停背景动画）</summary>
    [ObservableProperty] private bool _isUserScrolling;
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
    /// <summary>播放模式图标 ImageSource（由资源名转换）</summary>
    [ObservableProperty] private ImageSource? _playModeIconSource = ImageSourceHelper.FromNameThemed("ic_repeat_all");
    /// <summary>播放模式白色图标 ImageSource（用于主题色/透明背景）</summary>
    [ObservableProperty] private ImageSource? _playModeIconSourceWhite = ImageSourceHelper.FromNameOriginal("ic_repeat_all");

    // === Play/Pause ===
    /// <summary>播放/暂停按钮图标字符（▶ 或 ⏸）</summary>
    [ObservableProperty] private string _playPauseIcon = "\u25b6"; // ▶
    /// <summary>播放/暂停按钮图标 ImageSource（由资源名转换）</summary>
    [ObservableProperty] private ImageSource? _playPauseIconSource = ImageSourceHelper.FromNameThemed("ic_play");
    /// <summary>播放/暂停按钮白色图标 ImageSource（用于主题色/透明背景）</summary>
    [ObservableProperty] private ImageSource? _playPauseIconSourceWhite = ImageSourceHelper.FromNameOriginal("ic_play");

    // === Like ===
    /// <summary>当前歌曲是否已收藏</summary>
    [ObservableProperty] private bool _isLiked;
    /// <summary>收藏按钮图标字符（♡ 或 ♥）</summary>
    [ObservableProperty] private string _likeIcon = "\u2661"; // ♡
    /// <summary>收藏按钮图标 ImageSource（由资源名转换）</summary>
    [ObservableProperty] private ImageSource? _likeIconSource = ImageSourceHelper.FromNameThemed("ic_favorite_border");
    /// <summary>收藏按钮白色图标 ImageSource（用于主题色/透明背景）</summary>
    [ObservableProperty] private ImageSource? _likeIconSourceWhite = ImageSourceHelper.FromNameOriginal("ic_favorite_border_white");

    // === Previous / Next ===
    /// <summary>上一首按钮图标 ImageSource</summary>
    [ObservableProperty] private ImageSource? _playPreviousIconSource = ImageSourceHelper.FromNameThemed("ic_skip_previous");
    /// <summary>上一首按钮白色图标 ImageSource（用于主题色/透明背景）</summary>
    [ObservableProperty] private ImageSource? _playPreviousIconSourceWhite = ImageSourceHelper.FromNameOriginal("ic_skip_previous");
    /// <summary>下一首按钮图标 ImageSource</summary>
    [ObservableProperty] private ImageSource? _playNextIconSource = ImageSourceHelper.FromNameThemed("ic_skip_next");
    /// <summary>下一首按钮白色图标 ImageSource（用于主题色/透明背景）</summary>
    [ObservableProperty] private ImageSource? _playNextIconSourceWhite = ImageSourceHelper.FromNameOriginal("ic_skip_next");
    /// <summary>播放列表按钮图标 ImageSource</summary>
    [ObservableProperty] private ImageSource? _playlistIconSource = ImageSourceHelper.FromNameOriginal("ic_playlist");
    /// <summary>歌词按钮白色图标 ImageSource</summary>
    [ObservableProperty] private ImageSource? _lyricsIconSource = ImageSourceHelper.FromNameOriginal("ic_lyrics_white");

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
    /// <summary>当前高亮的歌词行索引（供全屏歌词页使用，基于过滤后列表）</summary>
    [ObservableProperty] private int _currentLyricIndexObservable = -1;
    /// <summary>全部歌词行（供全屏歌词页使用，只读，已按设置过滤空行）</summary>
    public IReadOnlyList<LrcLyricLine>? AllLyricLines => _filteredLines ?? _currentLyrics?.Lines;
    /// <summary>当前行的逐字填充进度（0~1），供 KaraokeLabel 使用以实现 Apple Music 风格逐字渐进填充</summary>
    private double _currentLineFillProgress = 0.0;
    /// <summary>当前行的逐字填充进度（0~1）。仅在变化超过 0.003 时触发 PropertyChanged，避免无意义重绘</summary>
    public double CurrentLineFillProgress
    {
        get => _currentLineFillProgress;
        set
        {
            if (Math.Abs(_currentLineFillProgress - value) < 0.0015)
                return;
            _currentLineFillProgress = value;
            OnPropertyChanged();
        }
    }

    private LrcLyrics? _currentLyrics;
    private int _currentLyricIndex = -1;
    /// <summary>上次播放位置缓存，用于设置切换时重新计算逐字进度</summary>
    private TimeSpan _lastPosition = TimeSpan.Zero;

    // 空行过滤相关：_filteredLines 为过滤后的列表，_originalToFilteredMap 映射原始索引→过滤后索引（-1 表示被过滤）
    private List<LrcLyricLine>? _filteredLines;
    private int[]? _originalToFilteredMap;

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
        Services.DesktopLyricManager? desktopLyricManager = null,
        IInteractionStateService? interactionState = null,
        INetworkMusicService? networkMusic = null,
        Services.SleepTimerService? sleepTimer = null)
    {
        _queue = queue;
        _lyrics = lyrics;
        _db = db;
        _audioService = audioService;
        _musicLibrary = musicLibrary;
        _desktopLyricManager = desktopLyricManager;
        _interactionState = interactionState;
        _networkMusic = networkMusic;
        _sleepTimer = sleepTimer;

        // Initialize cover cache directory
        _coverCacheDir = Path.Combine(FileSystem.CacheDirectory, "covers");
        Directory.CreateDirectory(_coverCacheDir);

        // Subscribe to audio events
        _audioService.PlaybackStateChanged += OnPlaybackStateChanged;
        _audioService.PositionChanged += OnPositionChanged;
        _audioService.DurationChanged += OnDurationChanged;
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
        // 桌面歌词开启失败时，回退通知栏按钮状态
        if (_desktopLyricManager != null)
            _desktopLyricManager.EnableFailed += OnDesktopLyricEnableFailed;
#endif

        // Commands
        TogglePlayPauseCommand = new AsyncRelayCommand(TogglePlayPauseAsync);
        PlayNextCommand = new AsyncRelayCommand(PlayNextAsync);
        PlayPreviousCommand = new AsyncRelayCommand(PlayPreviousAsync);
        CyclePlayModeCommand = new RelayCommand(CyclePlayMode);
        ToggleLikeCommand = new AsyncRelayCommand(ToggleLikeAsync);
        SeekCommand = new RelayCommand<double>(OnSeek);
        PlaySongFromQueueCommand = new AsyncRelayCommand<Song>(PlaySongFromQueueAsync);
        RemoveSongFromQueueCommand = new AsyncRelayCommand<Song>(RemoveSongFromQueueAsync);

        if (_interactionState != null)
        {
            _interactionState.InteractionStateChanged += OnInteractionStateChanged;
            // 订阅滚动状态变化：滑动时暂停雾面背景动画，释放主线程/GPU 给列表渲染
            _interactionState.ScrollStateChanged += OnScrollStateChanged;
        }

        if (Application.Current != null)
            Application.Current.RequestedThemeChanged += OnRequestedThemeChanged;
    }

    private void OnInteractionStateChanged(object? sender, bool isInteracting)
    {
        // Tab 滑动等交互也暂停 FrostedBackground 动画（通过 IsUserScrolling 绑定）。
        // MainPage 的 Pan 手势使用 BeginInteraction 而非 NotifyScrollStarted，
        // 避免与 CollectionView 的滚动状态共享 _scrollRefCount 导致抽搐。
        MainThread.BeginInvokeOnMainThread(() =>
            IsUserScrolling = isInteracting || (_interactionState?.IsUserScrolling ?? false));

        if (!isInteracting && _audioService.IsPlaying && !_isSeeking)
        {
            // 使用播放器实时位置而非 Progress，因为用户滚动期间 Progress 未被更新
            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdateLyricPosition(TimeSpan.FromSeconds(_audioService.CurrentPosition));
            });
        }
    }

    /// <summary>滚动状态变化：更新 IsUserScrolling，FrostedBackground 绑定此属性以暂停/恢复动画</summary>
    private void OnScrollStateChanged(object? sender, bool isScrolling)
    {
        // 同时检查交互状态：Tab 滑动使用 BeginInteraction，不触发 ScrollStateChanged，
        // 但 CollectionView 滚动停止时不应恢复动画如果用户正在 Tab 滑动
        MainThread.BeginInvokeOnMainThread(() =>
            IsUserScrolling = isScrolling || (_interactionState?.IsUserInteracting ?? false));
    }

    /// <summary>主题切换时刷新播放控制图标，使其使用对应深浅色变体</summary>
    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PlayPauseIconSource = ImageSourceHelper.FromNameThemed(_audioService.IsPlaying ? "ic_pause" : "ic_play");
            PlayPauseIconSourceWhite = ImageSourceHelper.FromNameOriginal(_audioService.IsPlaying ? "ic_pause" : "ic_play");
            PlayPreviousIconSource = ImageSourceHelper.FromNameThemed("ic_skip_previous");
            PlayPreviousIconSourceWhite = ImageSourceHelper.FromNameOriginal("ic_skip_previous");
            PlayNextIconSource = ImageSourceHelper.FromNameThemed("ic_skip_next");
            PlayNextIconSourceWhite = ImageSourceHelper.FromNameOriginal("ic_skip_next");
            RefreshPlayModeDisplay();
            LikeIconSource = ImageSourceHelper.FromNameThemed(IsLiked ? "ic_favorite" : "ic_favorite_border");
            LikeIconSourceWhite = ImageSourceHelper.FromNameOriginal(IsLiked ? "ic_favorite_white" : "ic_favorite_border_white");
        });
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
        catch (Exception ex) { Log.Debug("AppViewModels", $"[NowPlayingVM] NotifNext: {ex.Message}"); }
    }

    /// <summary>通知栏"上一首"回调：切到上一首并加载</summary>
    private async Task OnNotifPlayPrevious()
    {
        try
        {
            _queue.Previous();
            await LoadCurrentSongAsync();
        }
        catch (Exception ex) { Log.Debug("AppViewModels", $"[NowPlayingVM] NotifPrev: {ex.Message}"); }
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
                LikeIconSource = ImageSourceHelper.FromNameThemed(isFavorite ? "ic_favorite" : "ic_favorite_border");
                LikeIconSourceWhite = ImageSourceHelper.FromNameOriginal(isFavorite ? "ic_favorite_white" : "ic_favorite_border_white");
            }
            catch (Exception ex) { Log.Debug("AppViewModels", $"[NowPlayingVM] NotifFav: {ex.Message}"); }
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

    /// <summary>桌面歌词开启失败（权限不足等）：回退通知栏按钮状态</summary>
    private void OnDesktopLyricEnableFailed()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Platforms.Android.ForegroundPlayerService.SyncLyricsEnabled(false);
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
    /// <summary>从播放队列中选择一首歌播放</summary>
    public IAsyncRelayCommand<Song> PlaySongFromQueueCommand { get; }
    /// <summary>从播放队列中移除一首歌</summary>
    public IAsyncRelayCommand<Song> RemoveSongFromQueueCommand { get; }

    private void OnPlaybackStateChanged(object? sender, bool isPlaying)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            IsPlaying = isPlaying;
            PlayPauseIcon = isPlaying ? "\u23f8" : "\u25b6"; // ⏸ or ▶
            PlayPauseIconSource = ImageSourceHelper.FromNameThemed(isPlaying ? "ic_pause" : "ic_play");
            PlayPauseIconSourceWhite = ImageSourceHelper.FromNameOriginal(isPlaying ? "ic_pause" : "ic_play");

            // 听歌时长追踪：播放开始时记录起点，暂停时累积时长
            lock (_listenRecordLock)
            {
                if (isPlaying)
                {
                    var currentSongId = _queue.CurrentSong?.Id ?? 0;
                    if (currentSongId > 0)
                    {
                        if (_trackedSongId != currentSongId)
                        {
                            // 换歌后首次播放：重置追踪状态
                            _trackedSongId = currentSongId;
                            _pendingListenMs = 0;
                        }
                        _listeningStartUtc = DateTime.UtcNow;
                    }
                }
                else
                {
                    // 暂停：把已播放时长累加到 pending
                    if (_trackedSongId > 0 && _listeningStartUtc != DateTime.MinValue)
                    {
                        var elapsed = (long)(DateTime.UtcNow - _listeningStartUtc).TotalMilliseconds;
                        if (elapsed > 0)
                        {
                            _pendingListenMs += elapsed;
                        }
                        _listeningStartUtc = DateTime.MinValue;
                    }
                }
            }

            // 检测队列当前歌曲是否变化（外部页面播放时触发）
            // 此时 _loadedSongId 还是旧值，需要加载新歌信息更新迷你播放器
            var queueSong = _queue.CurrentSong;
            if (isPlaying && queueSong != null && queueSong.Id != _loadedSongId)
            {
                await LoadCurrentSongAsync(autoPlay: false);
            }
        });
    }

    private void OnDurationChanged(object? sender, double duration)
    {
        // 媒体打开后由平台播放器推送准确总时长
        if (duration > 1 && Math.Abs(Duration - duration) > 0.5)
        {
            Duration = duration;
            TotalTimeDisplay = FormatTime(Duration);
        }
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (Duration < 1 && _audioService.Duration > 1)
        {
            Duration = _audioService.Duration;
            TotalTimeDisplay = FormatTime(Duration);
        }

        // 滑动列表时跳过非必要 UI 更新（Progress、CurrentTimeDisplay、歌词），
        // 减少 PropertyChanged 绑定开销，让主线程专注处理列表渲染。
        // 滑动停止后由 OnInteractionStateChanged 补一次 UpdateLyricPosition 同步歌词，
        // Progress/CurrentTimeDisplay 会在下一个 tick 自动恢复。
        bool isUserInteracting = _interactionState?.IsUserInteracting ?? false;

        // 进度条与时间显示必须实时更新，不能因列表滚动 / Tab 滑动等交互状态被冻结。
        // 早期实现用「交互中直接 return」会卡死 Progress：一旦交互 refCount 没归零，
        // Progress 永久停止推进，进度条彻底不走（只有切歌/切页等事件偶然解卡才恢复）。
        if (!_isSeeking)
        {
            Progress = position.TotalSeconds;
        }
        else if ((DateTime.UtcNow - _seekStartTime).TotalSeconds >= 10)
        {
            _isSeeking = false;
            Progress = position.TotalSeconds;
        }

        // 仅在整数秒变化时才格式化时间显示，避免每 tick 分配新字符串
        var currentSecond = (int)position.TotalSeconds;
        if (currentSecond != _lastDisplayedSecond)
        {
            _lastDisplayedSecond = currentSecond;
            CurrentTimeDisplay = FormatTime(currentSecond);

            // 每 30 秒定时 flush 聆听时长，防止应用被系统杀死时数据丢失
            if (_lastFlushSecond < 0 || (currentSecond - _lastFlushSecond) >= 30)
            {
                _lastFlushSecond = currentSecond;
                _ = FlushListeningAsync(isFinalFlush: false);
            }
        }

        // 仅在不交互（未滚动列表 / 未滑动 Tab）时更新歌词逐字定位，避免滑动时歌词抖动；
        // 滑动停止后会由 OnInteractionStateChanged 用播放器实时位置补一次同步。
        if (!isUserInteracting)
        {
            UpdateLyricPosition(position);
        }

        // 预缓冲：距歌曲结束 PreBufferSeconds 秒时，开始缓冲下一首 + 预取元数据
        if (Duration > 0 && (Duration - position.TotalSeconds) <= AudioCacheService.PreBufferSeconds
            && _preBufferedSongId != _loadedSongId)
        {
            _preBufferedSongId = _loadedSongId;
            var nextSong = _queue.PeekNextSong();
            if (nextSong != null)
            {
                _ = Task.Run(() => PreBufferNextSongAsync(nextSong));
            }
        }
    }

    private void OnPlaybackCompleted(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // 睡眠定时：若处于“播完当前歌曲后停止”等待阶段，则暂停且不自动切下一首
            if (_sleepTimer != null && _sleepTimer.IsWaitingForSongEnd)
            {
                await FlushListeningAsync(isFinalFlush: true);
                _sleepTimer.StopOnSongCompleted();
                return;
            }

            // 播放完成：先 flush 当前歌曲的聆听时长，再切下一首
            await FlushListeningAsync(isFinalFlush: true);
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

    /// <summary>从播放队列中点选一首歌播放</summary>
    private async Task PlaySongFromQueueAsync(Song? song)
    {
        if (song == null) return;
        _queue.SelectSong(song.Id);
        await LoadCurrentSongAsync();
    }

    /// <summary>从播放队列中移除一首歌。若移除的是当前歌曲则切换到下一首</summary>
    private async Task RemoveSongFromQueueAsync(Song? song)
    {
        if (song == null) return;
        var wasCurrent = _queue.CurrentSong?.Id == song.Id;
        _queue.RemoveSong(song.Id);
        if (wasCurrent)
            await LoadCurrentSongAsync();
    }

    /// <summary>获取当前播放队列歌曲列表（供播放列表弹窗使用）</summary>
    public IReadOnlyList<Song> GetQueueSongs() => _queue.GetSongs();

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
            PlayMode.ListRepeat => ("\U0001f501", "列表循环", ImageSourceHelper.FromNameThemed("ic_repeat_all")),
            PlayMode.SingleRepeat => ("\U0001f502", "单曲循环", ImageSourceHelper.FromNameThemed("ic_repeat_one")),
            PlayMode.Shuffle => ("\U0001f500", "随机播放", ImageSourceHelper.FromNameThemed("ic_shuffle")),
            PlayMode.Sequential => ("\u27a1", "顺序播放", ImageSourceHelper.FromNameThemed("ic_repeat_all")),
            _ => ("\U0001f501", "列表循环", ImageSourceHelper.FromNameThemed("ic_repeat_all"))
        };
        PlayModeIconSourceWhite = _queue.PlayMode switch
        {
            PlayMode.ListRepeat => ImageSourceHelper.FromNameOriginal("ic_repeat_all"),
            PlayMode.SingleRepeat => ImageSourceHelper.FromNameOriginal("ic_repeat_one"),
            PlayMode.Shuffle => ImageSourceHelper.FromNameOriginal("ic_shuffle"),
            PlayMode.Sequential => ImageSourceHelper.FromNameOriginal("ic_repeat_all"),
            _ => ImageSourceHelper.FromNameOriginal("ic_repeat_all")
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
        LikeIconSource = ImageSourceHelper.FromNameThemed(newFav ? "ic_favorite" : "ic_favorite_border");
        LikeIconSourceWhite = ImageSourceHelper.FromNameOriginal(newFav ? "ic_favorite_white" : "ic_favorite_border_white");

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
        var oldSongId = _loadedSongId;

        // 换歌前先 flush 上一首的聆听时长（后台执行，不阻塞切歌）
        if (oldSongId > 0 && song != null && song.Id != oldSongId)
        {
            _ = Task.Run(() => FlushListeningAsync(isFinalFlush: true));
        }

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
                    Log.Debug("AppViewModels", $"[NowPlaying] 恢复播放队列: {restoredSongs.Count} 首, 当前歌曲ID={restoredCurrentId}");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("AppViewModels", $"[NowPlaying] 恢复播放队列失败: {ex.Message}");
            }
        }

        if (song == null)
        {
            Title = "";
            Artist = "";
            Album = "";
            HasAlbum = false;
            CoverImage = ImageSource.FromFile(DefaultCoverService.GetDefaultCoverPath());
            HasCover = false;
            HasLyrics = false;
            ClearLyrics();
            _lastRecordedSongId = -1;
            _loadedSongId = -1;
            Duration = 0;
            Progress = 0;
            IsPlaying = false;
#if ANDROID
            // 队列已无歌曲可播放：停止播放并移除前台通知（自然播放结束分支）。
            // 正常切歌不走此分支，前台服务/MediaSession 会被复用（见 AudioPlayerService 的 STATE_ENDED 处理）。
            try
            {
                if (_audioService is Services.AudioPlayerService androidAudio)
                    androidAudio.StopAndHideNotification();
                else
                    await _audioService.StopAsync();
            }
            catch { }
#else
            try { await _audioService.StopAsync(); } catch { }
#endif
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
            // 新歌曲还没加载完成，_audioService.Duration 可能是旧值或 0，
            // 因此只使用数据库中的时长；若无效则等待 DurationChanged 事件修正。
            var songDurationSec = song.Duration > 1000 ? song.Duration / 1000.0 : 0;
            Duration = songDurationSec;
            TotalTimeDisplay = Duration > 0 ? FormatTime(Duration) : "--:--";
            Progress = 0;
            CurrentTimeDisplay = "0:00";
            _lastDisplayedSecond = 0;
            ClearLyrics();

            // 持久化当前歌曲 ID，下次启动可恢复
            Preferences.Default.Set("last_playing_song_id", song.Id);
        }

        // Check favorite (与播放并行执行，不阻塞切歌)
        var favoriteTask = Task.Run(async () =>
        {
            try { return await _db.IsFavoriteAsync(song.Id); }
            catch { return false; }
        }, ct);

        // 预加载封面（与播放并行，提前显示）
        var coverTask = Task.Run(async () =>
        {
            try { await LoadCoverAsync(song, ct); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Debug("AppViewModels", $"[CoverArt] 预加载封面失败: {ex.Message}"); }
        }, ct);

#if ANDROID || WINDOWS
        // 更新前台播放通知 / Windows SMTC 显示
#if ANDROID
        // 仅在实际切歌时重置进度缓存：确保通知栏/锁屏立即显示新歌的 0 进度而非上一首旧进度。
        // 同一首歌（如进入播放页/响应式重载）绝不能调用——它会把已 STATE_READY 的 _isPrepared
        // 清成 false 且无后续 READY 事件恢复，导致进度条永远卡在 0（歌单播放进度不走的元凶）。
        if (!isSameSong)
        {
            try { (_audioService as Services.AudioPlayerService)?.NotifySongSwitching(); }
            catch { }
            // 把数据库已知时长传给通知栏兜底：ExoPlayer Prepare 完成前 Duration=0，
            // 没有它通知栏进度条会在切歌间隙显示 00:00（Song.Duration 单位毫秒）
            try { (_audioService as Services.AudioPlayerService)?.UpdateKnownDuration(song.Duration > 1000 ? song.Duration / 1000.0 : 0); }
            catch { }
        }
#endif
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
                // 播放与收藏查询并行执行
                var playTask = _audioService.PlayAsync(song.FilePath);
                await Task.WhenAll(playTask, favoriteTask);
                IsLiked = favoriteTask.Result;
                if (_lastRecordedSongId != song.Id)
                {
                    _lastRecordedSongId = song.Id;
                    _ = RecordPlayAsync(song.Id);
                }
            }
            else
            {
                IsLiked = await favoriteTask;
            }

            // 更新收藏图标
            LikeIcon = IsLiked ? "\u2665" : "\u2661";
            LikeIconSource = ImageSourceHelper.FromNameThemed(IsLiked ? "ic_favorite" : "ic_favorite_border");
            LikeIconSourceWhite = ImageSourceHelper.FromNameOriginal(IsLiked ? "ic_favorite_white" : "ic_favorite_border_white");

            // 换歌时加载歌词（封面已在上方预加载），网络歌曲先缓存到本地再处理
            _ = Task.Run(async () =>
            {
                try
                {
                    // 网络歌曲：先缓存到本地，然后用本地文件方式处理
                    if (song.Source != SongSource.Local)
                    {
                        var localPath = await ResolveToLocalPathAsync(song, ct);
                        if (localPath != null)
                        {
                            await Task.WhenAll(
                                LoadMetadataFromLocalFileAsync(song, localPath),
                                LoadLyricsFromLocalFileAsync(song, localPath, ct)
                            );
                            // 封面已在预加载阶段处理，仅在网络缓存成功后补充加载
                            if (string.IsNullOrEmpty(song.CoverArtPath) || !File.Exists(song.CoverArtPath))
                                await LoadCoverFromLocalFileAsync(song, localPath, ct);
                            return;
                        }
                    }
                    // 本地歌曲：封面已预加载，仅加载歌词
                    await LoadLyricsAsync(song, ct);
                    // 如果封面预加载失败，补充加载
                    if (!HasCover)
                        await LoadCoverAsync(song, ct);
                    if (song.Source != SongSource.Local && _networkMusic != null)
                        await FetchAndUpdateSongMetadataAsync(song);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Log.Debug("AppViewModels", $"Load cover/lyrics error: {ex.Message}");
                }
            }, ct);
        }
        else if (!isSameSong && (!autoPlay || _isStartupRestore))
        {
            // 首次加载或启动恢复：加载封面、歌词和元数据，但不播放
            // 外部页面（音乐库、发现页）触发播放时 autoPlay=false，走此分支；
            // 启动恢复时不记录（避免记录上次未完成的播放），其他情况记录播放会话
            if (!_isStartupRestore && _lastRecordedSongId != song.Id)
            {
                _lastRecordedSongId = song.Id;
                _ = RecordPlayAsync(song.Id);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    if (song.Source != SongSource.Local)
                    {
                        var localPath = await ResolveToLocalPathAsync(song, ct);
                        if (localPath != null)
                        {
                            await Task.WhenAll(
                                LoadMetadataFromLocalFileAsync(song, localPath),
                                LoadCoverFromLocalFileAsync(song, localPath, ct),
                                LoadLyricsFromLocalFileAsync(song, localPath, ct)
                            );
                            return;
                        }
                    }
                    await Task.WhenAll(
                        LoadCoverAsync(song, ct),
                        LoadLyricsAsync(song, ct)
                    );
                    if (song.Source != SongSource.Local && _networkMusic != null)
                        await FetchAndUpdateSongMetadataAsync(song);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Log.Debug("AppViewModels", $"Load cover/lyrics error: {ex.Message}");
                }
            }, ct);
        }
        else
        {
            // 同一首歌回到播放页：恢复正确的播放/暂停状态图标
            PlayPauseIcon = _audioService.IsPlaying ? "\u23f8" : "\u25b6";
            PlayPauseIconSource = ImageSourceHelper.FromNameThemed(_audioService.IsPlaying ? "ic_pause" : "ic_play");
            PlayPauseIconSourceWhite = ImageSourceHelper.FromNameOriginal(_audioService.IsPlaying ? "ic_pause" : "ic_play");
        }

        // 重置启动恢复标志
        _isStartupRestore = false;

        // 保存队列状态（后台执行，不阻塞切歌）
        _ = Task.Run(SaveQueueState);
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
            Log.Debug("AppViewModels", $"[NowPlaying] 保存队列状态失败: {ex.Message}");
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
            Log.Debug("AppViewModels", $"[NowPlaying] 恢复队列状态失败: {ex.Message}");
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
        Log.Debug("AppViewModels", $"[CoverArt] 开始加载封面: {song.Title} (Id={song.Id}, Protocol={song.Protocol}, CoverArtPath={song.CoverArtPath?[..Math.Min(60, song.CoverArtPath?.Length ?? 0)] ?? "null"})");
        string? coverPath = null;

        // 缓存命中：已下采样到播放页尺寸（1000px）的封面文件，直接复用避免重复解码大图
        var npCached = Services.CoverHelper.GetCachedPath(song.Id, Services.CoverHelper.NowPlayingSize);
        if (File.Exists(npCached))
        {
            CurrentCoverPath = npCached;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                CoverImage = ImageSource.FromFile(npCached);
                HasCover = true;
            });
#if ANDROID || WINDOWS
            try { (_audioService as Services.AudioPlayerService)?.UpdateCoverPath(npCached); } catch { }
#endif
            return;
        }

        // 1. Check existing CoverArtPath
        if (!string.IsNullOrEmpty(song.CoverArtPath) && File.Exists(song.CoverArtPath))
        {
            coverPath = song.CoverArtPath;
        }

        // 1b. Navidrome/Subsonic: CoverArtPath 是 getCoverArt URL，下载并缓存
        if (coverPath == null
            && !string.IsNullOrEmpty(song.CoverArtPath)
            && (song.CoverArtPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || song.CoverArtPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            var cachedPath = Path.Combine(_coverCacheDir, $"cover_{song.Id}.jpg");
            if (!File.Exists(cachedPath))
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    using var httpClient = new HttpClient();
                    var bytes = await httpClient.GetByteArrayAsync(song.CoverArtPath, ct);
                    if (bytes != null && bytes.Length > 0)
                    {
                        Directory.CreateDirectory(_coverCacheDir);
                        await File.WriteAllBytesAsync(cachedPath, bytes, ct);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log.Debug("AppViewModels", $"[CoverArt] URL下载失败: {ex.Message}");
                }
            }
            if (File.Exists(cachedPath))
                coverPath = cachedPath;
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
            // 注意：WebDAV/SMB 协议的歌曲跳过此路径（MediaMetadataRetriever 无法处理带 user:pass@ 的 URL），改由步骤 6 处理
#if ANDROID
            string? extractUri = null;
            if (song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                extractUri = song.FilePath;
            }
            else if ((song.FilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || song.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                && song.Protocol != ProtocolType.WebDAV && song.Protocol != ProtocolType.SMB)
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
            else if (song.FilePath.StartsWith("smb://", StringComparison.OrdinalIgnoreCase)
                && song.Protocol != ProtocolType.SMB)
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

        // 5. Navidrome 旧数据兼容: CoverArtPath 是 coverArt ID（非URL），通过 INetworkMusicService 下载封面
        if (coverPath == null
            && song.Protocol == ProtocolType.Navidrome
            && !string.IsNullOrEmpty(song.CoverArtPath)
            && !song.CoverArtPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !song.CoverArtPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var networkSvc = MauiProgram.Services.GetService<INetworkMusicService>();
                if (networkSvc != null)
                {
                    var profiles = await networkSvc.GetProfilesAsync();
                    var profile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.Navidrome);
                    if (profile != null)
                    {
                        var stream = await networkSvc.GetCoverAsync(song.CoverArtPath, profile);
                        if (stream != null)
                        {
                            var cachedPath = Path.Combine(_coverCacheDir, $"cover_{song.Id}.jpg");
                            using (var fs = File.Create(cachedPath))
                                await stream.CopyToAsync(fs, ct);
                            stream.Dispose();
                            coverPath = cachedPath;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Debug("AppViewModels", $"[CoverArt] Navidrome旧数据封面获取失败: {ex.Message}");
            }
        }

        // 6. WebDAV/SMB: 通过 INetworkMusicService 下载文件头并提取内嵌封面
        // MediaMetadataRetriever.SetDataSource 无法处理带 user:pass@ 的 WebDAV URL，需走 NetworkMusicService
        if (coverPath == null
            && (song.Protocol == ProtocolType.WebDAV || song.Protocol == ProtocolType.SMB)
            && !string.IsNullOrEmpty(song.RemoteId))
        {
            Log.Debug("AppViewModels", $"[CoverArt] 步骤6: 尝试 {song.Protocol} 封面提取 (RemoteId={song.RemoteId?[..Math.Min(40, song.RemoteId?.Length ?? 0)]})");
            try
            {
                ct.ThrowIfCancellationRequested();
                var networkSvc = MauiProgram.Services.GetService<INetworkMusicService>();
                if (networkSvc != null)
                {
                    var profiles = await networkSvc.GetProfilesAsync();
                    var profile = profiles.FirstOrDefault(p => p.Protocol == song.Protocol && p.IsEnabled);
                    if (profile != null)
                    {
                        Log.Debug("AppViewModels", $"[CoverArt] 步骤6: 找到配置 {profile.Name}, 调用 GetCoverAsync...");
                        var stream = await networkSvc.GetCoverAsync(song.RemoteId, profile);
                        if (stream != null)
                        {
                            var cachedPath = Path.Combine(_coverCacheDir, $"cover_{song.Id}.jpg");
                            using (var fs = File.Create(cachedPath))
                                await stream.CopyToAsync(fs, ct);
                            stream.Dispose();
                            coverPath = cachedPath;
                            Log.Debug("AppViewModels", $"[CoverArt] 步骤6: 封面提取成功 -> {cachedPath}");
                        }
                        else
                        {
                            Log.Debug("AppViewModels", $"[CoverArt] 步骤6: GetCoverAsync 返回 null");
                        }
                    }
                    else
                    {
                        Log.Debug("AppViewModels", $"[CoverArt] 步骤6: 未找到 {song.Protocol} 配置");
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Debug("AppViewModels", $"[CoverArt] WebDAV/SMB 封面获取失败: {ex.Message}");
            }
        }

        ct.ThrowIfCancellationRequested();

        Log.Debug("AppViewModels", $"[CoverArt] 封面加载完成: {song.Title}, coverPath={(coverPath != null ? "找到" : "未找到")}");

        if (coverPath != null)
        {
            // 限制播放页封面最大边长 1000px：
            // - 网络封面（http/https）保持原 URL 直显（Android 解码期会按显示尺寸再降采样）；
            // - 本方法各分支刚提取出的原始文件：直接下采样到 1000 并清理临时文件；
            // - 已是 song.CoverArtPath 缩略图等情况：按播放页尺寸重新解析（源太小会自动从音频重新提取全分辨率）。
            string finalPath;
            if (coverPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || coverPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                finalPath = coverPath;
            }
            else if (coverPath != song.CoverArtPath && File.Exists(coverPath))
            {
                var bucketed = Services.CoverHelper.GetCachedPath(song.Id, Services.CoverHelper.NowPlayingSize);
                finalPath = Services.CoverHelper.DownsampleToCache(coverPath, bucketed, Services.CoverHelper.NowPlayingSize)
                    ? bucketed
                    : coverPath;
                if (finalPath == bucketed && coverPath != bucketed)
                {
                    try { File.Delete(coverPath); } catch { }
                }
            }
            else
            {
                var resolved = Services.CoverHelper.ResolveSingleCover(song, Services.CoverHelper.NowPlayingSize);
                finalPath = resolved ?? coverPath;
            }

            CurrentCoverPath = finalPath;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // 使用 FileImageSource 而非 StreamImageSource：
                // 1. 避免 StreamImageSource 内部取消机制导致 FrostedBackground 加载失败
                // 2. 让 CachingFileImageSourceService 命中内存缓存，减少重复解码
                CoverImage = ImageSource.FromFile(finalPath);
                HasCover = true;
            });

#if ANDROID || WINDOWS
            try { (_audioService as Services.AudioPlayerService)?.UpdateCoverPath(finalPath); }
            catch { }
#endif
        }
        else
        {
            CurrentCoverPath = null;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                CoverImage = ImageSource.FromFile(DefaultCoverService.GetDefaultCoverPath());
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
            Log.Debug("AppViewModels", $"[CoverArt] content:// 提取失败: {ex.Message}");
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
            Log.Debug("AppViewModels", $"[Lyrics] 开始加载歌词: {song.Title} (Id={song.Id}, Path={song.FilePath?.Substring(0, Math.Min(60, song.FilePath?.Length ?? 0))}...)");
            Log.Debug("AppViewModels", $"[Lyrics] LyricsPath={song.LyricsPath ?? "null"}");

            lyrics = await _lyrics.GetLyricsAsync(song);

            Log.Debug("AppViewModels", $"[Lyrics] 结果: {(lyrics != null ? $"{lyrics.Lines.Count} 行" : "null")}");
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[Lyrics] 加载异常: {ex.GetType().Name}: {ex.Message}");
        }

        ct.ThrowIfCancellationRequested();

        if (lyrics != null && lyrics.Lines.Count > 0)
        {
            _currentLyrics = lyrics;
            _currentLyricIndex = -1;
            BuildFilteredLines();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                HasLyrics = true;
                NoLyricsText = "";
                CurrentLyricIndexObservable = -1;
                OnPropertyChanged(nameof(AllLyricLines));
            });
            _desktopLyricManager?.SetLyrics(lyrics);
            Log.Debug("AppViewModels", $"[Lyrics] 歌词已加载，首行: {lyrics.Lines[0].Text}");
        }
        else
        {
            _currentLyrics = null;
            _currentLyricIndex = -1;
            _filteredLines = null;
            _originalToFilteredMap = null;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                HasLyrics = false;
                NoLyricsText = "暂无歌词";
                ClearLyrics();
                OnPropertyChanged(nameof(AllLyricLines));
            });
            _desktopLyricManager?.SetLyrics(null);
            Log.Debug("AppViewModels", "[Lyrics] 未找到歌词");
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

        // 智能删除空行开启时，若当前行为空行（被过滤），不更新 UI 显示索引，
        // 保持上一句歌词的高亮位置不变，避免歌词向下回滚。
        // 空行代表间奏/停顿，UI 应停留在上一句歌词等待下一句出现。
        if (_originalToFilteredMap != null
            && newIndex >= 0
            && newIndex < _originalToFilteredMap.Length
            && _originalToFilteredMap[newIndex] == -1)
        {
            return;
        }

        // UI 显示使用过滤后列表和索引；预览行也基于过滤后列表，跳过空行
        var displayLines = _filteredLines ?? _currentLyrics.Lines;
        var displayIndex = MapOriginalToFiltered(newIndex);

        CurrentLyricIndexObservable = displayIndex;

        LyricCurrent = GetLineText(displayLines, displayIndex);
        LyricLine0 = GetLineText(displayLines, displayIndex - 4);
        LyricLine1 = GetLineText(displayLines, displayIndex - 3);
        LyricLine2 = GetLineText(displayLines, displayIndex - 2);
        LyricLine3 = GetLineText(displayLines, displayIndex - 1);
        LyricLine4 = GetLineText(displayLines, displayIndex + 1);
        LyricLine5 = GetLineText(displayLines, displayIndex + 2);
        LyricLine6 = GetLineText(displayLines, displayIndex + 3);
        LyricLine7 = GetLineText(displayLines, displayIndex + 4);
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

        // 智能删除空行开启时，若当前行为空行（被过滤），跳过填充进度更新，
        // 保持上一句歌词的着色状态（1.0=完全填充），等待下一句非空行再继续着色。
        // 这样可避免空行期间把上一句的 FillProgress 重置为 0 导致重复着色。
        if (_originalToFilteredMap != null
            && lineIndex < _originalToFilteredMap.Length
            && _originalToFilteredMap[lineIndex] == -1)
        {
            CurrentLineFillProgress = 1.0;
            return;
        }

        var lineMode = Services.LyricsSettingsService.Instance.LyricsMode == Services.LyricsSettingsService.Mode.Line;
        CurrentLineFillProgress = LyricFillCalculator.ComputeFillProgress(
            _currentLyrics.Lines[lineIndex], lineIndex, _currentLyrics.Lines, position, lineMode);
    }

    private static string GetLineText(List<LrcLyricLine> lines, int index)
    {
        if (index < 0 || index >= lines.Count) return "";
        return lines[index].Text;
    }

    /// <summary>
    /// 根据设置构建过滤后的歌词列表（移除空行）。
    /// 同时建立原始索引→过滤后索引的映射，供 UI 显示使用。
    /// </summary>
    private void BuildFilteredLines()
    {
        if (_currentLyrics == null || _currentLyrics.Lines.Count == 0)
        {
            _filteredLines = null;
            _originalToFilteredMap = null;
            return;
        }

        var removeEmpty = Services.LyricsSettingsService.Instance.RemoveEmptyLines;
        if (!removeEmpty)
        {
            _filteredLines = null;
            _originalToFilteredMap = null;
            return;
        }

        var original = _currentLyrics.Lines;
        _filteredLines = new List<LrcLyricLine>(original.Count);
        _originalToFilteredMap = new int[original.Count];
        for (int i = 0; i < original.Count; i++)
        {
            var line = original[i];
            // 空行：文本为空或仅空白，且无翻译内容
            var isEmpty = string.IsNullOrWhiteSpace(line.Text)
                          && string.IsNullOrWhiteSpace(line.Translation);
            if (isEmpty)
            {
                _originalToFilteredMap[i] = -1;
            }
            else
            {
                _originalToFilteredMap[i] = _filteredLines.Count;
                _filteredLines.Add(line);
            }
        }
    }

    /// <summary>将原始歌词行索引映射为过滤后列表中的索引。
    /// 若该行是空行被过滤，回退到最近的前一个非空行（空行代表停顿，前一行仍为当前歌词）。</summary>
    private int MapOriginalToFiltered(int originalIndex)
    {
        if (_originalToFilteredMap == null || originalIndex < 0 || originalIndex >= _originalToFilteredMap.Length)
            return originalIndex;
        var mapped = _originalToFilteredMap[originalIndex];
        if (mapped >= 0)
            return mapped;
        // 当前行是空行（被过滤），向前查找最近的可显示行
        for (int i = originalIndex - 1; i >= 0; i--)
        {
            if (_originalToFilteredMap[i] >= 0)
                return _originalToFilteredMap[i];
        }
        // 前面没有可显示行，向后查找
        for (int i = originalIndex + 1; i < _originalToFilteredMap.Length; i++)
        {
            if (_originalToFilteredMap[i] >= 0)
                return _originalToFilteredMap[i];
        }
        return -1;
    }

    /// <summary>
    /// 重新构建过滤列表并刷新 UI（设置变更后调用）。
    /// 重新映射当前行索引并触发 AllLyricLines 属性变更通知。
    /// </summary>
    public void RefreshFilteredLines()
    {
        BuildFilteredLines();
        // 重新映射当前行索引
        if (_currentLyricIndex >= 0)
        {
            CurrentLyricIndexObservable = MapOriginalToFiltered(_currentLyricIndex);
        }
        OnPropertyChanged(nameof(AllLyricLines));
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

    private async Task RecordPlayAsync(int songId, long durationMs = 0)
    {
        try
        {
            await _db.EnsureInitializedAsync();
            await _db.RecordPlayAsync(songId, durationMs);
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[NowPlayingViewModel] 记录播放失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 将当前累积的聆听时长写入数据库。
    /// 调用时机：切歌前、播放完成时、应用挂起时、每 30 秒定时。
    /// </summary>
    /// <param name="isFinalFlush">是否最终 flush（切歌/完成时），为 true 时重置追踪状态</param>
    public async Task FlushListeningAsync(bool isFinalFlush = false)
    {
        int songId;
        long elapsedMs;

        lock (_listenRecordLock)
        {
            songId = _trackedSongId;
            if (songId <= 0) return;

            // 计算从本次播放开始到现在的时长，累加到 pending
            if (_listeningStartUtc != DateTime.MinValue)
            {
                var elapsed = (long)(DateTime.UtcNow - _listeningStartUtc).TotalMilliseconds;
                if (elapsed > 0)
                {
                    _pendingListenMs += elapsed;
                    _listeningStartUtc = DateTime.UtcNow; // 重置起点，避免重复累加
                }
            }

            elapsedMs = _pendingListenMs;
            if (elapsedMs <= 0) return;

            if (isFinalFlush)
            {
                _trackedSongId = -1;
                _listeningStartUtc = DateTime.MinValue;
                _pendingListenMs = 0;
            }
            else
            {
                // 定时 flush：保留 pending 计数避免丢失，仅清零用于下一轮累加
                // 实际写入后不清零，因为 RecordPlayAsync 是插入新记录而非更新
                // 所以这里不需要清零，pending 会持续累积
                _pendingListenMs = 0;
            }
        }

        if (elapsedMs > 0 && songId > 0)
        {
            await RecordPlayAsync(songId, elapsedMs);
        }
    }

    /// <summary>
    /// 外部调用入口：应用挂起时 flush 聆听时长。
    /// 供 App.xaml.cs 的 OnSleep 调用。
    /// </summary>
    public void OnAppSleep()
    {
        _ = FlushListeningAsync(isFinalFlush: false);
    }

    /// <summary>
    /// 外部调用入口：应用恢复时重启计时。
    /// 供 App.xaml.cs 的 OnResume 调用。
    /// </summary>
    public void OnAppResume()
    {
        lock (_listenRecordLock)
        {
            if (_trackedSongId > 0 && _audioService.IsPlaying)
            {
                _listeningStartUtc = DateTime.UtcNow;
            }
        }
    }

    // === Utilities ===

    private static string FormatTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    // === 预缓冲 + 播放时元数据获取 ===

    private readonly INetworkMusicService? _networkMusic;

    /// <summary>
    /// 预缓冲下一首歌曲：下载音频到本地缓存 + 预取元数据（艺术家、专辑、封面、歌词）。
    /// 在当前歌曲即将结束时由 OnPositionChanged 触发。
    /// </summary>
    private async Task PreBufferNextSongAsync(Song nextSong)
    {
        try
        {
            Log.Debug("AppViewModels", $"[PreBuffer] 开始预缓冲: {nextSong.Title}");

            // 1. 下载音频到本地缓存（仅网络歌曲）
            if (nextSong.Source != SongSource.Local && !AudioCacheService.Instance.IsCached(nextSong.FilePath))
            {
                await AudioCacheService.Instance.CacheAsync(
                    nextSong.FilePath,
                    async url =>
                    {
                        // 通过 URL 转换器获取可下载的 HTTP URL
                        var proxyUrl = AudioPlayerService.UrlTransformer?.Invoke(url);
                        if (string.IsNullOrEmpty(proxyUrl)) return null;
                        try
                        {
                            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                            return await http.GetByteArrayAsync(proxyUrl);
                        }
                        catch { return null; }
                    });
            }

            // 2. 预取元数据（如果缺少）
            if (nextSong.Duration <= 0 || nextSong.Artist == "未知艺术家" || string.IsNullOrWhiteSpace(nextSong.Artist))
            {
                await FetchAndUpdateSongMetadataAsync(nextSong);
            }

            Log.Debug("AppViewModels", $"[PreBuffer] 预缓冲完成: {nextSong.Title}");
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[PreBuffer] 预缓冲失败: {nextSong.Title}, {ex.Message}");
        }
    }

    /// <summary>
    /// 获取歌曲元数据并更新数据库和 UI。
    /// 在播放时由 LoadCurrentSongAsync 的后台任务调用（对网络歌曲补充元数据）。
    /// </summary>
    private async Task FetchAndUpdateSongMetadataAsync(Song song)
    {
        if (_networkMusic == null) return;
        if (song.Source == SongSource.Local) return;

        try
        {
            // 查找对应的连接配置
            var profiles = await _networkMusic.GetProfilesAsync();
            var profile = profiles.FirstOrDefault(p =>
                (p.Protocol == ProtocolType.SMB && song.Source == SongSource.SMB) ||
                (p.Protocol == ProtocolType.WebDAV && song.Source == SongSource.WebDAV));
            if (profile == null) return;

            var tagged = await _networkMusic.FetchSongMetadataAsync(song, profile);
            if (tagged == null) return;

            // 更新 song 对象
            bool changed = false;
            if (!string.IsNullOrWhiteSpace(tagged.Title) && tagged.Title != song.Title)
            { song.Title = tagged.Title; changed = true; }
            if (!string.IsNullOrWhiteSpace(tagged.Artist) && tagged.Artist != "未知艺术家" && tagged.Artist != song.Artist)
            { song.Artist = tagged.Artist; changed = true; }
            if (!string.IsNullOrWhiteSpace(tagged.Album) && tagged.Album != "未知专辑" && tagged.Album != song.Album)
            { song.Album = tagged.Album; changed = true; }
            if (tagged.Duration > 0 && song.Duration <= 0)
            { song.Duration = tagged.Duration; changed = true; }
            if (tagged.Year > 0) song.Year = tagged.Year;
            if (tagged.TrackNumber > 0) song.TrackNumber = tagged.TrackNumber;
            song.Genre = tagged.Genre;

            if (changed)
            {
                await _db.SaveSongAsync(song);

                // 如果正在播放这首歌，更新 UI
                if (_loadedSongId == song.Id)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        Title = song.Title ?? "未知歌曲";
                        Artist = song.Artist ?? "未知艺术家";
                        Album = song.Album ?? "未知专辑";
                        HasAlbum = !string.IsNullOrEmpty(song.Album) && song.Album != "未知专辑";
                        if (song.Duration > 1000)
                        {
                            Duration = song.Duration / 1000.0;
                            TotalTimeDisplay = FormatTime(Duration);
                        }
                    });
                }
            }

            // 自动分类：确保艺术家和专辑记录存在（后台）
            if (changed && !string.IsNullOrWhiteSpace(song.Artist))
            {
                var artistNames = MusicUtility.SplitArtistNames(song.Artist);
                if (artistNames.Count > 0)
                {
                    var artistId = await _db.EnsureArtistAsync(artistNames[0]);
                    if (artistId > 0) song.ArtistId = artistId;
                }
            }
            if (changed && !string.IsNullOrWhiteSpace(song.Album) && song.Album != "未知专辑" && song.ArtistId > 0)
            {
                var albumId = await _db.EnsureAlbumAsync(song.Album, song.ArtistId);
                if (albumId > 0) song.AlbumId = albumId;
            }
            if (changed) await _db.SaveSongAsync(song);
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[NowPlayingVM] 元数据获取失败: {song.Title}, {ex.Message}");
        }
    }

    // === 网络歌曲 → 本地缓存 → 本地文件处理 ===

    /// <summary>
    /// 将网络歌曲解析为本地文件路径（通过缓存）。
    /// 已缓存直接返回；未缓存则通过代理下载整个文件到本地。
    /// </summary>
    private async Task<string?> ResolveToLocalPathAsync(Song song, CancellationToken ct)
    {
        // 已缓存：直接返回
        var cached = AudioCacheService.Instance.GetCachedPath(song.FilePath);
        if (cached != null) return cached;

        // 获取可下载的 HTTP URL
        string? downloadUrl = AudioPlayerService.UrlTransformer?.Invoke(song.FilePath);
        if (string.IsNullOrEmpty(downloadUrl)) return null;

        Log.Debug("AppViewModels", $"[Resolve] 开始缓存: {song.Title}");
        try
        {
            var localPath = await AudioCacheService.Instance.CacheAsync(
                song.FilePath,
                async url =>
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                    return await http.GetByteArrayAsync(downloadUrl, ct);
                },
                ct);
            Log.Debug("AppViewModels", $"[Resolve] 缓存完成: {song.Title}, {(localPath != null ? "成功" : "失败")}");
            return localPath;
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[Resolve] 缓存失败: {song.Title}, {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 从本地缓存文件提取元数据（标题、艺术家、专辑、时长等），更新 song 并写回数据库。
    /// 和扫描本地音乐完全一样的流程。
    /// </summary>
    private async Task LoadMetadataFromLocalFileAsync(Song song, string localPath)
    {
        if (song.Source == SongSource.Local) return; // 本地歌曲不需要
        try
        {
            using var fs = File.OpenRead(localPath);
            var tagged = CatClawMusic.Core.Services.TagReader.ReadFromStream(fs, localPath, Path.GetFileName(localPath), new FileInfo(localPath).Length);
            if (tagged == null) return;

            bool changed = false;
            if (!string.IsNullOrWhiteSpace(tagged.Title) && tagged.Title != song.Title)
            { song.Title = tagged.Title; changed = true; }
            if (!string.IsNullOrWhiteSpace(tagged.Artist) && tagged.Artist != "未知艺术家" && tagged.Artist != song.Artist)
            { song.Artist = tagged.Artist; changed = true; }
            if (!string.IsNullOrWhiteSpace(tagged.Album) && tagged.Album != "未知专辑" && tagged.Album != song.Album)
            { song.Album = tagged.Album; changed = true; }
            if (tagged.Duration > 0 && song.Duration <= 0)
            { song.Duration = tagged.Duration; changed = true; }
            if (tagged.Bitrate > 0) song.Bitrate = tagged.Bitrate;
            if (tagged.Year > 0) song.Year = tagged.Year;
            if (tagged.TrackNumber > 0) song.TrackNumber = tagged.TrackNumber;
            song.Genre = tagged.Genre;

            if (changed)
            {
                await _db.SaveSongAsync(song);
                // 自动分类
                if (!string.IsNullOrWhiteSpace(song.Artist))
                {
                    var artistNames = MusicUtility.SplitArtistNames(song.Artist);
                    if (artistNames.Count > 0)
                    {
                        var artistId = await _db.EnsureArtistAsync(artistNames[0]);
                        if (artistId > 0) song.ArtistId = artistId;
                    }
                }
                if (!string.IsNullOrWhiteSpace(song.Album) && song.Album != "未知专辑" && song.ArtistId > 0)
                {
                    var albumId = await _db.EnsureAlbumAsync(song.Album, song.ArtistId);
                    if (albumId > 0) song.AlbumId = albumId;
                }
                await _db.SaveSongAsync(song);

                // 更新 UI
                if (_loadedSongId == song.Id)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        Title = song.Title ?? "未知歌曲";
                        Artist = song.Artist ?? "未知艺术家";
                        Album = song.Album ?? "未知专辑";
                        HasAlbum = !string.IsNullOrEmpty(song.Album) && song.Album != "未知专辑";
                        if (song.Duration > 1000)
                        {
                            Duration = song.Duration / 1000.0;
                            TotalTimeDisplay = FormatTime(Duration);
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[Resolve] 元数据提取失败: {song.Title}, {ex.Message}");
        }
    }

    /// <summary>
    /// 从本地缓存文件提取封面（和扫描本地音乐一样用 TagReader.ExtractCoverArtToFile）。
    /// </summary>
    private async Task LoadCoverFromLocalFileAsync(Song song, string localPath, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var coverPath = await Task.Run(() =>
                CatClawMusic.Core.Services.TagReader.ExtractCoverArtToFile(localPath, _coverCacheDir), ct);

            if (coverPath != null)
            {
                CurrentCoverPath = coverPath;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    CoverImage = ImageSource.FromFile(coverPath);
                    HasCover = true;
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[Resolve] 封面提取失败: {song.Title}, {ex.Message}");
        }
    }

    /// <summary>
    /// 从本地缓存文件加载歌词（先找同目录 .lrc 文件，再尝试内嵌歌词）。
    /// 和播放本地音乐完全一样的流程。
    /// </summary>
    private async Task LoadLyricsFromLocalFileAsync(Song song, string localPath, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            // 1. 先找同名 .lrc 文件
            var lrcPath = Path.ChangeExtension(localPath, ".lrc");
            string? lyricsText = null;
            if (File.Exists(lrcPath))
            {
                lyricsText = await File.ReadAllTextAsync(lrcPath, ct);
            }
            else
            {
                // 2. 尝试 .ttml
                var ttmlPath = Path.ChangeExtension(localPath, ".ttml");
                if (File.Exists(ttmlPath))
                    lyricsText = await File.ReadAllTextAsync(ttmlPath, ct);
            }

            // 3. 内嵌歌词
            if (string.IsNullOrWhiteSpace(lyricsText))
            {
                using var fs = File.OpenRead(localPath);
                lyricsText = CatClawMusic.Core.Services.TagReader.ReadEmbeddedLyricsFromStream(fs, Path.GetFileName(localPath));
            }

            if (!string.IsNullOrWhiteSpace(lyricsText))
            {
                var parsed = await Task.Run(() => _lyrics.TryParseLyrics(lyricsText), ct);
                if (parsed != null)
                {
                    _currentLyrics = parsed;
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        HasLyrics = true;
                        NoLyricsText = "";
                        OnPropertyChanged(nameof(AllLyricLines));
                    });
                }
            }
            else
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    HasLyrics = false;
                    NoLyricsText = "暂无歌词";
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[Resolve] 歌词加载失败: {song.Title}, {ex.Message}");
        }
    }
}
