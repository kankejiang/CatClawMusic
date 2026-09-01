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
    /// <summary>封面/预缓冲下载共享客户端（30s 超时），复用连接池避免 socket 泄漏</summary>
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    /// <summary>完整音频缓存下载共享客户端（2 分钟超时，大文件）</summary>
    private static readonly HttpClient CacheHttpClient = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>交互（切页/滚屏）动画完全结束并过宽限期后触发，通知歌词页重测行高并重新钉行，修复返回时歌词挤在一起。</summary>
    public event EventHandler? LyricResumeRequested;

    private readonly PlayQueue _queue;
    private readonly ILyricsService _lyrics;
    private readonly MusicDatabase _db;
    private readonly IPluginManager? _pluginManager;
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
    /// <summary>交互（切页/滚屏）完全结束的时刻（UTC）。结束时记录，
    /// 之后 LyricResumeGraceSeconds 宽限期内仍视为交互中，避免切页动画归位后立即恢复歌词更新再卡一帧。</summary>
    private DateTime _interactionEndedAtUtc = DateTime.MinValue;

    // === 听歌时长追踪 ===
    /// <summary>当前正在追踪时长的歌曲ID</summary>
    private int _trackedSongId = -1;
    /// <summary>当前聆听对应的 PlaySession 行 Id（-1 表示尚未建行），用于把累计时长写回同一行，避免每 30 秒新建一行</summary>
    private int _currentSessionId = -1;
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
    /// <summary>当前封面主导色（供流光背景按封面着色；无封面或取色失败时为透明）</summary>
    [ObservableProperty] private Color _coverTintColor = Colors.Transparent;

    // === Playback State ===
    /// <summary>是否正在播放</summary>
    [ObservableProperty] private bool _isPlaying;
    /// <summary>用户是否正在滑动列表（绑定到 FrostedBackground.IsScrolling，滑动时暂停背景动画）</summary>
    [ObservableProperty] private bool _isUserScrolling;
    /// <summary>当前播放进度（秒）</summary>
    [ObservableProperty] private double _progress;
    /// <summary>歌曲总时长（秒）</summary>
    [ObservableProperty] private double _duration;

    /// <summary>迷你播放器进度条比例（0.0-1.0，供 ProgressBar 绑定）</summary>
    public double MiniPlayerProgress => _duration > 0 ? Math.Clamp(_progress / _duration, 0.0, 1.0) : 0;

    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(MiniPlayerProgress));
    partial void OnDurationChanged(double value) => OnPropertyChanged(nameof(MiniPlayerProgress));
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
    [ObservableProperty] private ImageSource? _playModeIconSource = ImageSourceHelper.FromNamePlayerCtrl("ic_repeat_all", "ic_repeat_all");
    /// <summary>播放模式白色图标 ImageSource（恒白，专给播放页等已有深色遮罩的场景）</summary>
    [ObservableProperty] private ImageSource? _playModeIconSourceWhite = ImageSourceHelper.FromNameOriginal("ic_repeat_all");

    // === Play/Pause ===
    /// <summary>播放/暂停按钮图标字符（▶ 或 ⏸）</summary>
    [ObservableProperty] private string _playPauseIcon = "\u25b6"; // ▶
    /// <summary>播放/暂停按钮图标 ImageSource（由资源名转换）</summary>
    [ObservableProperty] private ImageSource? _playPauseIconSource = ImageSourceHelper.FromNamePlayerCtrl("ic_notif_play", "ic_notif_play");
    /// <summary>播放/暂停按钮白色图标 ImageSource（恒白，专给播放页等已有深色遮罩的场景）</summary>
    [ObservableProperty] private ImageSource? _playPauseIconSourceWhite = ImageSourceHelper.FromNameOriginal("ic_notif_play");

    // === Like ===
    /// <summary>当前歌曲是否已收藏</summary>
    [ObservableProperty] private bool _isLiked;
    /// <summary>收藏按钮图标字符（♡ 或 ♥）</summary>
    [ObservableProperty] private string _likeIcon = "\u2661"; // ♡
    /// <summary>收藏按钮图标 ImageSource（由资源名转换）</summary>
    [ObservableProperty] private ImageSource? _likeIconSource = ImageSourceHelper.FromNamePlayerCtrl("ic_notif_favorite_border", "ic_notif_favorite_border");
    /// <summary>收藏按钮白色图标 ImageSource（恒白，专给播放页等已有深色遮罩的场景）</summary>
    [ObservableProperty] private ImageSource? _likeIconSourceWhite = ImageSourceHelper.FromNameOriginal("ic_notif_favorite_border");

    // === FM Mode (私人漫游模式切换) ===
    /// <summary>当前是否处于 FM 电台模式（绑定 PlayQueue.IsFmMode，播放页据此显示/隐藏模式按钮）</summary>
    [ObservableProperty] private bool _isFmMode;
    /// <summary>当前 FM 推荐模式显示名（如"默认模式"/"熟悉模式"/"探索模式"）；非 FM 模式时为空</summary>
    [ObservableProperty] private string _fmModeLabel = "";

    // === Previous / Next ===
    /// <summary>上一首按钮图标 ImageSource</summary>
    [ObservableProperty] private ImageSource? _playPreviousIconSource = ImageSourceHelper.FromNamePlayerCtrl("ic_notif_previous", "ic_notif_previous");
    /// <summary>上一首按钮白色图标 ImageSource（恒白，专给播放页等已有深色遮罩的场景）</summary>
    [ObservableProperty] private ImageSource? _playPreviousIconSourceWhite = ImageSourceHelper.FromNameOriginal("ic_notif_previous");
    /// <summary>下一首按钮图标 ImageSource</summary>
    [ObservableProperty] private ImageSource? _playNextIconSource = ImageSourceHelper.FromNamePlayerCtrl("ic_notif_next", "ic_notif_next");
    /// <summary>下一首按钮白色图标 ImageSource（恒白，专给播放页等已有深色遮罩的场景）</summary>
    [ObservableProperty] private ImageSource? _playNextIconSourceWhite = ImageSourceHelper.FromNameOriginal("ic_notif_next");
    /// <summary>播放列表按钮图标 ImageSource</summary>
    [ObservableProperty] private ImageSource? _playlistIconSource = ImageSourceHelper.FromNameOriginal("ic_playlist");
    /// <summary>歌词按钮图标 ImageSource（与通知栏媒体控件一致，使用 ic_notif_lyric_on）</summary>
    [ObservableProperty] private ImageSource? _lyricsIconSource = ImageSourceHelper.FromNameOriginal("ic_notif_lyric_on");

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
        Services.SleepTimerService? sleepTimer = null,
        IPluginManager? pluginManager = null)
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
        _pluginManager = pluginManager;

        // Initialize cover cache directory
        _coverCacheDir = Path.Combine(FileSystem.CacheDirectory, "covers");
        Directory.CreateDirectory(_coverCacheDir);

        // Subscribe to audio events
        _audioService.PlaybackStateChanged += OnPlaybackStateChanged;
        _audioService.PositionChanged += OnPositionChanged;
        _audioService.DurationChanged += OnDurationChanged;
        _audioService.PlaybackCompleted += OnPlaybackCompleted;
        _queue.IsFmModeChanged += OnIsFmModeChanged;

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
        CycleFmModeCommand = new AsyncRelayCommand(CycleFmModeAsync);
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

        if (!isInteracting)
        {
            // 记录交互完全结束时刻：此后 LyricResumeGraceSeconds 内歌词仍不更新，
            // 让切页动画归位（Settling→Idle）后的卡顿帧平稳结束，再恢复逐字/Fill 着色。
            _interactionEndedAtUtc = DateTime.UtcNow;
        }

        if (!isInteracting && _audioService.IsPlaying && !_isSeeking)
        {
            // 延迟 0.2s 后再用播放器实时位置补一次歌词定位，与宽限期对齐：
            // 避免切页动画一归位就立刻重算歌词，此时帧尚未稳定、补算也会卡。
            var pos = TimeSpan.FromSeconds(_audioService.CurrentPosition);
            _ = DelayLyricRelocateAsync(pos);
        }
    }

    /// <summary>延迟 0.2s 后补一次歌词定位（在 UI 线程执行），对齐歌词恢复宽限期。</summary>
    private async Task DelayLyricRelocateAsync(TimeSpan position)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(LyricResumeGraceSeconds));
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_isSeeking) return;
                UpdateLyricPosition(position);
                // 宽限期结束：通知歌词页重新测量行高并钉行，修复切回页面时歌词挤在一起。
                // 此时页面几何已稳定、行高就绪，重测得到的锚点才是准确的。
                LyricResumeRequested?.Invoke(this, EventArgs.Empty);
            });
        }
        catch { }
    }

    /// <summary>交互结束后的歌词恢复宽限期（秒）。结束后才恢复逐字/Fill 着色。</summary>
    private static readonly double LyricResumeGraceSeconds = 0.2;

    /// <summary>是否处于"交互结束后宽限期"：切页/滚屏动画刚归位，歌词暂不更新。</summary>
    private bool InLyricResumeGrace()
        => (DateTime.UtcNow - _interactionEndedAtUtc).TotalSeconds < LyricResumeGraceSeconds;

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
        MainThread.BeginInvokeOnMainThread(RefreshPlayerCtrlIcons);
    }

    /// <summary>
    /// 按当前深浅模式与主题色刷新播放控制条图标：深色=白色原版，浅色=主题色预生成变体
    /// （*_{hex}_active，构建期由 MauiImage 转 PNG，不依赖平台 TintColor）。
    /// 深浅切换经 RequestedThemeChanged 触发；主题色切换由页面 OnAppearing 时调用。
    /// </summary>
    public void RefreshPlayerCtrlIcons()
    {
        try
        {
            var isPlaying = _audioService.IsPlaying;
            PlayPauseIconSource = ImageSourceHelper.FromNamePlayerCtrl(isPlaying ? "ic_notif_pause" : "ic_notif_play", isPlaying ? "ic_notif_pause" : "ic_notif_play");
            PlayPauseIconSourceWhite = ImageSourceHelper.FromNameOriginal(isPlaying ? "ic_notif_pause" : "ic_notif_play");
            PlayPreviousIconSource = ImageSourceHelper.FromNamePlayerCtrl("ic_notif_previous", "ic_notif_previous");
            PlayPreviousIconSourceWhite = ImageSourceHelper.FromNameOriginal("ic_notif_previous");
            PlayNextIconSource = ImageSourceHelper.FromNamePlayerCtrl("ic_notif_next", "ic_notif_next");
            PlayNextIconSourceWhite = ImageSourceHelper.FromNameOriginal("ic_notif_next");
            RefreshPlayModeDisplay();
            LikeIconSource = ImageSourceHelper.FromNamePlayerCtrl(IsLiked ? "ic_notif_favorite" : "ic_notif_favorite_border", IsLiked ? "ic_notif_favorite" : "ic_notif_favorite_border");
            LikeIconSourceWhite = ImageSourceHelper.FromNameOriginal(IsLiked ? "ic_notif_favorite" : "ic_notif_favorite_border");
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[NowPlayingVM] RefreshPlayerCtrlIcons failed: {ex.Message}");
        }
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
                LikeIconSource = ImageSourceHelper.FromNamePlayerCtrl(isFavorite ? "ic_notif_favorite" : "ic_notif_favorite_border", isFavorite ? "ic_notif_favorite" : "ic_notif_favorite_border");
                LikeIconSourceWhite = ImageSourceHelper.FromNameOriginal(isFavorite ? "ic_notif_favorite" : "ic_notif_favorite_border");
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
    /// <summary>循环切换私人漫游推荐模式命令（默认→熟悉→探索→默认）</summary>
    public IRelayCommand CycleFmModeCommand { get; }
    /// <summary>进度跳转命令，参数为目标位置（秒）</summary>
    public RelayCommand<double> SeekCommand { get; }
    /// <summary>从播放队列中选择一首歌播放</summary>
    public IAsyncRelayCommand<Song> PlaySongFromQueueCommand { get; }
    /// <summary>从播放队列中移除一首歌</summary>
    public IAsyncRelayCommand<Song> RemoveSongFromQueueCommand { get; }

    private static string FormatTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    // === 预缓冲 + 播放时元数据获取 ===

    private readonly INetworkMusicService? _networkMusic;
}
