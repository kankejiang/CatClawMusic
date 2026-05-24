using System.Collections.ObjectModel;
using Android.Text;
using Android.Text.Style;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
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
    private bool _isPositionUpdating;
    private int _saveCounter;
    private ConnectionProfile? _cachedNavidromeProfile;
    private ConnectionProfile? _cachedWebDavProfile;
    private ConnectionProfile? _cachedSmbProfile;
    private readonly HashSet<int> _metadataFetchAttempted = new();
    private CancellationTokenSource? _errorDialogCts;
    private CancellationTokenSource? _songLoadCts;
    private Song? _lastActiveSong; // 上一次成功播放的歌曲（用于播放失败时回退）
    private volatile bool _isSwitchingSong;
    private volatile bool _isRestoring;
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

    /// <summary>逐字歌词 Spannable，当前字高亮为主题色，供 Fragment 直接设置到 TextView</summary>
    public ISpannable? CurrentLyricSpannable { get; private set; }

    /// <summary>
    /// 根据当前播放位置生成逐字着色的 SpannableString
    /// 优先使用 LRC 内嵌 &lt;mm:ss.xx&gt; 逐字时间戳，否则实时动态映射播放进度到字符
    /// </summary>
    public void UpdateLyricSpannable()
    {
        var lines = CurrentLyrics?.Lines;
        var idx = CurrentLyricIndex;
        if (lines == null || idx < 0 || idx >= lines.Count) { ClearSpannable(); return; }

        var line = lines[idx];
        var text = line.Text;
        if (string.IsNullOrEmpty(text)) { ClearSpannable(); return; }

        var ss = new SpannableString(text);
        var baseGray = Android.Graphics.Color.Argb(0xDD, 0xBB, 0xBB, 0xBB);
        ss.SetSpan(new ForegroundColorSpan(baseGray), 0, text.Length, SpanTypes.ExclusiveExclusive);

        if (line.WordTimestamps is { Count: > 0 })
        {
            ApplyExactWordTimestamps(ss, line.WordTimestamps);
        }
        else
        {
            var lineDuration = GetLineDuration(lines, idx);
            ApplyDynamicProgress(ss, text, line.Timestamp, lineDuration);
        }

        CurrentLyricSpannable = ss;
        OnPropertyChanged(nameof(CurrentLyricSpannable));
    }

    private void ApplyExactWordTimestamps(SpannableString ss, List<WordTimestamp> wts)
    {
        var wordIdx = -1;
        for (int i = wts.Count - 1; i >= 0; i--)
            if (wts[i].Start <= CurrentPosition) { wordIdx = i; break; }
        if (wordIdx < 0) return;

        var lineEnd = wts[wordIdx].Start + wts[wordIdx].Duration;
        var nextLineStart = GetNextLineStart();
        var nearEnd = nextLineStart.HasValue && (nextLineStart.Value - CurrentPosition).TotalMilliseconds < 200;

        ApplyWordSpans(ss, wts, wordIdx, (i, w) =>
        {
            if (i == wordIdx)
            {
                if (nearEnd || i == wts.Count - 1)
                    return 1f;
                return Math.Clamp((float)((CurrentPosition - w.Start).TotalMilliseconds / w.Duration.TotalMilliseconds), 0f, 1f);
            }
            return i < wordIdx ? 1f : 0f;
        });
    }

    private TimeSpan? GetNextLineStart()
    {
        var lines = CurrentLyrics?.Lines;
        var idx = CurrentLyricIndex;
        if (lines == null || idx < 0 || idx + 1 >= lines.Count) return null;
        return lines[idx + 1].Timestamp;
    }

    private void ApplyDynamicProgress(SpannableString ss, string text, TimeSpan lineStart, TimeSpan duration)
    {
        var units = SplitTextUnits(text);
        if (units.Count == 0) return;
        if (duration.TotalMilliseconds <= 0) return;

        var progress = Math.Clamp((CurrentPosition - lineStart).TotalMilliseconds / duration.TotalMilliseconds, 0.0, 1.0);

        var nextLineStart = GetNextLineStart();
        var nearEnd = nextLineStart.HasValue && (nextLineStart.Value - CurrentPosition).TotalMilliseconds < 200;
        if (nearEnd) progress = 1.0;

        var unitProgress = progress * units.Count;
        var activeIdx = (int)unitProgress;
        var activeFrac = (float)(unitProgress - activeIdx);

        var fakeTimestamps = new List<WordTimestamp>();
        for (int i = 0; i < units.Count; i++)
            fakeTimestamps.Add(new WordTimestamp { Word = units[i], Start = TimeSpan.Zero, Duration = TimeSpan.Zero });

        ApplyWordSpans(ss, fakeTimestamps, activeIdx, (i, _) =>
        {
            if (i < activeIdx) return 1f;
            if (i == activeIdx) return activeFrac;
            return 0f;
        });
    }

    private void ApplyWordSpans(SpannableString ss, List<WordTimestamp> wts, int highlightIdx, Func<int, WordTimestamp, float> getAlpha)
    {
        var offset = 0;
        for (int i = 0; i < wts.Count; i++)
        {
            var wlen = wts[i].Word.Length;
            var alpha = getAlpha(i, wts[i]);

            if (alpha > 0.01f && offset + wlen <= ss.Length())
            {
                var r = (int)(0xBB + (0xFF - 0xBB) * alpha);
                var g = (int)(0xBB + (0xFF - 0xBB) * alpha);
                var b = (int)(0xBB + (0xFF - 0xBB) * alpha);
                ss.SetSpan(new ForegroundColorSpan(Android.Graphics.Color.Rgb(r, g, b)),
                    offset, offset + wlen, SpanTypes.ExclusiveExclusive);
            }
            offset += wlen;
        }
    }

    private static List<string> SplitTextUnits(string text)
    {
        var units = new List<string>();
        var currentWord = "";
        foreach (var ch in text)
        {
            if (ch >= 0x4E00 && ch <= 0x9FFF || ch >= 0x3400 && ch <= 0x4DBF)
            {
                if (currentWord.Length > 0) { units.Add(currentWord); currentWord = ""; }
                units.Add(ch.ToString());
            }
            else if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch))
            {
                if (currentWord.Length > 0) { units.Add(currentWord); currentWord = ""; }
            }
            else if (char.IsLetterOrDigit(ch))
            {
                currentWord += ch;
            }
            else
            {
                if (currentWord.Length > 0) { units.Add(currentWord); currentWord = ""; }
                units.Add(ch.ToString());
            }
        }
        if (currentWord.Length > 0) units.Add(currentWord);
        return units;
    }
    

    /// <summary>清除逐字高亮状态</summary>
    private void ClearSpannable()
    {
        if (CurrentLyricSpannable != null)
        {
            CurrentLyricSpannable = null;
            OnPropertyChanged(nameof(CurrentLyricSpannable));
        }
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
                using (var fs = File.Create(coverPath)) await stream.CopyToAsync(fs);
                stream.Dispose();
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

    /// <summary>在 UI 线程弹出错误对话框</summary>
    private static void ShowErrorDialog(string message)
    {
        var activity = MainActivity.Instance;
        if (activity == null) return;
        try
        {
            new global::Android.App.AlertDialog.Builder(activity)
                .SetTitle("播放失败")
                .SetMessage(message)
                .SetPositiveButton("确定", (s, e) => { })
                .Show();
        }
        catch { }
    }

    /// <summary>
    /// 播放位置变化回调，更新歌词索引和当前位置
    /// </summary>
    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        _dispatcher.Post(() =>
        {
            _isPositionUpdating = true;
            CurrentPosition = position;
            var duration = _audioPlayer.Duration;
            if (duration > TimeSpan.Zero)
                TotalDuration = duration;
            if (CurrentLyrics?.Lines is { Count: > 0 })
            {
                var idx = _lyricsService.GetCurrentLyricIndex(CurrentLyrics, position);
                if (idx >= 0 && idx < CurrentLyrics.Lines.Count)
                {
                    PrevLyricLine2 = idx > 1 ? CurrentLyrics.Lines[idx - 2].Text : "";
                    PrevLyricLine = idx > 0 ? CurrentLyrics.Lines[idx - 1].Text : "";
                    NextLyricLine = idx + 1 < CurrentLyrics.Lines.Count ? CurrentLyrics.Lines[idx + 1].Text : "";
                    NextLyricLine2 = idx + 2 < CurrentLyrics.Lines.Count ? CurrentLyrics.Lines[idx + 2].Text : "";
                    CurrentLyricLine = CurrentLyrics.Lines[idx].Text;
                    CurrentLyricIndex = idx;
                    UpdateLyricSpannable();
                }
                else if (idx < 0)
                {
                    PrevLyricLine2 = "";
                    PrevLyricLine = "";
                    CurrentLyricLine = "";
                    NextLyricLine = CurrentLyrics.Lines[0].Text;
                    NextLyricLine2 = CurrentLyrics.Lines.Count > 1 ? CurrentLyrics.Lines[1].Text : "";
                    CurrentLyricIndex = -1;
                    ClearSpannable();
                }
            }
            _isPositionUpdating = false;
            // 每 ~5 秒保存一次播放位置（200ms 定时器 × 25）
            // 传入 CurrentSong 以保存 Source/RemoteId，解决网络歌曲 URL 动态 token 导致恢复失败的问题
            if (++_saveCounter % 25 == 0)
                CatClawMusic.UI.Services.PlaybackStateManager.Save(_audioPlayer, CurrentSong);
        });
    }

    /// <summary>
    /// 异步加载歌词，支持本地文件和远程歌词源
    /// </summary>
    public async Task LoadLyricsAsync(Song? song, CancellationToken ct = default)
    {
        if (song == null) { CurrentLyricLine = "🐾 猫爪音乐"; NextLyricLine = "选择一首歌曲开始播放吧~"; PrevLyricLine2 = ""; PrevLyricLine = ""; NextLyricLine2 = ""; CurrentLyrics = null; ClearSpannable(); CurrentLyricIndex = -1; return; }
        var songId = song.Id;
        CurrentLyricLine = ""; NextLyricLine = ""; PrevLyricLine2 = ""; PrevLyricLine = ""; NextLyricLine2 = "";
        CurrentLyrics = null;
        CurrentLyricIndex = -1;
        ClearSpannable();
        try
        {
            var skipEmbedded = song.Source == SongSource.WebDAV || song.Source == SongSource.SMB;
            CurrentLyrics = await _lyricsService.GetLocalLyricsAsync(song, skipEmbedded);
        }
        catch (OperationCanceledException) { return; }
        catch { }
        if (ct.IsCancellationRequested) return;
        if (CurrentSong?.Id != songId) return;

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

        if (CurrentLyrics == null) { CurrentLyricLine = "暂无歌词"; NextLyricLine = ""; }
        else if (CurrentLyrics.Lines.Count > 0)
        {
            var pos = _audioPlayer.CurrentPosition;
            var idx = _lyricsService.GetCurrentLyricIndex(CurrentLyrics, pos);
            if (idx >= 0 && idx < CurrentLyrics!.Lines.Count)
            {
                PrevLyricLine2 = idx > 1 ? CurrentLyrics.Lines[idx - 2].Text : "";
                PrevLyricLine = idx > 0 ? CurrentLyrics.Lines[idx - 1].Text : "";
                NextLyricLine = idx + 1 < CurrentLyrics.Lines.Count ? CurrentLyrics.Lines[idx + 1].Text : "";
                NextLyricLine2 = idx + 2 < CurrentLyrics.Lines.Count ? CurrentLyrics.Lines[idx + 2].Text : "";
                CurrentLyricLine = CurrentLyrics.Lines[idx].Text;
            }
            else
            {
                PrevLyricLine2 = "";
                PrevLyricLine = "";
                CurrentLyricLine = "";
                NextLyricLine = CurrentLyrics.Lines[0].Text;
                NextLyricLine2 = CurrentLyrics.Lines.Count > 1 ? CurrentLyrics.Lines[1].Text : "";
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
                                    song.ArtistId = await _database.EnsureArtistAsync(resolved.Artist);
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
                                updated.ArtistId = await _database.EnsureArtistAsync(updated.Artist);
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
