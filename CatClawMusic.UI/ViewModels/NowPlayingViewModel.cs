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
    /// <summary>上一次的逐字索引，用于去重避免重复刷新 Spannable</summary>
    private int _lastWordIndex = -2;
    private bool _isPositionUpdating;
    private int _saveCounter;
    private ConnectionProfile? _cachedNavidromeProfile;
    private ConnectionProfile? _cachedWebDavProfile;
    private readonly HashSet<int> _metadataFetchAttempted = new();
    private CancellationTokenSource? _errorDialogCts;
    private CancellationTokenSource? _songLoadCts;
    private Song? _lastActiveSong; // 上一次成功播放的歌曲（用于播放失败时回退）
    private volatile bool _isSwitchingSong; // 用户主动切歌时设置，阻止 Stopped 事件错误调用 Next()

    [ObservableProperty] private Song? _currentSong;
    [ObservableProperty] private string _coverSource = "";
    [ObservableProperty] private string _prevLyricLine2 = "";
    [ObservableProperty] private string _prevLyricLine = "";
    [ObservableProperty] private string _currentLyricLine = "🐾 猫爪音乐";
    [ObservableProperty] private string _nextLyricLine = "选择一首歌曲开始播放吧~";
    [ObservableProperty] private string _nextLyricLine2 = "";
    [ObservableProperty] private TimeSpan _currentPosition;
    [ObservableProperty] private TimeSpan _totalDuration;
    [ObservableProperty] private string _playPauseIcon = "▶";
    [ObservableProperty] private string _playModeIcon = ""; // 构造函数中同步
    [ObservableProperty] private string _likeIcon = "🤍";
    [ObservableProperty] private bool _isLiked;
    [ObservableProperty] private int _volume = 80;
    [ObservableProperty] private string _queueHint = "";
    /// <summary>
    /// 即将播放的歌曲列表
    /// </summary>
    public ObservableCollection<Song> UpcomingSongs { get; } = new();

    /// <summary>逐字歌词 Spannable，当前字高亮为主题色，供 Fragment 直接设置到 TextView</summary>
    public ISpannable? CurrentLyricSpannable { get; private set; }

    /// <summary>
    /// 根据当前播放位置生成逐字着色的 SpannableString
    /// 优先使用 LRC 内嵌的 &lt;mm:ss.xx&gt; 逐字时间戳，否则自动拆字估算时序
    /// </summary>
    public void UpdateLyricSpannable()
    {
        var lines = CurrentLyrics?.Lines;
        var idx = CurrentLyricIndex;
        if (lines == null || idx < 0 || idx >= lines.Count)
        {
            ClearSpannable();
            return;
        }

        var line = lines[idx];
        var text = line.Text;
        if (string.IsNullOrEmpty(text))
        {
            ClearSpannable();
            return;
        }

        // 获取或自动生成逐字时间戳
        var wordTimestamps = line.WordTimestamps;
        if (wordTimestamps == null || wordTimestamps.Count == 0)
        {
            // 自动拆字 fallback：将文本按字符拆分，根据前后行的间隔均匀分配时间
            var lineDuration = GetLineDuration(lines, idx);
            wordTimestamps = AutoSplitText(text, line.Timestamp, lineDuration);
        }

        if (wordTimestamps == null || wordTimestamps.Count == 0)
        {
            ClearSpannable();
            return;
        }

        // 在本地 wordTimestamps 列表中查找当前字索引（支持自动拆字生成的时序）
        var wordIdx = -1;
        for (int i = wordTimestamps.Count - 1; i >= 0; i--)
        {
            if (wordTimestamps[i].Start <= CurrentPosition)
            { wordIdx = i; break; }
        }
        if (wordIdx == _lastWordIndex) return;
        _lastWordIndex = wordIdx;

        var ss = new SpannableString(text);
        if (wordIdx >= 0 && wordIdx < wordTimestamps.Count)
        {
            // 累积高亮：从第一个字到当前字全部着色为当前主题色
            var highlightedLength = 0;
            for (int i = 0; i <= wordIdx; i++)
                highlightedLength += wordTimestamps[i].Word.Length;

            if (highlightedLength > 0 && highlightedLength <= text.Length)
            {
                var accentColor = GetThemeAccentColor();
                ss.SetSpan(new ForegroundColorSpan(accentColor), 0, highlightedLength, SpanTypes.ExclusiveExclusive);
                ss.SetSpan(new RelativeSizeSpan(1.12f), 0, highlightedLength, SpanTypes.ExclusiveExclusive);
            }
        }
        CurrentLyricSpannable = ss;
        OnPropertyChanged(nameof(CurrentLyricSpannable));
    }

    /// <summary>清除逐字高亮状态</summary>
    private void ClearSpannable()
    {
        _lastWordIndex = -2;
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
    /// 将文本自动按字符拆分，均匀分配时间戳
    /// 中文字/英文单词各占一个单位，跳过空格/标点
    /// </summary>
    private static List<WordTimestamp> AutoSplitText(string text, TimeSpan startTime, TimeSpan duration)
    {
        var result = new List<WordTimestamp>();

        // 提取有效字符单元：中文字单独拆分，英文/数字按词拆分
        var units = new List<string>();
        var currentWord = "";
        foreach (var ch in text)
        {
            if (ch >= 0x4E00 && ch <= 0x9FFF || ch >= 0x3400 && ch <= 0x4DBF)
            {
                // 中文字
                if (currentWord.Length > 0)
                {
                    units.Add(currentWord);
                    currentWord = "";
                }
                units.Add(ch.ToString());
            }
            else if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch))
            {
                if (currentWord.Length > 0)
                {
                    units.Add(currentWord);
                    currentWord = "";
                }
            }
            else if (char.IsLetterOrDigit(ch))
            {
                currentWord += ch;
            }
            else
            {
                if (currentWord.Length > 0)
                {
                    units.Add(currentWord);
                    currentWord = "";
                }
                units.Add(ch.ToString());
            }
        }
        if (currentWord.Length > 0)
            units.Add(currentWord);

        if (units.Count == 0)
            return result;

        var perUnit = TimeSpan.FromTicks(duration.Ticks / units.Count);
        for (int i = 0; i < units.Count; i++)
        {
            result.Add(new WordTimestamp
            {
                Word = units[i],
                Start = startTime + TimeSpan.FromTicks(perUnit.Ticks * i),
                Duration = i + 1 < units.Count ? perUnit : duration - TimeSpan.FromTicks(perUnit.Ticks * i)
            });
        }

        return result;
    }

    /// <summary>获取当前主题色，用于逐字高亮</summary>
    private static Android.Graphics.Color GetThemeAccentColor()
    {
        try
        {
            var ctx = global::Android.App.Application.Context;
            var typedValue = new Android.Util.TypedValue();
            if (ctx.Theme?.ResolveAttribute(Resource.Attribute.catClawAccentColor, typedValue, true) == true)
                return new Android.Graphics.Color(typedValue.Data);
        }
        catch { }
        return Android.Graphics.Color.ParseColor("#FF80AB");
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

    private void OnNext() { var s = _playQueue.Next(); if (s != null) { CurrentSong = s; _ = _audioPlayer.PlayAsync(s.FilePath); _ = RecordPlayAsync(); } }
    private void OnPrevious() { var s = _playQueue.Previous(); if (s != null) { CurrentSong = s; _ = _audioPlayer.PlayAsync(s.FilePath); _ = RecordPlayAsync(); } }

    /// <summary>
    /// 与播放队列同步当前歌曲状态
    /// </summary>
    public void SyncWithQueue()
    {
        var queueSong = _playQueue.CurrentSong;
        System.Diagnostics.Debug.WriteLine($"[CatClaw] SyncWithQueue: CurrentSong={CurrentSong?.Title}(Id={CurrentSong?.Id}), queueSong={queueSong?.Title}(Id={queueSong?.Id})");
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
            byte[]? coverBytes = null;

            if (song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                coverBytes = ExtractCoverFromContentUri(song.FilePath);
            }

            Stream? stream = coverBytes != null
                ? new MemoryStream(coverBytes)
                : await _musicLibrary.GetAlbumCoverAsync(song);
            ct.ThrowIfCancellationRequested();

            if (stream == null && song.Source == SongSource.WebDAV)
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
                if (!_isSwitchingSong)
                    Next();
            }
            else if (e.State == PlaybackState.Playing)
            {
                _isSwitchingSong = false;
                // 检查当前显示的歌曲和实际播放的队列歌曲是否一致
                var queueSong = _playQueue.CurrentSong;
                if (queueSong != null && (CurrentSong == null || CurrentSong.Id != queueSong.Id))
                {
                    System.Diagnostics.Debug.WriteLine($"[CatClaw] Playing 同步: {queueSong.Title}(Id={queueSong.Id})");
                    CurrentSong = queueSong;
                    var ct = _songLoadCts?.Token ?? CancellationToken.None;
                    _ = LoadLyricsAsync(queueSong, ct);
                    _ = LoadCoverAsync(queueSong, ct);
                    UpdateQueuePeek();
                }
                // 记录此次成功播放的歌曲，取消待显示的报错对话框
                _lastActiveSong = CurrentSong;
                _errorDialogCts?.Cancel();
                _errorDialogCts = null;
                PlayPauseIcon = "⏸";
            }
            else if (e.State is PlaybackState.Paused or PlaybackState.Error)
            {
                _isSwitchingSong = false;
                PlayPauseIcon = "▶";
                // 播放失败时，如果当前显示的歌曲和实际播放的不一致，回退到上一首
                if (e.State == PlaybackState.Error)
                {
                    var actualPath = _audioPlayer.CurrentSongFilePath;
                    if (!string.IsNullOrEmpty(actualPath) && CurrentSong != null
                        && CurrentSong.FilePath != actualPath && _lastActiveSong != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CatClaw] 播放失败，回退到: {_lastActiveSong.Title}");
                        CurrentSong = _lastActiveSong;
                        // 不手动调用 LoadCover/LoadLyrics，OnCurrentSongChanged 会自动触发
                    }
                    if (!string.IsNullOrEmpty(e.ErrorMessage))
                        ShowErrorDialogDelayed(e.ErrorMessage);
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
            // 仅在播放中读取 Duration，避免切歌时的竞态触发原生 getDuration 错误
            if (_audioPlayer.IsPlaying)
                TotalDuration = _audioPlayer.Duration;
            if (CurrentLyrics?.Lines is { Count: > 0 })
            {
                var idx = _lyricsService.GetCurrentLyricIndex(CurrentLyrics, position);
                if (idx >= 0 && idx < CurrentLyrics.Lines.Count)
                {
                    PrevLyricLine2 = idx > 1 ? CurrentLyrics.Lines[idx - 2].Text : "";
                    PrevLyricLine = idx > 0 ? CurrentLyrics.Lines[idx - 1].Text : "";
                    NextLyricLine = idx + 1 < CurrentLyrics.Lines.Count ? CurrentLyrics.Lines[idx + 1].Text : "";
                    NextLyricLine2 = idx + 2 < CurrentLyrics.Lines.Count ? CurrentLyrics.Lines[idx + 2].Text : "";
                    CurrentLyricLine = CurrentLyrics.Lines[idx].Text; // 最后设，触发 UI 刷新
                    CurrentLyricIndex = idx;
                    UpdateLyricSpannable(); // 更新逐字高亮
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
        if (song == null) { CurrentLyricLine = "🐾 猫爪音乐"; NextLyricLine = "选择一首歌曲开始播放吧~"; PrevLyricLine2 = ""; PrevLyricLine = ""; NextLyricLine2 = ""; CurrentLyrics = null; return; }
        var songId = song.Id;
        CurrentLyricLine = "正在加载歌词..."; NextLyricLine = ""; PrevLyricLine2 = ""; PrevLyricLine = ""; NextLyricLine2 = "";
        CurrentLyrics = await _lyricsService.GetLyricsAsync(song);
        ct.ThrowIfCancellationRequested();

        if (CurrentSong?.Id != songId) return;

        if (CurrentLyrics == null && song.Source == SongSource.WebDAV)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] LoadLyrics: 网络歌曲, Source={song.Source}, 尝试远程歌词");
            CurrentLyrics = await GetNetworkLyricsAsync(song);
            ct.ThrowIfCancellationRequested();
            if (CurrentSong?.Id != songId) return;
        }
        else if (CurrentLyrics == null)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] LoadLyrics: 本地无歌词, Source={song.Source}, 跳过远程");
        }

        if (CurrentSong?.Id != songId) return;

        if (CurrentLyrics == null) { CurrentLyricLine = "暂无歌词"; NextLyricLine = ""; }
        else if (CurrentLyrics.Lines.Count > 0)
        {
            // 根据当前播放位置显示初始歌词行
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

        if (song.Source == SongSource.WebDAV
            && (song.Artist == "未知艺术家" || song.Album == "未知专辑" || song.Duration == 0)
            && _networkMusic != null
            && !_metadataFetchAttempted.Contains(song.Id))
        {
            _metadataFetchAttempted.Add(song.Id);
            var profile = await GetNetworkProfileAsync(ProtocolType.WebDAV);
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
        if (_networkMusic == null) return null;
        try
        {
            var profiles = await _networkMusic.GetProfilesAsync();
            _cachedNavidromeProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.Navidrome && p.IsEnabled);
            _cachedWebDavProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.WebDAV && p.IsEnabled);
            return protocol == ProtocolType.Navidrome ? _cachedNavidromeProfile : _cachedWebDavProfile;
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
        var protocol = IsNavidromeSong(song) ? ProtocolType.Navidrome : ProtocolType.WebDAV;
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
        var protocol = isNavidrome ? ProtocolType.Navidrome : ProtocolType.WebDAV;
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
