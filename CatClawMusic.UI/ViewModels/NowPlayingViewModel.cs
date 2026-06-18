using System.Collections.ObjectModel;
using Android.Text;
using Android.Text.Style;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.UI.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.UI.ViewModels;

/// <summary>
/// 正在播放ViewModel，管理播放状态、歌词、封面、播放队列和收藏状态
/// </summary>
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
    [ObservableProperty] private LrcLyrics? _currentLyrics;
    [ObservableProperty] private int _currentLyricIndex = -1;
    public int LyricsMode { get; set; } = 0;
    public int LyricStyle { get; set; } = 0;
    public ISpannable? CurrentLyricSpannable { get; private set; }
    /// <summary>
    /// 当前歌词行的演唱进度（0~1），用于 StrokeTextView 的渐变裁剪
    /// <para>值由 UpdateLyricSpannable 根据播放位置和行持续时间计算</para>
    /// <para>设置时带阈值过滤，避免 100ms 定时器产生过于频繁的 PropertyChanged 通知</para>
    /// </summary>
    private float _currentLyricProgress;
    public float CurrentLyricProgress
    {
        get => _currentLyricProgress;
        set
        {
            // 低阈值配合 33ms 定时器，保证 30fps 平滑着色
            if (Math.Abs(_currentLyricProgress - value) < 0.002f) return;
            _currentLyricProgress = value;
            OnPropertyChanged(nameof(CurrentLyricProgress));
        }
    }
    private bool _isPositionUpdating;
    private int _saveCounter;
    private ConnectionProfile? _cachedNavidromeProfile;
    private ConnectionProfile? _cachedWebDavProfile;
    private ConnectionProfile? _cachedSmbProfile;
    private readonly HashSet<int> _metadataFetchAttempted = new();
    private CancellationTokenSource? _errorDialogCts;
    private CancellationTokenSource? _songLoadCts;
    private Song? _lastActiveSong;
    private volatile bool _isSwitchingSong;
    private volatile bool _isRestoring;
    private int _lastSpannableLineIdx = -1;
    private int _lastLyricIndex = -999;
    private int _positionUpdateQueued;
    private TimeSpan _pendingPosition;
    /// <summary>
    /// 是否在当前歌曲播放完毕后停止
    /// </summary>
    public volatile bool StopAfterCurrentSong;

    /// <summary>
    /// 当前播放的歌曲
    /// </summary>
    [ObservableProperty] private Song? _currentSong;
    /// <summary>
    /// 封面图片路径
    /// </summary>
    [ObservableProperty] private string _coverSource = "";
    [ObservableProperty] private string _prevLyricLine8 = "";
    [ObservableProperty] private string _prevLyricLine7 = "";
    [ObservableProperty] private string _prevLyricLine6 = "";
    [ObservableProperty] private string _prevLyricLine5 = "";
    [ObservableProperty] private string _prevLyricLine4 = "";
    [ObservableProperty] private string _prevLyricLine3 = "";
    [ObservableProperty] private string _prevLyricLine2 = "";
    [ObservableProperty] private string _prevLyricLine = "";
    /// <summary>
    /// 当前歌词行文本
    /// </summary>
    [ObservableProperty] private string _currentLyricLine = "🐾 猫爪音乐";
    /// <summary>
    /// 下一行歌词文本
    /// </summary>
    [ObservableProperty] private string _nextLyricLine = "选择一首歌曲开始播放吧~";
    [ObservableProperty] private string _nextLyricLine2 = "";
    [ObservableProperty] private string _nextLyricLine3 = "";
    [ObservableProperty] private string _nextLyricLine4 = "";
    [ObservableProperty] private string _nextLyricLine5 = "";
    [ObservableProperty] private string _nextLyricLine6 = "";
    [ObservableProperty] private string _nextLyricLine7 = "";
    [ObservableProperty] private string _nextLyricLine8 = "";

    // ── 逐行对齐属性（TTML/AMLL 对唱歌词支持）──
    // 0=左对齐, 1=居中, 2=右对齐
    [ObservableProperty] private int _prevLyricAlignment2 = 1;
    [ObservableProperty] private int _prevLyricAlignment = 1;
    [ObservableProperty] private int _currentLyricAlignment = 1;
    [ObservableProperty] private int _nextLyricAlignment = 1;
    [ObservableProperty] private int _nextLyricAlignment2 = 1;
    /// <summary>当前歌词是否支持逐行对齐</summary>
    [ObservableProperty] private bool _hasPerLineAlignment;

    // ── 合唱/对唱同时着色支持 ──
    /// <summary>当前合唱伙伴行索引（与当前行时间重叠的另一歌手行），-1 表示无伙伴</summary>
    [ObservableProperty] private int _duetPartnerIndex = -1;
    /// <summary>
    /// 合唱伙伴行的演唱进度（0~1）
    /// <para>设置时带阈值过滤，避免 100ms 定时器产生过于频繁的 PropertyChanged 通知</para>
    /// </summary>
    private float _duetPartnerProgress;
    public float DuetPartnerProgress
    {
        get => _duetPartnerProgress;
        set
        {
            // 低阈值配合 33ms 定时器，保证 30fps 平滑着色
            if (Math.Abs(_duetPartnerProgress - value) < 0.002f) return;
            _duetPartnerProgress = value;
            OnPropertyChanged(nameof(DuetPartnerProgress));
        }
    }
    /// <summary>是否处于合唱状态（当前行有合唱伙伴）</summary>
    public bool HasDuetPartner => DuetPartnerIndex >= 0;
    /// <summary>
    /// 当前播放位置
    /// </summary>
    [ObservableProperty] private TimeSpan _currentPosition;
    /// <summary>
    /// 歌曲总时长
    /// </summary>
    [ObservableProperty] private TimeSpan _totalDuration;
    /// <summary>
    /// 播放/暂停按钮图标
    /// </summary>
    [ObservableProperty] private string _playPauseIcon = "▶";
    /// <summary>
    /// 播放模式图标
    /// </summary>
    [ObservableProperty] private string _playModeIcon = ""; // 构造函数中同步
    /// <summary>
    /// 收藏按钮图标
    /// </summary>
    [ObservableProperty] private string _likeIcon = "🤍";
    /// <summary>
    /// 是否已收藏
    /// </summary>
    [ObservableProperty] private bool _isLiked;
    /// <summary>
    /// 当前音量（0-100）
    /// </summary>
    [ObservableProperty] private int _volume = 80;
    /// <summary>
    /// 播放队列提示文本
    /// </summary>
    [ObservableProperty] private string _queueHint = "";
    /// <summary>
    /// 即将播放的歌曲列表
    /// </summary>
    public ObservableCollection<Song> UpcomingSongs { get; } = new();

    /// <summary>
    /// 更新当前歌词行的 Spannable 富文本和渐变进度
    /// <para>根据当前播放位置计算当前行的演唱进度（0~1），并构建包含翻译的 SpannableString</para>
    /// <para>翻译文本以灰色+缩小字号显示在原文下方</para>
    /// <para>同一行内只更新进度值，不重复创建 SpannableString</para>
    /// <para>当有逐字时间戳时，使用逐字进度替代行级线性插值</para>
    /// </summary>
    public void UpdateLyricSpannable()
    {
        var lines = CurrentLyrics?.Lines;
        var idx = CurrentLyricIndex;
        if (lines == null || idx < 0 || idx >= lines.Count) { ClearSpannable(); _lastSpannableLineIdx = -1; _lastLyricIndex = -999; return; }

        var line = lines[idx];
        var text = line.Text;
        if (string.IsNullOrEmpty(text)) { ClearSpannable(); _lastSpannableLineIdx = -1; return; }

        // 计算进度：优先使用逐字时间戳，回退到行级线性插值
        if (line.WordTimestamps != null && line.WordTimestamps.Count > 0)
        {
            CurrentLyricProgress = (float)CalculateWordProgress(line.WordTimestamps, CurrentPosition);
        }
        else
        {
            var lineDuration = GetLineDuration(lines, idx);
            if (lineDuration.TotalMilliseconds > 0)
                CurrentLyricProgress = (float)Math.Clamp((CurrentPosition - line.Timestamp).TotalMilliseconds / lineDuration.TotalMilliseconds, 0.0, 1.0);
            else
                CurrentLyricProgress = 1f;
        }

        if (idx == _lastSpannableLineIdx)
            return;

        _lastSpannableLineIdx = idx;

        var hasTranslation = !string.IsNullOrEmpty(line.Translation);
        var spanText = hasTranslation ? text + "\n" + line.Translation! : text;
        var ss = new SpannableString(spanText);

        if (hasTranslation)
        {
            var transStart = text.Length + 1;
            var transGray = Android.Graphics.Color.Argb(0x99, 0x99, 0x99, 0x99);
            ss.SetSpan(new ForegroundColorSpan(transGray), transStart, spanText.Length, SpanTypes.ExclusiveExclusive);
            ss.SetSpan(new Android.Text.Style.RelativeSizeSpan(0.82f), transStart, spanText.Length, SpanTypes.ExclusiveExclusive);
        }

        CurrentLyricSpannable = ss;
        OnPropertyChanged(nameof(CurrentLyricSpannable));
    }

    /// <summary>
    /// 基于逐字时间戳计算当前行的演唱进度（0~1）
    /// <para>根据已唱完的字宽度和当前字的进度，精确计算整体进度</para>
    /// </summary>
    private static double CalculateWordProgress(List<WordTimestamp> words, TimeSpan position)
    {
        if (words.Count == 0) return 0;

        // 计算每个字的字符宽度占比（近似：按字符数分配）
        var totalChars = words.Sum(w => w.Word.Length);
        if (totalChars == 0) return 0;

        double sungChars = 0;
        for (int i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var wordEnd = word.Start + word.Duration;

            if (position >= wordEnd)
            {
                // 这个字已唱完
                sungChars += word.Word.Length;
            }
            else if (position >= word.Start)
            {
                // 这个字正在唱（position == word.Start 时即开始着色，避免慢半拍）
                var wordProgress = word.Duration.TotalMilliseconds > 0
                    ? (position - word.Start).TotalMilliseconds / word.Duration.TotalMilliseconds
                    : 1.0;
                sungChars += word.Word.Length * Math.Clamp(wordProgress, 0, 1);
                break;
            }
            else
            {
                // 还没唱到
                break;
            }
        }

        return Math.Clamp(sungChars / totalChars, 0, 1);
    }

    private void ClearSpannable()
    {
        if (CurrentLyricSpannable != null)
        {
            CurrentLyricSpannable = null;
            OnPropertyChanged(nameof(CurrentLyricSpannable));
        }
    }

    private static string FormatLyricLine(List<LrcLyricLine> lines, int idx)
    {
        if (idx < 0 || idx >= lines.Count) return "";
        var line = lines[idx];
        if (string.IsNullOrEmpty(line.Translation)) return line.Text;
        return line.Text + "\n" + line.Translation;
    }

    /// <summary>获取指定行的对齐方式（0=左,1=中,2=右），越界返回默认居中</summary>
    private static int GetLineAlignment(List<LrcLyricLine> lines, int idx)
    {
        if (idx < 0 || idx >= lines.Count) return 1;
        return lines[idx].Alignment;
    }

    /// <summary>
    /// 获取当前歌词行的持续时间（下一行时间 - 本行时间，最后一行默认 5 秒）
    /// </summary>
    private static TimeSpan GetLineDuration(List<LrcLyricLine> lines, int idx)
    {
        if (idx + 1 < lines.Count)
        {
            var gap = lines[idx + 1].Timestamp - lines[idx].Timestamp;
            if (gap.TotalSeconds > 0 && gap.TotalSeconds < 30)
                return gap;
        }
        return TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// 计算指定行的演唱进度（0~1），用于合唱时同时着色伙伴行。
    /// 优先使用逐字时间戳，回退到行级线性插值。
    /// </summary>
    private static float CalculateLineProgress(List<LrcLyricLine> lines, int idx, TimeSpan position)
    {
        if (idx < 0 || idx >= lines.Count) return 0f;
        var line = lines[idx];
        if (line.WordTimestamps != null && line.WordTimestamps.Count > 0)
            return (float)CalculateWordProgress(line.WordTimestamps, position);
        var lineDuration = GetLineDuration(lines, idx);
        if (lineDuration.TotalMilliseconds > 0)
            return (float)Math.Clamp((position - line.Timestamp).TotalMilliseconds / lineDuration.TotalMilliseconds, 0.0, 1.0);
        return 1f;
    }

    /// <summary>
    /// 查找当前行的合唱伙伴行索引。
    /// 合唱伙伴 = 与当前行时间区间重叠且对齐方式不同的另一行（不同歌手）。
    /// 若无真正重叠，则查找相邻的不同歌手行（对唱模式）。
    /// </summary>
    private static int FindDuetPartner(List<LrcLyricLine> lines, int currentIdx, TimeSpan position)
    {
        if (currentIdx < 0 || currentIdx >= lines.Count) return -1;
        var currentAlignment = lines[currentIdx].Alignment;
        if (currentAlignment == 1) return -1; // 居中行不参与对唱

        var currentStart = lines[currentIdx].Timestamp;
        var currentEnd = currentIdx + 1 < lines.Count
            ? lines[currentIdx + 1].Timestamp
            : currentStart + TimeSpan.FromSeconds(5);

        // 1. 查找时间区间真正重叠的不同歌手行（合唱）
        for (int i = 0; i < lines.Count; i++)
        {
            if (i == currentIdx) continue;
            if (lines[i].Alignment == currentAlignment) continue; // 同一歌手
            if (lines[i].Alignment == 1) continue; // 居中行不参与

            var otherStart = lines[i].Timestamp;
            var otherEnd = i + 1 < lines.Count
                ? lines[i + 1].Timestamp
                : otherStart + TimeSpan.FromSeconds(5);

            // 时间区间重叠 && 当前位置在两行的活跃区间内
            if (otherStart < currentEnd && currentStart < otherEnd
                && position >= otherStart && position < otherEnd)
                return i;
        }

        // 2. 对唱模式：查找相邻的不同歌手行（前一行或后一行），且时间接近
        // 前一行
        if (currentIdx > 0)
        {
            var prev = lines[currentIdx - 1];
            if (prev.Alignment != currentAlignment && prev.Alignment != 1)
            {
                var gap = currentStart - prev.Timestamp;
                if (gap.TotalSeconds >= 0 && gap.TotalSeconds < 3)
                    return currentIdx - 1;
            }
        }
        // 后一行
        if (currentIdx + 1 < lines.Count)
        {
            var next = lines[currentIdx + 1];
            if (next.Alignment != currentAlignment && next.Alignment != 1)
            {
                var gap = next.Timestamp - currentStart;
                if (gap.TotalSeconds >= 0 && gap.TotalSeconds < 3)
                    return currentIdx + 1;
            }
        }

        return -1;
    }

    /// <summary>
    /// 当前播放位置（秒），设置时会触发Seek操作
    /// </summary>
    public double CurrentPositionSeconds
    {
        get => CurrentPosition.TotalSeconds;
        set { if (!_isPositionUpdating) _ = _audioPlayer.SeekAsync(TimeSpan.FromSeconds(value)); }
    }
    /// <summary>
    /// 总时长（秒）
    /// </summary>
    public double TotalDurationSeconds => TotalDuration.TotalSeconds;

    /// <summary>
    /// 当前歌曲变化时加载歌词、封面、检查收藏状态并解析歌曲详情
    /// </summary>
    partial void OnCurrentSongChanged(Song? value)
    {
        _songLoadCts?.Cancel();
        _songLoadCts = new CancellationTokenSource();
        var ct = _songLoadCts.Token;
        _ = LoadLyricsAsync(value, ct);
        _ = LoadCoverAsync(value, ct);
        UpdateQueuePeek();
        _ = CheckFavoriteAsync();
        _ = ResolveSongDetails(value);
    }
    /// <summary>
    /// 收藏状态变化时更新图标
    /// </summary>
    partial void OnIsLikedChanged(bool value) { LikeIcon = value ? "❤️" : "🤍"; }

    /// <summary>
    /// 初始化NowPlayingViewModel，绑定播放器状态和位置变化事件
    /// </summary>
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
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
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
        _isSwitchingSong = true;
        CurrentSong = song;
        UpdateQueuePeek();
    }

    /// <summary>标记开始恢复播放状态，抑制错误对话框和自动切歌</summary>
    public void BeginRestore() => _isRestoring = true;

    /// <summary>标记恢复结束</summary>
    public void EndRestore() => _isRestoring = false;

    /// <summary>
    /// 播放/暂停切换
    /// </summary>
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

    /// <summary>
    /// 播放下一首
    /// </summary>
    [RelayCommand]
    private void Next() { _isSwitchingSong = true; var s = _playQueue.Next(); if (s != null) { CurrentSong = s; _ = _audioPlayer.PlayAsync(s.FilePath); _ = RecordPlayAsync(); } }

    /// <summary>
    /// 播放上一首
    /// </summary>
    [RelayCommand]
    private void Previous() { _isSwitchingSong = true; var s = _playQueue.Previous(); if (s != null) { CurrentSong = s; _ = _audioPlayer.PlayAsync(s.FilePath); _ = RecordPlayAsync(); } }

    /// <summary>
    /// 循环切换播放模式：列表循环 → 随机播放 → 单曲循环
    /// </summary>
    [RelayCommand]
    private void CyclePlayMode()
    {
        // 循环切换：🔁列表循环 → 🔀随机播放 → 🔂单曲循环
        _playQueue.PlayMode = _playQueue.PlayMode switch
        {
            PlayMode.ListRepeat => PlayMode.Shuffle,
            PlayMode.Shuffle => PlayMode.SingleRepeat,
            PlayMode.SingleRepeat => PlayMode.ListRepeat,
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
        // 记住播放模式
        CatClawMusic.UI.Services.PlaybackStateManager.SavePlayMode(_playQueue.PlayMode);
    }

    /// <summary>
    /// 切换收藏状态
    /// </summary>
    [RelayCommand]
    private void ToggleLike() { IsLiked = !IsLiked; _ = SaveFavoriteAsync(); }

    /// <summary>
    /// 处理滑动手势，左滑切下一首、右滑切上一首
    /// </summary>
    [RelayCommand]
    private void Swipe(string direction)
    {
        if (direction == "Left") OnNext();
        else if (direction == "Right") OnPrevious();
    }

    private void OnNext() { _isSwitchingSong = true; var s = _playQueue.Next(); if (s != null) { CurrentSong = s; _ = _audioPlayer.PlayAsync(s.FilePath); _ = RecordPlayAsync(); } }
    private void OnPrevious() { _isSwitchingSong = true; var s = _playQueue.Previous(); if (s != null) { CurrentSong = s; _ = _audioPlayer.PlayAsync(s.FilePath); _ = RecordPlayAsync(); } }

    /// <summary>
    /// 与播放队列同步当前歌曲状态
    /// </summary>
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
        SyncPlayMode();
        if (_audioPlayer.IsPlaying) PlayPauseIcon = "⏸";
    }

    /// <summary>同步播放模式图标与 PlayQueue 的实际模式（供 RestoreAsync 调用）</summary>
    public void SyncPlayMode()
    {
        PlayModeIcon = _playQueue.PlayMode switch
        {
            PlayMode.ListRepeat => "🔁",
            PlayMode.SingleRepeat => "🔂",
            PlayMode.Shuffle => "🔀",
            _ => "➡️"
        };
    }

    /// <summary>
    /// 异步加载歌曲封面图片并缓存到本地
    /// </summary>
    public async Task LoadCoverAsync(Song? song, CancellationToken ct = default)
    {
        CoverSource = "";
        if (song == null) return;
        try
        {
            if (song.Source == SongSource.Local && song.MediaStoreId > 0)
            {
                var msBitmap = await Platforms.Android.MediaStoreCoverHelper.LoadCoverFromMediaStoreAsync(song.MediaStoreId, 480);
                if (msBitmap != null)
                {
                    var cacheDir = Path.Combine(global::Android.App.Application.Context.CacheDir!.AbsolutePath, "covers");
                    Directory.CreateDirectory(cacheDir);
                    var coverPath = Path.Combine(cacheDir, $"cover_{song.Id}.jpg");
                    using var fs = File.Create(coverPath);
                    await msBitmap.CompressAsync(Android.Graphics.Bitmap.CompressFormat.Jpeg, 85, fs);
                    msBitmap.Recycle();
                    var songId = song.Id;
                    _dispatcher.Post(() => { if (CurrentSong?.Id == songId) CoverSource = coverPath; });
                    return;
                }
            }

            byte[]? coverBytes = null;

            if (song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                coverBytes = ExtractCoverFromContentUri(song.FilePath);
            }

            Stream? stream = coverBytes != null
                ? new MemoryStream(coverBytes)
                : await _musicLibrary.GetAlbumCoverAsync(song);
            ct.ThrowIfCancellationRequested();

            if (stream == null && (song.Source == SongSource.WebDAV || song.Source == SongSource.SMB))
            {
                stream = await GetNetworkCoverAsync(song);
                ct.ThrowIfCancellationRequested();
            }

            if (stream != null)
            {
                var cacheDir = Path.Combine(global::Android.App.Application.Context.CacheDir!.AbsolutePath, "covers");
                Directory.CreateDirectory(cacheDir);
                var coverPath = Path.Combine(cacheDir, $"cover_{song.Id}.jpg");

                var bitmap = await Android.Graphics.BitmapFactory.DecodeStreamAsync(stream);
                stream.Dispose();
                if (bitmap != null)
                {
                    const int maxCoverSize = 960;
                    if (bitmap.Width > maxCoverSize || bitmap.Height > maxCoverSize)
                    {
                        var scale = (float)maxCoverSize / Math.Max(bitmap.Width, bitmap.Height);
                        var w = (int)(bitmap.Width * scale);
                        var h = (int)(bitmap.Height * scale);
                        var scaled = Android.Graphics.Bitmap.CreateScaledBitmap(bitmap, w, h, true);
                        bitmap.Recycle();
                        bitmap = scaled;
                    }
                    using var fs = File.Create(coverPath);
                    await bitmap.CompressAsync(Android.Graphics.Bitmap.CompressFormat.Jpeg, 85, fs);
                    bitmap.Recycle();
                }

                var songId = song.Id;
                _dispatcher.Post(() => { if (CurrentSong?.Id == songId) CoverSource = coverPath; });
            }
        }
        catch { }
    }

    /// <summary>
    /// 从Content URI提取内嵌封面
    /// </summary>
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

    /// <summary>
    /// 更新即将播放歌曲预览列表
    /// </summary>
    private void UpdateQueuePeek()
    {
        UpcomingSongs.Clear();
        foreach (var s in _playQueue.GetUpcomingSongs(3)) UpcomingSongs.Add(s);
        QueueHint = UpcomingSongs.Count > 0 ? $"下一首: {UpcomingSongs[0].Title}" : "";
        OnPropertyChanged(nameof(UpcomingSongs));
    }

    /// <summary>
    /// 播放器状态变化回调，处理播放、暂停、停止和错误状态
    /// </summary>
    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        _dispatcher.Post(() =>
        {
            if (e.State == PlaybackState.Stopped)
            {
                // 防御性检查：如果当前位置离歌曲末尾还很远，说明这是 seek 或缓冲导致的误判，不应切歌
                var duration = _audioPlayer.Duration;
                var pos = _audioPlayer.CurrentPosition;
                if (duration > TimeSpan.Zero && pos < duration - TimeSpan.FromMilliseconds(500))
                {
                    System.Diagnostics.Debug.WriteLine($"[CatClaw] 忽略误触发的 Stopped（pos={pos}, dur={duration}）");
                    return;
                }
                if (StopAfterCurrentSong)
                {
                    StopAfterCurrentSong = false;
                    _ = _audioPlayer.PauseAsync();
                    return;
                }
                if (!_isSwitchingSong && !_isRestoring)
                    Next();
            }
            else if (e.State == PlaybackState.Playing)
            {
                _isSwitchingSong = false;
                _isRestoring = false;
                var queueSong = _playQueue.CurrentSong;
                if (queueSong != null && (CurrentSong == null || CurrentSong.Id != queueSong.Id))
                {
                    CurrentSong = queueSong;
                }
                _lastActiveSong = CurrentSong;
                _errorDialogCts?.Cancel();
                _errorDialogCts = null;
                PlayPauseIcon = "⏸";
            }
            else if (e.State is PlaybackState.Paused or PlaybackState.Error)
            {
                _isSwitchingSong = false;
                PlayPauseIcon = "▶";
                if (e.State == PlaybackState.Error)
                {
                    if (_isRestoring)
                    {
                        _isRestoring = false;
                        System.Diagnostics.Debug.WriteLine($"[CatClaw] 恢复播放失败（静默）: {e.ErrorMessage}");
                        return;
                    }
                    var actualPath = _audioPlayer.CurrentSongFilePath;
                    if (!string.IsNullOrEmpty(actualPath) && CurrentSong != null
                        && CurrentSong.FilePath != actualPath && _lastActiveSong != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CatClaw] 播放失败，回退到: {_lastActiveSong.Title}");
                        CurrentSong = _lastActiveSong;
                    }
                    if (!string.IsNullOrEmpty(e.ErrorMessage))
                        ShowErrorDialogDelayed(e.ErrorMessage);
                }
                else if (e.State == PlaybackState.Paused && _isRestoring)
                {
                    _isRestoring = false;
                }
            }
        });
    }

    /// <summary>延迟 1 秒显示错误对话框，期间如果恢复播放则取消</summary>
    private void ShowErrorDialogDelayed(string message)
    {
        // 取消之前的延迟
        _errorDialogCts?.Cancel();
        _errorDialogCts = new CancellationTokenSource();
        var token = _errorDialogCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, token);
                if (!token.IsCancellationRequested)
                {
                    _dispatcher.Post(() => ShowErrorDialog(message));
                }
            }
            catch (TaskCanceledException) { /* 恢复了，不弹框 */ }
        });
    }

    /// <summary>在 UI 线程弹出错误对话框（毛玻璃风格）</summary>
    private static void ShowErrorDialog(string message)
    {
        var activity = MainActivity.Instance;
        if (activity == null) return;
        try
        {
            new GlassDialog(activity)
                .SetTitle("播放失败")
                .AddMessage(message)
                .AddPositiveButton("确定", (_) => { })
                .Show();
        }
        catch { }
    }

    /// <summary>
    /// 播放位置变化回调，更新歌词索引和当前位置
    /// </summary>
    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        _pendingPosition = position;
        if (Interlocked.Exchange(ref _positionUpdateQueued, 1) == 1)
            return;

        _dispatcher.Post(() =>
        {
            Interlocked.Exchange(ref _positionUpdateQueued, 0);
            var pos = _pendingPosition;
            _isPositionUpdating = true;
            CurrentPosition = pos;
            var duration = _audioPlayer.Duration;
            if (duration > TimeSpan.Zero)
                TotalDuration = duration;
            if (CurrentLyrics?.Lines is { Count: > 0 })
            {
                var idx = _lyricsService.GetCurrentLyricIndex(CurrentLyrics, pos);
                // 逐字歌词行切换保护：若当前行最后一个字尚未唱完，不切换到下一行。
                // TTML/AMLL 歌词中，最后一字的结束时间可能晚于下一行的开始时间
                // （行间和声重叠、句尾拖音等），此时应等当前行唱完再切。
                // 额外缓冲 50ms（约 3 帧），确保 progress=1.0 有足够时间渲染到屏幕，
                // 避免行切换时同一 UI tick 中 progress=1.0 和新行文本互相覆盖导致最后一字未着色
                if (idx != _lastLyricIndex && _lastLyricIndex >= 0 && _lastLyricIndex < CurrentLyrics.Lines.Count)
                {
                    var prevLine = CurrentLyrics.Lines[_lastLyricIndex];
                    if (prevLine.WordTimestamps != null && prevLine.WordTimestamps.Count > 0)
                    {
                        var lastWord = prevLine.WordTimestamps[^1];
                        var lastWordEnd = lastWord.Start + lastWord.Duration;
                        if (pos < lastWordEnd + TimeSpan.FromMilliseconds(50))
                        {
                            // 当前行最后一个字还没唱完（或刚唱完但需缓冲一帧），保持当前行
                            idx = _lastLyricIndex;
                        }
                    }
                }
                if (idx != _lastLyricIndex)
                {
                    _lastLyricIndex = idx;
                    HasPerLineAlignment = CurrentLyrics.HasPerLineAlignment;
                    if (idx >= 0 && idx < CurrentLyrics.Lines.Count)
                    {
                        _prevLyricLine8 = FormatLyricLine(CurrentLyrics.Lines, idx - 8);
                        _prevLyricLine7 = FormatLyricLine(CurrentLyrics.Lines, idx - 7);
                        _prevLyricLine6 = FormatLyricLine(CurrentLyrics.Lines, idx - 6);
                        _prevLyricLine5 = FormatLyricLine(CurrentLyrics.Lines, idx - 5);
                    _prevLyricLine4 = FormatLyricLine(CurrentLyrics.Lines, idx - 4);
                    _prevLyricLine3 = FormatLyricLine(CurrentLyrics.Lines, idx - 3);
                    _prevLyricLine2 = FormatLyricLine(CurrentLyrics.Lines, idx - 2);
                    _prevLyricLine = FormatLyricLine(CurrentLyrics.Lines, idx - 1);
                    _nextLyricLine = FormatLyricLine(CurrentLyrics.Lines, idx + 1);
                    _nextLyricLine2 = FormatLyricLine(CurrentLyrics.Lines, idx + 2);
                    _nextLyricLine3 = FormatLyricLine(CurrentLyrics.Lines, idx + 3);
                    _nextLyricLine4 = FormatLyricLine(CurrentLyrics.Lines, idx + 4);
                    _nextLyricLine5 = FormatLyricLine(CurrentLyrics.Lines, idx + 5);
                    _nextLyricLine6 = FormatLyricLine(CurrentLyrics.Lines, idx + 6);
                    _nextLyricLine7 = FormatLyricLine(CurrentLyrics.Lines, idx + 7);
                    _nextLyricLine8 = FormatLyricLine(CurrentLyrics.Lines, idx + 8);
                    _currentLyricLine = FormatLyricLine(CurrentLyrics.Lines, idx);

                    // 设置逐行对齐
                    _prevLyricAlignment2 = GetLineAlignment(CurrentLyrics.Lines, idx - 2);
                    _prevLyricAlignment = GetLineAlignment(CurrentLyrics.Lines, idx - 1);
                    _currentLyricAlignment = GetLineAlignment(CurrentLyrics.Lines, idx);
                    _nextLyricAlignment = GetLineAlignment(CurrentLyrics.Lines, idx + 1);
                    _nextLyricAlignment2 = GetLineAlignment(CurrentLyrics.Lines, idx + 2);

                    CurrentLyricIndex = idx;
                    OnPropertyChanged(nameof(CurrentLyricLine));

                    // 计算合唱伙伴行（用于同时着色）
                    var partner = FindDuetPartner(CurrentLyrics.Lines, idx, pos);
                    if (partner != DuetPartnerIndex)
                    {
                        DuetPartnerIndex = partner;
                        OnPropertyChanged(nameof(HasDuetPartner));
                    }
                }
                else if (idx < 0)
                {
                    _prevLyricLine8 = "";
                    _prevLyricLine7 = "";
                    _prevLyricLine6 = "";
                    _prevLyricLine5 = "";
                    _prevLyricLine4 = "";
                    _prevLyricLine3 = "";
                    _prevLyricLine2 = "";
                    _prevLyricLine = "";
                    _currentLyricLine = "";
                    _nextLyricLine = FormatLyricLine(CurrentLyrics.Lines, 0);
                    _nextLyricLine2 = FormatLyricLine(CurrentLyrics.Lines, 1);
                    _nextLyricLine3 = FormatLyricLine(CurrentLyrics.Lines, 2);
                    _nextLyricLine4 = FormatLyricLine(CurrentLyrics.Lines, 3);
                    _nextLyricLine5 = FormatLyricLine(CurrentLyrics.Lines, 4);
                    _nextLyricLine6 = FormatLyricLine(CurrentLyrics.Lines, 5);
                    _nextLyricLine7 = FormatLyricLine(CurrentLyrics.Lines, 6);
                    _nextLyricLine8 = FormatLyricLine(CurrentLyrics.Lines, 7);
                    CurrentLyricIndex = -1;
                    if (DuetPartnerIndex != -1)
                    {
                        DuetPartnerIndex = -1;
                        OnPropertyChanged(nameof(HasDuetPartner));
                    }
                    OnPropertyChanged(nameof(CurrentLyricLine));
                }
                }
                if (LyricStyle == 1) UpdateLyricSpannable();

                // 更新合唱伙伴行的进度（每次位置更新都计算）
                if (DuetPartnerIndex >= 0 && DuetPartnerIndex < CurrentLyrics.Lines.Count)
                    DuetPartnerProgress = CalculateLineProgress(CurrentLyrics.Lines, DuetPartnerIndex, pos);
            }
            _isPositionUpdating = false;
            // 33ms 定时器下，75 次 ≈ 2.5 秒保存一次播放状态
            if (++_saveCounter % 75 == 0)
                CatClawMusic.UI.Services.PlaybackStateManager.Save(_audioPlayer, CurrentSong, _playQueue);
        });
    }

    /// <summary>
    /// 异步加载歌词，支持本地文件和远程歌词源
    /// </summary>
    public async Task LoadLyricsAsync(Song? song, CancellationToken ct = default)
    {
        if (song == null) { CurrentLyricLine = "🐾 猫爪音乐"; NextLyricLine = "选择一首歌曲开始播放吧~"; PrevLyricLine8 = ""; PrevLyricLine7 = ""; PrevLyricLine6 = ""; PrevLyricLine5 = ""; PrevLyricLine4 = ""; PrevLyricLine3 = ""; PrevLyricLine2 = ""; PrevLyricLine = ""; NextLyricLine2 = ""; NextLyricLine3 = ""; NextLyricLine4 = ""; NextLyricLine5 = ""; NextLyricLine6 = ""; NextLyricLine7 = ""; NextLyricLine8 = ""; CurrentLyrics = null; CurrentLyricIndex = -1; _lastSpannableLineIdx = -1; _lastLyricIndex = -999; DuetPartnerIndex = -1; return; }
        var songId = song.Id;
        CurrentLyricLine = ""; NextLyricLine = ""; PrevLyricLine8 = ""; PrevLyricLine7 = ""; PrevLyricLine6 = ""; PrevLyricLine5 = ""; PrevLyricLine4 = ""; PrevLyricLine3 = ""; PrevLyricLine2 = ""; PrevLyricLine = ""; NextLyricLine2 = ""; NextLyricLine3 = ""; NextLyricLine4 = ""; NextLyricLine5 = ""; NextLyricLine6 = ""; NextLyricLine7 = ""; NextLyricLine8 = "";
        CurrentLyrics = null;
        CurrentLyricIndex = -1;
        _lastSpannableLineIdx = -1; _lastLyricIndex = -999;
        DuetPartnerIndex = -1;
        try
        {
            if (LyricsMode == 2)
            {
                CurrentLyrics = null;
            }
            else if (LyricsMode == 1)
            {
                CurrentLyrics = await _lyricsService.GetLocalLyricsAsync(song, false, true);
            }
            else
            {
                var skipEmbedded = song.Source == SongSource.WebDAV || song.Source == SongSource.SMB;
                CurrentLyrics = await _lyricsService.GetLocalLyricsAsync(song, skipEmbedded);
            }
            System.Diagnostics.Debug.WriteLine($"[LoadLyricsAsync] mode={LyricsMode}, local={CurrentLyrics?.Lines?.Count}, path={song.FilePath}, lyricsPath={song.LyricsPath}");
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LoadLyricsAsync] GetLocalLyricsAsync 异常: {ex.Message}"); }
        if (ct.IsCancellationRequested) return;
        if (CurrentSong?.Id != songId) return;

        // 兜底：与歌曲详情页保持一致，使用 FindExternalLyricsTextAsync（含模糊匹配）和数据库
        if (CurrentLyrics == null && LyricsMode != 2)
        {
            try
            {
                var raw = await _lyricsService.FindExternalLyricsTextAsync(song);
                System.Diagnostics.Debug.WriteLine($"[LoadLyricsAsync] FindExternalLyricsTextAsync 原始长度: {raw?.Length ?? 0}, 含空字符: {raw?.Contains('\0') ?? false}");
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    CurrentLyrics = await Task.Run(() => _lyricsService.TryParseLyrics(raw));
                    System.Diagnostics.Debug.WriteLine($"[LoadLyricsAsync] FindExternalLyricsTextAsync 解析结果: {CurrentLyrics?.Lines?.Count} 行");
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LoadLyricsAsync] FindExternalLyricsTextAsync 异常: {ex.Message}"); }
            if (ct.IsCancellationRequested) return;
            if (CurrentSong?.Id != songId) return;
        }

        if (CurrentLyrics == null && LyricsMode != 2 && _database != null)
        {
            try
            {
                var dbLyric = await _database.GetLyricAsync(song.Id);
                if (dbLyric != null && !string.IsNullOrEmpty(dbLyric.Content))
                {
                    CurrentLyrics = await Task.Run(() => _lyricsService.TryParseLyrics(dbLyric.Content));
                    System.Diagnostics.Debug.WriteLine($"[LoadLyricsAsync] 数据库歌词解析结果: {CurrentLyrics?.Lines?.Count} 行");
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LoadLyricsAsync] 数据库歌词异常: {ex.Message}"); }
            if (ct.IsCancellationRequested) return;
            if (CurrentSong?.Id != songId) return;
        }

        if (CurrentLyrics == null && (song.Source == SongSource.WebDAV || song.Source == SongSource.SMB))
        {
            try
            {
                CurrentLyrics = await GetNetworkLyricsAsync(song);
            }
            catch (OperationCanceledException) { return; }
            catch { }
            if (ct.IsCancellationRequested) return;
            if (CurrentSong?.Id != songId) return;

            if (CurrentLyrics == null)
            {
                try { CurrentLyrics = await _lyricsService.GetLocalLyricsAsync(song, false); }
                catch { }
            }
        }

        if (CurrentSong?.Id != songId) return;

        // 将 CurrentLyrics 快照到局部变量，防止切歌时另一个 LoadLyricsAsync 将其置 null
        var lyrics = CurrentLyrics;

        if (LyricsMode == 2) { CurrentLyricLine = ""; NextLyricLine = ""; }
        else if (lyrics == null) { CurrentLyricLine = "暂无歌词"; NextLyricLine = ""; }
        else if (lyrics.Lines.Count > 0)
        {
            var pos = _audioPlayer.CurrentPosition;
            var idx = _lyricsService.GetCurrentLyricIndex(lyrics, pos);
            if (idx >= 0 && idx < lyrics.Lines.Count)
            {
                PrevLyricLine8 = idx > 7 ? lyrics.Lines[idx - 8].Text : "";
                PrevLyricLine7 = idx > 6 ? lyrics.Lines[idx - 7].Text : "";
                PrevLyricLine6 = idx > 5 ? lyrics.Lines[idx - 6].Text : "";
                PrevLyricLine5 = idx > 4 ? lyrics.Lines[idx - 5].Text : "";
                PrevLyricLine4 = idx > 3 ? lyrics.Lines[idx - 4].Text : "";
                PrevLyricLine3 = idx > 2 ? lyrics.Lines[idx - 3].Text : "";
                PrevLyricLine2 = idx > 1 ? lyrics.Lines[idx - 2].Text : "";
                PrevLyricLine = idx > 0 ? lyrics.Lines[idx - 1].Text : "";
                NextLyricLine = idx + 1 < lyrics.Lines.Count ? lyrics.Lines[idx + 1].Text : "";
                NextLyricLine2 = idx + 2 < lyrics.Lines.Count ? lyrics.Lines[idx + 2].Text : "";
                NextLyricLine3 = idx + 3 < lyrics.Lines.Count ? lyrics.Lines[idx + 3].Text : "";
                NextLyricLine4 = idx + 4 < lyrics.Lines.Count ? lyrics.Lines[idx + 4].Text : "";
                NextLyricLine5 = idx + 5 < lyrics.Lines.Count ? lyrics.Lines[idx + 5].Text : "";
                NextLyricLine6 = idx + 6 < lyrics.Lines.Count ? lyrics.Lines[idx + 6].Text : "";
                NextLyricLine7 = idx + 7 < lyrics.Lines.Count ? lyrics.Lines[idx + 7].Text : "";
                NextLyricLine8 = idx + 8 < lyrics.Lines.Count ? lyrics.Lines[idx + 8].Text : "";
                CurrentLyricLine = lyrics.Lines[idx].Text;
            }
            else
            {
                PrevLyricLine8 = "";
                PrevLyricLine7 = "";
                PrevLyricLine6 = "";
                PrevLyricLine5 = "";
                PrevLyricLine4 = "";
                PrevLyricLine3 = "";
                PrevLyricLine2 = "";
                PrevLyricLine = "";
                CurrentLyricLine = "";
                NextLyricLine = lyrics.Lines[0].Text;
                NextLyricLine2 = lyrics.Lines.Count > 1 ? lyrics.Lines[1].Text : "";
                NextLyricLine3 = lyrics.Lines.Count > 2 ? lyrics.Lines[2].Text : "";
                NextLyricLine4 = lyrics.Lines.Count > 3 ? lyrics.Lines[3].Text : "";
                NextLyricLine5 = lyrics.Lines.Count > 4 ? lyrics.Lines[4].Text : "";
                NextLyricLine6 = lyrics.Lines.Count > 5 ? lyrics.Lines[5].Text : "";
                NextLyricLine7 = lyrics.Lines.Count > 6 ? lyrics.Lines[6].Text : "";
                NextLyricLine8 = lyrics.Lines.Count > 7 ? lyrics.Lines[7].Text : "";
            }
        }
    }

    private async Task RecordPlayAsync()
    {
        if (_database == null || CurrentSong == null) return;
        try
        {
            await _database.EnsureInitializedAsync();
            await _database.RecordPlayAsync(CurrentSong.Id);
            var playlistVm = MainApplication.Services.GetService(typeof(PlaylistViewModel)) as PlaylistViewModel;
            if (playlistVm != null)
            {
                playlistVm.MarkDirty();
                _ = playlistVm.RefreshSystemPlaylistCountsAsync();
            }
        }
        catch { }
    }

    private async Task SaveFavoriteAsync()
    {
        if (_database == null || CurrentSong == null) return;
        try
        {
            await _database.EnsureInitializedAsync();
            await _database.SetFavoriteAsync(CurrentSong.Id, IsLiked);
            var playlistVm = MainApplication.Services.GetService(typeof(PlaylistViewModel)) as PlaylistViewModel;
            if (playlistVm != null)
            {
                playlistVm.MarkDirty();
                _ = playlistVm.RefreshSystemPlaylistCountsAsync();
            }
        }
        catch { }
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

    private async Task ResolveSongDetails(Song? song)
    {
        if (song == null || _database == null) return;

        if ((song.Source == SongSource.WebDAV || song.Source == SongSource.SMB)
            && (song.Artist == "未知艺术家" || song.Album == "未知专辑" || song.Duration == 0)
            && !_metadataFetchAttempted.Contains(song.Id))
        {
            _metadataFetchAttempted.Add(song.Id);

            if (IsNavidromeSong(song) && _subsonic != null)
            {
                var songId = ExtractSubsonicSongId(song);
                if (!string.IsNullOrEmpty(songId))
                {
                    var navProfile = await GetNetworkProfileAsync(ProtocolType.Navidrome);
                    if (navProfile != null)
                    {
                        try
                        {
                            var resolved = await _subsonic.GetSongAsync(songId, navProfile);
                            if (resolved != null)
                            {
                                if (!string.IsNullOrEmpty(resolved.Artist))
                                    song.Artist = resolved.Artist;
                                if (!string.IsNullOrEmpty(resolved.Album))
                                    song.Album = resolved.Album;
                                if (resolved.Duration > 0)
                                    song.Duration = resolved.Duration;
                                if (!string.IsNullOrEmpty(resolved.Artist))
                                {
                                    var names = MusicUtility.SplitArtistNames(resolved.Artist);
                                    song.ArtistId = await _database.EnsureArtistAsync(names[0]);
                                    if (names.Count > 1)
                                    {
                                        // 次要艺术家
                                        var allIds = new List<int>();
                                        foreach (var n in names)
                                            allIds.Add(await _database.EnsureArtistAsync(n));
                                        try { await _database.SaveSongArtistsBatchAsync(new List<(int, List<int>)> { (song.Id, allIds) }); } catch { }
                                    }
                                }
                                if (!string.IsNullOrEmpty(resolved.Album))
                                    song.AlbumId = await _database.EnsureAlbumAsync(resolved.Album, song.ArtistId);
                                await _database.SaveSongAsync(song);
                                _dispatcher.Post(() =>
                                {
                                    if (CurrentSong?.Id == song.Id)
                                        OnPropertyChanged(nameof(CurrentSong));
                                });
                                return;
                            }
                        }
                        catch { }
                    }
                }
            }

            if (_networkMusic != null)
            {
                var profile = await GetNetworkProfileAsync(
                    song.Source == SongSource.SMB ? ProtocolType.SMB : ProtocolType.WebDAV);
                if (profile != null)
                {
                    try
                    {
                        var updated = await _networkMusic.FetchSongMetadataAsync(song, profile);
                        if (updated != null)
                        {
                            if (!string.IsNullOrEmpty(updated.Artist))
                            {
                                var names = MusicUtility.SplitArtistNames(updated.Artist);
                                updated.ArtistId = await _database.EnsureArtistAsync(names[0]);
                                if (names.Count > 1)
                                {
                                    var allIds = new List<int>();
                                    foreach (var n in names)
                                        allIds.Add(await _database.EnsureArtistAsync(n));
                                    try { await _database.SaveSongArtistsBatchAsync(new List<(int, List<int>)> { (updated.Id, allIds) }); } catch { }
                                }
                            }
                            if (!string.IsNullOrEmpty(updated.Album))
                                updated.AlbumId = await _database.EnsureAlbumAsync(updated.Album, updated.ArtistId);
                            await _database.SaveSongAsync(updated);
                            _dispatcher.Post(() =>
                            {
                                if (CurrentSong?.Id == updated.Id)
                                {
                                    OnPropertyChanged(nameof(CurrentSong));
                                }
                            });
                            return;
                        }
                    }
                    catch { }
                }
            }
        }

        if (!string.IsNullOrEmpty(song.Artist) && !string.IsNullOrEmpty(song.Album)) return;

        try
        {
            if (song.ArtistId > 0 && string.IsNullOrEmpty(song.Artist))
            {
                var allArtists = await _database.GetAllArtistsAsync();
                var artist = allArtists.FirstOrDefault(a => a.Id == song.ArtistId);
                if (!string.IsNullOrEmpty(artist?.Name))
                    song.Artist = artist.Name;
            }
            if (song.AlbumId > 0 && string.IsNullOrEmpty(song.Album))
            {
                var allAlbums = await _database.GetAllAlbumsAsync();
                var album = allAlbums.FirstOrDefault(a => a.Id == song.AlbumId);
                if (!string.IsNullOrEmpty(album?.Title))
                    song.Album = album.Title;
            }
        }
        catch { }
    }

    private async Task<ConnectionProfile?> GetNetworkProfileAsync(ProtocolType protocol)
    {
        if (protocol == ProtocolType.Navidrome && _cachedNavidromeProfile != null) return _cachedNavidromeProfile;
        if (protocol == ProtocolType.WebDAV && _cachedWebDavProfile != null) return _cachedWebDavProfile;
        if (protocol == ProtocolType.SMB && _cachedSmbProfile != null) return _cachedSmbProfile;
        if (_networkMusic == null) return null;
        try
        {
            var profiles = await _networkMusic.GetProfilesAsync();
            _cachedNavidromeProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.Navidrome && p.IsEnabled);
            _cachedWebDavProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.WebDAV && p.IsEnabled);
            _cachedSmbProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.SMB && p.IsEnabled);
            return protocol == ProtocolType.Navidrome ? _cachedNavidromeProfile
                : protocol == ProtocolType.SMB ? _cachedSmbProfile
                : _cachedWebDavProfile;
        }
        catch { return null; }
    }

    private static bool IsNavidromeSong(Song song)
    {
        return !string.IsNullOrEmpty(song.FilePath) && song.FilePath.Contains("stream.view?id=");
    }

    private async Task<Stream?> GetNetworkCoverAsync(Song song)
    {
        if (_networkMusic == null) return null;
        var coverId = song.CoverArtPath ?? song.RemoteId;
        if (string.IsNullOrEmpty(coverId)) return null;
        var protocol = IsNavidromeSong(song) ? ProtocolType.Navidrome
            : song.Source == SongSource.SMB ? ProtocolType.SMB : ProtocolType.WebDAV;
        var profile = await GetNetworkProfileAsync(protocol);
        if (profile == null) return null;
        try { return await _networkMusic.GetCoverAsync(coverId, profile); }
        catch { return null; }
    }

    private static string GetLyricsCachePath(int songId)
    {
        var cacheDir = Path.Combine(global::Android.App.Application.Context.CacheDir!.AbsolutePath, "lyrics");
        Directory.CreateDirectory(cacheDir);
        return Path.Combine(cacheDir, $"lyrics_{songId}.lrc");
    }

    private async Task<LrcLyrics?> GetNetworkLyricsAsync(Song song)
    {
        var cachePath = GetLyricsCachePath(song.Id);
        if (File.Exists(cachePath))
        {
            try
            {
                var cached = await File.ReadAllTextAsync(cachePath);
                if (!string.IsNullOrWhiteSpace(cached))
                {
                    var parsed = _lyricsService.ParseLrc(cached);
                    if (parsed != null) return parsed;
                    return BuildUnsynchronizedLyrics(cached);
                }
            }
            catch { }
        }

        var isNavidrome = IsNavidromeSong(song);
        var protocol = isNavidrome ? ProtocolType.Navidrome
            : song.Source == SongSource.SMB ? ProtocolType.SMB : ProtocolType.WebDAV;
        var profile = await GetNetworkProfileAsync(protocol);
        if (profile == null) return null;

        string? lrcText = null;
        if (isNavidrome)
        {
            if (_subsonic == null) return null;
            try
            {
                var songId = ExtractSubsonicSongId(song);
                if (string.IsNullOrEmpty(songId)) return null;
                lrcText = await _subsonic.GetLyricsAsync(songId, profile);
            }
            catch { return null; }
        }
        else
        {
            if (_networkMusic == null) return null;
            var remotePath = song.RemoteId ?? song.CoverArtPath;
            if (string.IsNullOrEmpty(remotePath)) return null;
            try { lrcText = await _networkMusic.GetLyricsAsync(remotePath, profile); }
            catch { return null; }
        }

        if (string.IsNullOrEmpty(lrcText)) return null;

        try { await File.WriteAllTextAsync(cachePath, lrcText); } catch { }

        var result = _lyricsService.ParseLrc(lrcText);
        if (result != null) return result;
        return BuildUnsynchronizedLyrics(lrcText);
    }

    /// <summary>将纯文本歌词转换为伪 LRC（无时间戳，按行显示）</summary>
    private static LrcLyrics BuildUnsynchronizedLyrics(string text)
    {
        var lyrics = new LrcLyrics(); // unused in unsynchronized path
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
