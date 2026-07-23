using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>听歌统计页 ViewModel：基于 PlayHistory（累计次数）+ PlaySession（逐次日志）
/// 聚合展示总播放、听过歌曲、听歌天数、累计时长、Top10 歌曲、Top5 歌手、最近在听、
/// 听歌趋势（次数/时长切换 + 时间范围）、时段分布、连续听歌、环比对比等。</summary>
public partial class ListeningStatsViewModel : ObservableObject
{
    private readonly MusicDatabase _db;

    /// <summary>统计时间范围内的"第 0 天"（项目启动纪元，用于"陪伴你聆听 N 天"文案）</summary>
    private static readonly long _epoch = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    // 列表集合用 [ObservableProperty]：加载完成后整体替换实例（一次 PropertyChanged），
    // 避免 Clear + 逐条 Add 触发 N 次 CollectionChanged → CollectionView 连续 diff/布局
    [ObservableProperty] private ObservableCollection<TopSongItem> _topSongs = new();
    [ObservableProperty] private ObservableCollection<Song> _recentSongs = new();
    [ObservableProperty] private ObservableCollection<ArtistStatItem> _topArtists = new();
    [ObservableProperty] private ObservableCollection<TimeSlotItem> _timeSlots = new();
    [ObservableProperty] private ObservableCollection<CompareItem> _compareItems = new();
    // 趋势图柱子集合：图表视图直接订阅 CollectionChanged 手动建图，保持单实例（视图侧已去抖）
    public ObservableCollection<TrendBar> TrendBars { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _statusText = "";

    // 顶部统计卡
    [ObservableProperty] private string _totalPlaysText = "0";
    [ObservableProperty] private string _songsHeardText = "0";
    [ObservableProperty] private string _listeningDaysText = "0";
    [ObservableProperty] private string _companionDaysText = "0";

    // 听歌时长大卡
    [ObservableProperty] private string _totalDurationText = "0 分";
    [ObservableProperty] private string _avgDurationText = "0";
    [ObservableProperty] private string _longestSongText = "--:--";
    [ObservableProperty] private string _topSongTitle = "—";
    [ObservableProperty] private string _nightPercentText = "0";

    // 连续听歌
    [ObservableProperty] private string _streakCurrentText = "0";
    [ObservableProperty] private string _streakBestText = "0";
    [ObservableProperty] private string _streakCapText = "已坚持 0 天";

    // 趋势图
    [ObservableProperty] private string _trendCapText = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountMetricBgColor))]
    [NotifyPropertyChangedFor(nameof(CountMetricTextColor))]
    [NotifyPropertyChangedFor(nameof(DurationMetricBgColor))]
    [NotifyPropertyChangedFor(nameof(DurationMetricTextColor))]
    private bool _isCountMetric = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Range7BgColor))]
    [NotifyPropertyChangedFor(nameof(Range7TextColor))]
    private bool _isRange7 = false;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Range30BgColor))]
    [NotifyPropertyChangedFor(nameof(Range30TextColor))]
    private bool _isRange30 = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeAllBgColor))]
    [NotifyPropertyChangedFor(nameof(RangeAllTextColor))]
    private bool _isRangeAll = false;

    // Chips 选中颜色（直接绑定，避免 XAML 内联 Style + DataTrigger 解析问题）
    public Color Range7BgColor => IsRange7 ? PrimaryColor : Colors.Transparent;
    public Color Range7TextColor => IsRange7 ? Colors.White : TextSecondaryColor;
    public Color Range30BgColor => IsRange30 ? PrimaryColor : Colors.Transparent;
    public Color Range30TextColor => IsRange30 ? Colors.White : TextSecondaryColor;
    public Color RangeAllBgColor => IsRangeAll ? PrimaryColor : Colors.Transparent;
    public Color RangeAllTextColor => IsRangeAll ? Colors.White : TextSecondaryColor;
    public Color CountMetricBgColor => IsCountMetric ? PrimaryColor : Colors.Transparent;
    public Color CountMetricTextColor => IsCountMetric ? Colors.White : TextSecondaryColor;
    public Color DurationMetricBgColor => !IsCountMetric ? PrimaryColor : Colors.Transparent;
    public Color DurationMetricTextColor => !IsCountMetric ? Colors.White : TextSecondaryColor;

    private static Color PrimaryColor => Application.Current?.Resources?["PrimaryColor"] as Color ?? Color.FromArgb("#8C7BFF");
    private static Color TextSecondaryColor => Application.Current?.Resources?["TextSecondaryColor"] as Color ?? Colors.Gray;

    // 时段分布夜间提示
    [ObservableProperty] private string _nightNoteText = "";

    // 环比对比
    [ObservableProperty] private string _compareCapText = "近 30 天 vs 前 30 天";
    [ObservableProperty] private bool _hasCompare = true;

    // Top10 进度条最大值（用于相对宽度）
    [ObservableProperty] private int _topSongMax = 1;

    /// <summary>当前时间范围：7 / 30 / 0（0 表示全部）</summary>
    private int _currentRange = 30;

    public ListeningStatsViewModel(MusicDatabase db)
    {
        _db = db;
    }

    /// <summary>切换时间范围（7 / 30 / 0=全部），重新计算所有时间相关统计。</summary>
    /// <param name="range">7=近7天, 30=近30天, 0=全部</param>
    [RelayCommand]
    public async Task SwitchRangeAsync(int range)
    {
        _currentRange = range;
        IsRange7 = range == 7;
        IsRange30 = range == 30;
        IsRangeAll = range == 0;
        await LoadAsync();
    }

    /// <summary>切换趋势图指标：true=次数, false=时长。
    /// 聚合在后台线程完成，仅最终赋值回 UI 线程，避免大范围（全部）下对上万条会话做
    /// LINQ 时阻塞主线程导致 ANR（发现页切次数时主线程卡死）。</summary>
    [RelayCommand]
    public async Task SwitchMetricAsync(bool isCount)
    {
        IsCountMetric = isCount; // 命令起始于 UI 线程，指标 chip 颜色即时切换
        var sessions = _lastSessions;
        if (sessions == null) return;
        var (bars, cap) = await Task.Run(() => BuildTrendBars(sessions, isCount, _currentRange))
            .ConfigureAwait(false);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            TrendCapText = cap;
            TrendBars.Clear();
            foreach (var b in bars) TrendBars.Add(b);
        }).ConfigureAwait(false);
    }

    /// <summary>加载统计数据（含趋势/时段/环比等所有区块）。
    /// 所有统计 LINQ（可能遍历上万条会话）在后台线程执行，仅最后一步把结果一次性回 UI 线程赋值，
    /// 避免切换范围/次数时主线程被阻塞触发 ANR。</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusText = "加载中…";
        try
        {
            // 并行拉取所有需要的数据（DB 查询本身在后台线程）
            var recentPlaysTask = _db.GetRecentPlaysAsync(200);
            var topSongsTask = _db.GetTopPlayedSongsAsync(10);
            var recentSongsTask = _db.GetRecentSongsAsync();
            var topArtistsTask = _db.GetTopPlayedArtistsAsync(5);
            var sessionsTask = LoadSessionsForCurrentRangeAsync();

            await Task.WhenAll(recentPlaysTask, topSongsTask, recentSongsTask, topArtistsTask, sessionsTask)
                .ConfigureAwait(false);

            var recentPlays = recentPlaysTask.Result;
            var topSongs = topSongsTask.Result;
            var recentSongs = recentSongsTask.Result;
            var topArtists = topArtistsTask.Result;
            var sessions = sessionsTask.Result;

            // —— 所有 CPU 密集聚合在后台线程完成 ——
            var result = await Task.Run(async () =>
            {
                var r = new StatsResult();
                r.HasData = recentPlays.Count > 0;

                ComputeSummary(r, sessions, topSongs, recentSongs);
                RenderTrendChart(r, sessions);
                RenderTimeSlots(r, sessions);
                ComputeStreak(r, sessions);
                await RenderCompareAsync(r, sessions);

                var maxPlays = topSongs.Count > 0 ? topSongs.Max(s => s.PlayCount) : 1;
                r.TopSongMax = Math.Max(1, maxPlays);
                r.TopSongs = topSongs.Select((s, i) => new TopSongItem(s, i + 1, r.TopSongMax)).ToList();
                r.RecentSongs = recentSongs.Take(10).ToList();
                r.TopArtists = topArtists.Select(a => new ArtistStatItem(a.Artist, a.PlayCount)).ToList();

                r.StatusText = r.HasData ? "" : "还没有听歌记录，去发现页听听吧";
                return r;
            }).ConfigureAwait(false);

            // —— 一次性回 UI 线程赋值（触发绑定刷新 / CollectionView 重绑）——
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ApplyResult(result);
                IsLoading = false;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug("ListeningStatsViewModel", $"[ListeningStatsVM] LoadAsync 异常: {ex}");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StatusText = "加载失败：" + ex.Message;
                IsLoading = false;
            }).ConfigureAwait(false);
        }
    }

    /// <summary>后台计算结果的容器：所有统计字段集中此处，避免在后台线程触碰绑定属性。</summary>
    private sealed class StatsResult
    {
        public string TotalPlaysText = "0";
        public string SongsHeardText = "0";
        public string ListeningDaysText = "0";
        public string CompanionDaysText = "0";
        public string TotalDurationText = "0 分";
        public string AvgDurationText = "0";
        public string LongestSongText = "--:--";
        public string TopSongTitle = "—";
        public string NightPercentText = "0";
        public string TrendCapText = "";
        public List<TrendBar> TrendBars = new();
        public List<TimeSlotItem> TimeSlots = new();
        public string NightNoteText = "";
        public string StreakCurrentText = "0";
        public string StreakBestText = "0";
        public string StreakCapText = "0";
        public List<CompareItem> CompareItems = new();
        public string CompareCapText = "";
        public bool HasCompare = true;
        public int TopSongMax = 1;
        public List<TopSongItem> TopSongs = new();
        public List<Song> RecentSongs = new();
        public List<ArtistStatItem> TopArtists = new();
        public bool HasData;
        public string StatusText = "";
    }

    /// <summary>把后台计算结果一次性赋给绑定属性（必须在 UI 线程调用）。</summary>
    private void ApplyResult(StatsResult r)
    {
        TotalPlaysText = r.TotalPlaysText;
        SongsHeardText = r.SongsHeardText;
        ListeningDaysText = r.ListeningDaysText;
        CompanionDaysText = r.CompanionDaysText;
        TotalDurationText = r.TotalDurationText;
        AvgDurationText = r.AvgDurationText;
        LongestSongText = r.LongestSongText;
        TopSongTitle = r.TopSongTitle;
        NightPercentText = r.NightPercentText;
        TrendCapText = r.TrendCapText;
        TrendBars.Clear();
        foreach (var b in r.TrendBars) TrendBars.Add(b);
        TimeSlots = new ObservableCollection<TimeSlotItem>(r.TimeSlots);
        NightNoteText = r.NightNoteText;
        StreakCurrentText = r.StreakCurrentText;
        StreakBestText = r.StreakBestText;
        StreakCapText = r.StreakCapText;
        CompareItems = new ObservableCollection<CompareItem>(r.CompareItems);
        CompareCapText = r.CompareCapText;
        HasCompare = r.HasCompare;
        TopSongMax = r.TopSongMax;
        TopSongs = new ObservableCollection<TopSongItem>(r.TopSongs);
        RecentSongs = new ObservableCollection<Song>(r.RecentSongs);
        TopArtists = new ObservableCollection<ArtistStatItem>(r.TopArtists);
        HasData = r.HasData;
        StatusText = r.StatusText;
    }

    /// <summary>根据当前时间范围拉取播放会话列表。</summary>
    private async Task<List<PlaySession>> LoadSessionsForCurrentRangeAsync()
    {
        if (_currentRange <= 0)
        {
            return await _db.GetAllPlaySessionsAsync();
        }
        var since = DateTimeOffset.UtcNow.AddDays(-_currentRange).ToUnixTimeSeconds();
        return await _db.GetPlaySessionsAsync(since);
    }

    /// <summary>计算顶部统计卡 + 听歌时长大卡（写入 StatsResult，不触碰绑定属性）。</summary>
    private void ComputeSummary(StatsResult r, List<PlaySession> sessions, List<Song> topSongs, List<Song> recentSongs)
    {
        // 总播放次数 = 会话条数；听过歌曲数 = 不同 SongId 数；听歌天数 = 不同日期数
        var totalPlays = sessions.Count;
        var songsHeard = sessions.Select(s => s.SongId).Distinct().Count();
        var listeningDays = sessions
            .Select(s => DateTimeOffset.FromUnixTimeSeconds(s.PlayedAt).LocalDateTime.Date)
            .Distinct().Count();
        var now = DateTimeOffset.Now.ToUnixTimeSeconds();
        var companionDays = (int)Math.Max(1, Math.Round((now - _epoch) / 86400.0));

        r.TotalPlaysText = totalPlays.ToString("N0");
        r.SongsHeardText = songsHeard.ToString("N0");
        r.ListeningDaysText = listeningDays.ToString("N0");
        r.CompanionDaysText = companionDays.ToString("N0");

        // 累计听歌时长 = Σ session.DurationMs
        var totalMs = sessions.Sum(s => s.DurationMs);
        var totalMinutes = totalMs / 60000.0;
        r.TotalDurationText = FormatDuration(totalMinutes);
        r.AvgDurationText = listeningDays > 0 ? Math.Round(totalMinutes / listeningDays).ToString("N0") : "0";

        // 最长单曲：从 topSongs + recentSongs 并集取最长 Duration
        var songById = new Dictionary<int, Song>();
        foreach (var s in topSongs) songById[s.Id] = s;
        foreach (var s in recentSongs) songById.TryAdd(s.Id, s);
        int maxDurationSec = 0;
        string longestDisplay = "--:--";
        foreach (var s in songById.Values)
        {
            var sec = s.Duration / 1000;
            if (sec > maxDurationSec)
            {
                maxDurationSec = sec;
                longestDisplay = $"{sec / 60}:{sec % 60:D2}";
            }
        }
        r.LongestSongText = longestDisplay;
        r.TopSongTitle = topSongs.Count > 0 ? topSongs[0].Title : "—";

        // 深夜听歌占比：21:00-05:00 的会话数 / 总会话数
        var nightCount = sessions.Count(s =>
        {
            var hour = DateTimeOffset.FromUnixTimeSeconds(s.PlayedAt).LocalDateTime.Hour;
            return hour >= 21 || hour < 5;
        });
        var nightPct = totalPlays > 0 ? (int)Math.Round(nightCount * 100.0 / totalPlays) : 0;
        r.NightPercentText = nightPct.ToString();
    }

    /// <summary>纯函数：按当前指标/范围聚合趋势柱子，不触碰 UI 绑定（可在后台线程调用）。</summary>
    private (List<TrendBar> Bars, string CapText) BuildTrendBars(List<PlaySession> sessions, bool isCount, int range)
    {
        var bars = new List<TrendBar>();
        if (sessions.Count == 0)
            return (bars, "当前范围暂无播放记录");

        // 按时间范围决定聚合粒度：7 天→按日；30 天→按日；全部→按月
        List<(string Label, int Count, long Ms)> buckets;
        string capPrefix;
        if (range == 7)
        {
            buckets = AggregateByDay(sessions, 7);
            capPrefix = "近 7 天";
        }
        else if (range == 30)
        {
            buckets = AggregateByDay(sessions, 30);
            capPrefix = "近 30 天";
        }
        else
        {
            buckets = AggregateByMonth(sessions);
            capPrefix = "全部时间";
        }

        var maxCount = buckets.Max(b => b.Count);
        var maxMs = buckets.Max(b => b.Ms);
        var maxVal = isCount ? Math.Max(1, maxCount) : Math.Max(1L, maxMs);
        var activeDays = buckets.Count(b => b.Count > 0);

        foreach (var b in buckets)
        {
            var rawVal = isCount ? b.Count : b.Ms;
            var ratio = maxVal > 0 ? (double)rawVal / maxVal : 0;
            var display = isCount
                ? (b.Count > 0 ? b.Count.ToString() : "")
                : (b.Ms > 0 ? FormatDuration(b.Ms / 60000.0) : "");
            bars.Add(new TrendBar(b.Label, ratio, display));
        }

        var cap = isCount
            ? $"{capPrefix} · 有 {activeDays} 天在听歌"
            : $"{capPrefix} · 共 {FormatDuration(sessions.Sum(s => s.DurationMs) / 60000.0)}";
        return (bars, cap);
    }

    /// <summary>为 LoadAsync 在后台线程填充趋势结果。</summary>
    private void RenderTrendChart(StatsResult r, List<PlaySession> sessions)
    {
        _lastSessions = sessions;
        var (bars, cap) = BuildTrendBars(sessions, IsCountMetric, _currentRange);
        r.TrendBars = bars;
        r.TrendCapText = cap;
    }

    private List<PlaySession>? _lastSessions;

    /// <summary>按日聚合：返回最近 N 天每天的播放次数与时长。
    /// 标签规则：7 天显示"月/日"（全部显示）；30 天显示"日"（每 5 天一个标签，对齐原型 1/6/11/16/21/26）。</summary>
    private static List<(string Label, int Count, long Ms)> AggregateByDay(List<PlaySession> sessions, int days)
    {
        var result = new List<(string, int, long)>();
        var today = DateTime.Today;
        var byDate = sessions
            .GroupBy(s => DateTimeOffset.FromUnixTimeSeconds(s.PlayedAt).LocalDateTime.Date)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Ms: g.Sum(s => s.DurationMs)));

        for (int i = days - 1; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            byDate.TryGetValue(date, out var v);
            string label;
            if (days == 7)
            {
                label = $"{date.Month}/{date.Day}";
            }
            else
            {
                // 30 天：日期号模 5 余 1 时显示（1,6,11,16,21,26），对齐原型
                label = (date.Day % 5 == 1) ? $"{date.Day}" : "";
            }
            result.Add((label, v.Count, v.Ms));
        }
        return result;
    }

    /// <summary>按月聚合：返回所有会话覆盖的每个月（用于"全部"范围）。</summary>
    private static List<(string Label, int Count, long Ms)> AggregateByMonth(List<PlaySession> sessions)
    {
        var byMonth = sessions
            .GroupBy(s =>
            {
                var d = DateTimeOffset.FromUnixTimeSeconds(s.PlayedAt).LocalDateTime;
                return new DateTime(d.Year, d.Month, 1);
            })
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Ms: g.Sum(s => s.DurationMs)));

        var result = new List<(string, int, long)>();
        if (byMonth.Count == 0) return result;
        var start = byMonth.Keys.Min();
        var end = byMonth.Keys.Max();
        var cur = start;
        while (cur <= end)
        {
            byMonth.TryGetValue(cur, out var v);
            result.Add(($"{cur.Month}月", v.Count, v.Ms));
            cur = cur.AddMonths(1);
        }
        return result;
    }

    /// <summary>渲染时段分布：6 个固定时段（写入 StatsResult）。</summary>
    private void RenderTimeSlots(StatsResult r, List<PlaySession> sessions)
    {
        if (sessions.Count == 0)
        {
            r.TimeSlots = new List<TimeSlotItem>();
            r.NightNoteText = "";
            return;
        }

        // 时段定义：(名称, 起始小时, 结束小时)
        var slots = new[]
        {
            ("清晨 5–9", 5, 9),
            ("上午 9–12", 9, 12),
            ("午后 12–17", 12, 17),
            ("傍晚 17–21", 17, 21),
            ("夜晚 21–24", 21, 24),
            ("凌晨 0–5", 0, 5),
        };

        var total = sessions.Count;
        var counts = new int[slots.Length];
        foreach (var s in sessions)
        {
            var hour = DateTimeOffset.FromUnixTimeSeconds(s.PlayedAt).LocalDateTime.Hour;
            for (int i = 0; i < slots.Length; i++)
            {
                if (hour >= slots[i].Item2 && hour < slots[i].Item3)
                {
                    counts[i]++;
                    break;
                }
            }
        }

        var maxCount = counts.Max();
        var items = new List<TimeSlotItem>(slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            var pct = total > 0 ? (int)Math.Round(counts[i] * 100.0 / total) : 0;
            var ratio = maxCount > 0 ? (double)counts[i] / maxCount : 0;
            items.Add(new TimeSlotItem(slots[i].Item1, pct, ratio));
        }
        r.TimeSlots = items;

        var nightPct = total > 0 ? (int)Math.Round((counts[4] + counts[5]) * 100.0 / total) : 0;
        r.NightNoteText = nightPct >= 30
            ? $"🌙 深夜 + 凌晨共占 {nightPct}%，你是个不折不扣的夜猫子"
            : nightPct >= 15
                ? $"🌙 深夜 + 凌晨共占 {nightPct}%，偶尔也会熬夜听歌"
                : $"☀️ 深夜 + 凌晨共占 {nightPct}%，你的作息很健康";
    }

    /// <summary>计算连续听歌（当前连续 + 最长连续，按天）。</summary>
    private void ComputeStreak(StatsResult r, List<PlaySession> sessions)
    {
        if (sessions.Count == 0)
        {
            r.StreakCurrentText = "0";
            r.StreakBestText = "0";
            r.StreakCapText = "还未开始听歌";
            return;
        }

        var dates = sessions
            .Select(s => DateTimeOffset.FromUnixTimeSeconds(s.PlayedAt).LocalDateTime.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        // 当前连续：从今天（或昨天）向前数
        var today = DateTime.Today;
        var cur = 0;
        if (dates.Contains(today))
        {
            cur = 1;
            var d = today.AddDays(-1);
            while (dates.Contains(d)) { cur++; d = d.AddDays(-1); }
        }
        else if (dates.Contains(today.AddDays(-1)))
        {
            cur = 1;
            var d = today.AddDays(-2);
            while (dates.Contains(d)) { cur++; d = d.AddDays(-1); }
        }

        // 最长连续
        var best = 1;
        var run = 1;
        for (int i = 1; i < dates.Count; i++)
        {
            if (dates[i] == dates[i - 1].AddDays(1)) run++;
            else run = 1;
            if (run > best) best = run;
        }

        r.StreakCurrentText = cur.ToString();
        r.StreakBestText = best.ToString();
        r.StreakCapText = cur > 0 ? $"已坚持 {cur} 天" : "今天还没听歌";
    }

    /// <summary>渲染环比对比：当前范围 vs 上一周期（仅 7/30 支持，全部无对比）。</summary>
    private async Task RenderCompareAsync(StatsResult r, List<PlaySession> sessions)
    {
        r.CompareItems = new List<CompareItem>();
        if (_currentRange == 0)
        {
            r.HasCompare = false;
            r.CompareCapText = "全部时间 · 无对比";
            return;
        }
        r.HasCompare = true;
        r.CompareCapText = $"近 {_currentRange} 天 vs 前 {_currentRange} 天";

        // 上一周期数据
        var now = DateTimeOffset.UtcNow;
        var prevSince = now.AddDays(-_currentRange * 2).ToUnixTimeSeconds();
        var prevUntil = now.AddDays(-_currentRange).ToUnixTimeSeconds();
        var prevSessions = (await _db.GetPlaySessionsAsync(prevSince, 10000))
            .Where(s => s.PlayedAt < prevUntil).ToList();

        var curSessions = sessions;

        var curPlays = curSessions.Count;
        var prevPlays = prevSessions.Count;
        var curDays = curSessions.Select(s => DateTimeOffset.FromUnixTimeSeconds(s.PlayedAt).LocalDateTime.Date).Distinct().Count();
        var prevDays = prevSessions.Select(s => DateTimeOffset.FromUnixTimeSeconds(s.PlayedAt).LocalDateTime.Date).Distinct().Count();
        var curMs = curSessions.Sum(s => s.DurationMs);
        var prevMs = prevSessions.Sum(s => s.DurationMs);

        r.CompareItems.Add(new CompareItem("播放次数", curPlays.ToString("N0"), DeltaPct(curPlays, prevPlays)));
        r.CompareItems.Add(new CompareItem("听歌时长", FormatDuration(curMs / 60000.0), DeltaPct(curMs, prevMs)));
        r.CompareItems.Add(new CompareItem("听歌天数", curDays.ToString(), DeltaPct(curDays, prevDays)));
    }

    /// <summary>计算环比百分比（+X% / -X% / 持平 / —）。</summary>
    private static (int Delta, string Text, string Kind) DeltaPct(long cur, long prev)
    {
        if (prev == 0 && cur == 0) return (0, "持平", "flat");
        if (prev == 0) return (100, "新增", "up");
        var pct = (int)Math.Round((cur - prev) * 100.0 / prev);
        if (pct == 0) return (0, "持平", "flat");
        var arrow = pct > 0 ? "▲" : "▼";
        return (pct, $"{arrow} {Math.Abs(pct)}%", pct > 0 ? "up" : "down");
    }

    /// <summary>分钟数 → 可读时长（"X 天 Y 小时" / "X 小时 Y 分" / "X 分"）</summary>
    private static string FormatDuration(double minutes)
    {
        var min = Math.Round(minutes);
        if (min >= 1440)
        {
            var d = (int)(min / 1440);
            var h = (int)Math.Round((min - d * 1440) / 60.0);
            return h > 0 ? $"{d} 天 {h} 小时" : $"{d} 天";
        }
        if (min >= 60)
        {
            var h = (int)(min / 60);
            var m = (int)Math.Round(min - h * 60);
            return m > 0 ? $"{h} 小时 {m} 分" : $"{h} 小时";
        }
        return $"{min} 分";
    }
}

/// <summary>趋势图柱子：标签 + 高度比例（0~1）+ 数值文本。</summary>
public sealed class TrendBar
{
    public string Label { get; }
    public double Ratio { get; }
    /// <summary>柱高数值（0~100 的 double，用于 HeightRequest 绑定）。</summary>
    public double HeightValue => Math.Round(Ratio * 100, 1);
    public string ValueText { get; }
    public bool HasValue => !string.IsNullOrEmpty(ValueText);

    public TrendBar(string label, double ratio, string valueText)
    {
        Label = label;
        Ratio = Math.Clamp(ratio, 0, 1);
        ValueText = valueText;
    }
}

/// <summary>时段分布项：时段名 + 占比百分比 + 相对最大值的进度比例（0~1，用于 ProgressBar.Progress）。</summary>
public sealed class TimeSlotItem
{
    public string Name { get; }
    public int Percent { get; }
    public string PercentText => Percent + "%";
    public double Ratio { get; }

    public TimeSlotItem(string name, int percent, double ratio)
    {
        Name = name;
        Percent = percent;
        Ratio = Math.Clamp(ratio, 0, 1);
    }
}

/// <summary>环比对比项：指标名 + 当前值 + 增量文本 + up/down/flat 状态 + 增量颜色。</summary>
public sealed class CompareItem
{
    public string Label { get; }
    public string Value { get; }
    public string DeltaText { get; }
    public string Kind { get; } // "up" / "down" / "flat"
    public bool IsUp => Kind == "up";
    public bool IsDown => Kind == "down";
    public bool IsFlat => Kind == "flat";
    /// <summary>增量颜色：up=薄荷绿, down=粉红, flat=灰</summary>
    public Color DeltaColor => Kind switch
    {
        "up" => Color.FromArgb("#5EEAD4"),
        "down" => Color.FromArgb("#FF9AAE"),
        _ => Color.FromArgb("#8D93B7"),
    };

    public CompareItem(string label, string value, (int Delta, string Text, string Kind) delta)
    {
        Label = label;
        Value = value;
        DeltaText = delta.Text;
        Kind = delta.Kind;
    }
}

/// <summary>歌手统计项（名称 + 播放次数）</summary>
public sealed class ArtistStatItem
{
    // 头像背景色板（与原型 CSS 渐变色首色对齐，按名称 hash 取色，保证稳定）
    private static readonly Color[] _palette =
    {
        Color.FromArgb("#8C7BFF"), Color.FromArgb("#FF7AAE"), Color.FromArgb("#55D6FF"),
        Color.FromArgb("#A78BFA"), Color.FromArgb("#5EEAD4"), Color.FromArgb("#FCA5A5"),
        Color.FromArgb("#818CF8"), Color.FromArgb("#38BDF8"),
    };

    public string Name { get; }
    public int PlayCount { get; }
    public string PlayCountText => PlayCount.ToString("N0") + " 次";
    /// <summary>首字母（用于头像占位）</summary>
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "♪" : Name.Trim()[0].ToString().ToUpper();
    /// <summary>头像背景色（按名称 hash 从色板取色，保证同一歌手颜色稳定）</summary>
    public Color AvatarColor
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return _palette[0];
            var hash = 0;
            foreach (var c in Name) hash = (hash * 31 + c) & 0x7FFFFFFF;
            return _palette[hash % _palette.Length];
        }
    }

    public ArtistStatItem(string name, int playCount)
    {
        Name = name;
        PlayCount = playCount;
    }
}

/// <summary>Top 歌曲统计项：包装 Song 并暴露排名、播放次数文本、相对进度比例（0~1）</summary>
public sealed class TopSongItem
{
    public Song Song { get; }
    public int Rank { get; }
    public string RankText => Rank.ToString();
    public string Title => Song.Title;
    public string Artist => string.IsNullOrWhiteSpace(Song.AllArtists) ? (Song.Artist ?? "未知艺术家") : Song.AllArtists;
    public string? CoverArtPath => Song.CoverArtPath;
    public int PlayCount => Song.PlayCount;
    public string PlayCountText => PlayCount.ToString("N0") + " 次";
    /// <summary>相对最大播放次数的进度比例（0~1），用于进度条</summary>
    public double ProgressRatio => TopSongMax <= 0 ? 0 : Math.Clamp((double)PlayCount / TopSongMax, 0, 1);

    private readonly int TopSongMax;

    public TopSongItem(Song song, int rank, int topSongMax)
    {
        Song = song;
        Rank = rank;
        TopSongMax = topSongMax;
    }
}
