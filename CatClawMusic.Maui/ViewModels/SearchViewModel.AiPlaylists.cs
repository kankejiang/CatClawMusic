using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;

using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// AI 歌单域：由 AI 每天生成 5 个主题歌单（每个 20 首 + 推荐理由），
/// 磁盘缓存按天复用；未开启智能推荐、未配置模型或生成失败时保持为空（UI 隐藏）。
/// </summary>
public partial class SearchViewModel
{
    /// <summary>AI 歌单列表（5 张卡片的数据源）</summary>
    [ObservableProperty]
    private ObservableCollection<AiPlaylist> _aiPlaylists = new();

    /// <summary>AI 歌单生成中标记（卡片区可显示加载态）</summary>
    [ObservableProperty]
    private bool _isAiPlaylistsLoading;

    /// <summary>AI 歌单区是否显示：开启智能推荐 且（有数据 或 正在生成）；模型未配置/生成失败时隐藏</summary>
    public bool IsAiPlaylistsVisible => IsAiRecommendationEnabled && AiPlaylists.Count > 0;

    /// <summary>AI 歌单区整体可见（含生成中的加载态）：开启智能推荐 且（有数据 或 正在生成）</summary>
    public bool IsAiPlaylistsSectionVisible => IsAiRecommendationEnabled && (AiPlaylists.Count > 0 || IsAiPlaylistsLoading);

    /// <summary>生成状态变化时同步区块可见性（加载中也要显示区块以便提示）</summary>
    partial void OnIsAiPlaylistsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAiPlaylistsSectionVisible));
    }

    private readonly string _aiPlaylistsCacheFilePath = Path.Combine(FileSystem.AppDataDirectory, "cache", "ai_playlists.json");
    private string? _aiPlaylistsDate;          // 内存中的歌单日期
    private string? _aiPlaylistsAttemptDate;   // 当天已尝试拉取的标记（失败也标记，避免反复调用）
    private bool _aiPlaylistsFetching;
    private bool _aiPlaylistsLoaded;           // 本次会话是否已检查（每天只走一次完整流程）
    private int _aiPlaylistsRetryCount;        // 候选池空时的延迟重试次数

    /// <summary>
    /// 确保当天 AI 歌单就绪：内存 → 磁盘缓存 → 调用 AI（每天仅一次）。
    /// 未开启智能推荐或未配置模型时直接返回（保持空 → UI 不显示）。
    /// </summary>
    public async Task EnsureDailyAiPlaylistsAsync()
    {
        if (!IsAiRecommendationEnabled || !_agentService.IsConfigured)
        {
            return;
        }
        if (_aiPlaylistsLoaded)
        {
            return;
        }
        _aiPlaylistsLoaded = true;

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (_aiPlaylistsDate == today && AiPlaylists.Count > 0) return;
        if (_aiPlaylistsFetching) return;

        // 候选池为空（发现页数据尚未加载，如刚开启开关/启动初期）：
        // 不标记"当天已尝试"，延迟重试几次（数据通常 1~3 秒内加载完成）
        var pool = BuildAiCandidatePool();
        if (pool.Count == 0)
        {
            _aiPlaylistsLoaded = false;
            if (_aiPlaylistsRetryCount < 3)
            {
                _aiPlaylistsRetryCount++;
                _ = Task.Delay(3000).ContinueWith(_ =>
                    MainThread.BeginInvokeOnMainThread(() => _ = EnsureDailyAiPlaylistsAsync()));
            }
            return;
        }
        _aiPlaylistsRetryCount = 0;

        _aiPlaylistsFetching = true;
        try
        {
            // 磁盘命中（跨重启复用当天结果，不消耗 token）
            var disk = await LoadAiPlaylistsCacheAsync(today);
            if (disk != null && disk.Count > 0)
            {
                ApplyAiPlaylists(disk, today);
                return;
            }

            // 当天已尝试过且无结果，避免失败后反复调用
            if (_aiPlaylistsAttemptDate == today)
            {
                return;
            }

            // 调用 AI 生成当天歌单（每天仅一次）
            IsAiPlaylistsLoading = true;
            var batch = await FetchAiPlaylistsAsync();
            _aiPlaylistsAttemptDate = today;
            if (batch.Count > 0)
            {
                ApplyAiPlaylists(batch, today);
                await SaveAiPlaylistsCacheAsync(today, batch);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] AI 歌单获取失败: {ex.Message}");
            _aiPlaylistsAttemptDate = today;
        }
        finally
        {
            IsAiPlaylistsLoading = false;
            _aiPlaylistsFetching = false;
        }
    }

    /// <summary>把解析结果应用到 UI 集合并记录日期。</summary>
    private void ApplyAiPlaylists(List<AiPlaylist> playlists, string date)
    {
        _aiPlaylistsDate = date;
        AiPlaylists.Clear();
        foreach (var p in playlists) AiPlaylists.Add(p);
        OnPropertyChanged(nameof(IsAiPlaylistsVisible));
        OnPropertyChanged(nameof(IsAiPlaylistsSectionVisible));
    }

    /// <summary>向 AI 请求当天歌单：候选歌曲（含 ID）交给 AI，生成 5 个主题歌单（每歌单 20 首 + 推荐理由），
    /// 返回严格 JSON，随后按 ID 匹配回本地曲库，避免 AI 编造不存在的歌曲。
    /// 主题规则：①星期/周末 ②节日/节气/季节 + 当前时间段 ③常听偏好 ④音乐类型风格 ⑤自由发挥。</summary>
    private async Task<List<AiPlaylist>> FetchAiPlaylistsAsync()
    {
        // 候选池截断：推理模型会逐首分析候选歌曲（reasoning 占用大量 token），
        // 候选过多时 max_tokens 会在生成 content 前耗尽（finishReason=length、content 空）
        var pool = BuildAiCandidatePool().Take(25).ToList();
        if (pool.Count == 0) return new();

        var sb = new StringBuilder();
        foreach (var s in pool)
            sb.AppendLine($"{s.Id}. {s.Title ?? "未知"} - {s.Artist ?? "未知艺术家"}");

        // 当前时间上下文：星期/周末、季节、时间段（随当前时刻变化）
        var now = DateTime.Now;
        var weekDay = now.DayOfWeek switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            _ => "周日",
        };
        var isWeekend = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "周末" : "工作日";
        var season = now.Month switch
        {
            3 or 4 or 5 => "春季",
            6 or 7 or 8 => "夏季",
            9 or 10 or 11 => "秋季",
            _ => "冬季",
        };
        var period = now.Hour switch
        {
            >= 0 and < 6 => "凌晨",
            >= 6 and < 11 => "早上",
            >= 11 and < 14 => "中午",
            >= 14 and < 18 => "下午",
            >= 18 and < 21 => "傍晚",
            _ => "夜晚",
        };

        var systemPrompt = "你是Yuki，猫爪音乐的AI音乐推荐助手，说话温柔可爱带点喵口癖。";
        var userPrompt =
            $"今天是 {now:yyyy年M月d日}（{weekDay}，{isWeekend}），当前季节：{season}，当前时间段：{period}。\n" +
            $"下面是用户曲库里的候选歌曲（每行格式：ID. 歌名 - 艺术家）：\n{sb}\n" +
            "请按以下主题规则创建 5 个歌单：\n" +
            $"1. 第一张：以今天是{weekDay}（{isWeekend}）为主题（如{isWeekend}的放松时光、{weekDay}的能量补给）。\n" +
            "2. 第二张：先判断今天是否节日或节气（如有则以其为主题，没有则用当前季节），并紧密结合当前时间段（凌晨/早上/中午/下午/傍晚/夜晚）的听歌场景。\n" +
            "3. 第三张：基于用户常听的音乐偏好（从候选歌曲中推断最常见的口味/情绪/风格）。\n" +
            "4. 第四张：基于明确的音乐类型或风格（如流行、民谣、古风、电子、摇滚等）。\n" +
            "5. 第五张：自由发挥，创意不限。\n" +
            "每个歌单包含 20 首歌曲、一个歌单名和一句推荐理由（理由不超过20字，不要加引号）。\n" +
            "歌曲必须从上面的候选列表中选择，song_ids 里填歌曲 ID，不同歌单可以重复使用同一首歌。\n" +
            "重要：不要做任何逐步分析、解释或逐首歌点评，直接快速挑选并输出结果。\n" +
            "只返回严格的 JSON 数组，不要任何多余文字、代码块标记、换行或缩进（保持单行紧凑），格式：" +
            "[{\"name\":\"歌单名\",\"reason\":\"推荐理由\",\"song_ids\":[数字,...]}]";

        var raw = await _agentService.QuickAskAsync(systemPrompt, userPrompt);
        return ParseAiPlaylists(raw, pool);
    }

    /// <summary>解析 AI 返回的 JSON 歌单数组：按 ID 匹配候选池真实歌曲，丢弃空歌单与非法项。</summary>
    private static List<AiPlaylist> ParseAiPlaylists(string raw, List<Song> pool)
    {
        var result = new List<AiPlaylist>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result;
        }
        try
        {
            var start = raw.IndexOf('[');
            var end = raw.LastIndexOf(']');
            if (start < 0 || end <= start)
            {
                return result;
            }
            var json = raw.Substring(start, end - start + 1);

            // 反序列化（失败时尝试补全被截断的 JSON 尾部括号——模型输出超长时可能被 max_tokens 截断）
            List<AiPlaylist>? items = null;
            try
            {
                items = System.Text.Json.JsonSerializer.Deserialize<List<AiPlaylist>>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (System.Text.Json.JsonException)
            {
                foreach (var suffix in new[] { "]", "}]", "}}]", "}}}]" })
                {
                    try
                    {
                        items = System.Text.Json.JsonSerializer.Deserialize<List<AiPlaylist>>(json.TrimEnd() + suffix,
                            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (items != null) break;
                    }
                    catch { }
                }
            }
            if (items == null)
            {
                return result;
            }

            var validIds = pool.Select(s => s.Id).ToHashSet();
            var idToSong = pool.ToDictionary(s => s.Id);

            foreach (var p in items)
            {
                var name = p.Name?.Trim() ?? "";
                var reason = p.Reason?.Trim()?.Trim('"', '「', '」', '\n', '\r') ?? "";
                if (string.IsNullOrEmpty(name)) continue;
                if (reason.Length > 40) reason = reason.Substring(0, 40);

                // 按 ID 映射真实歌曲，去重
                var songs = new List<Song>();
                var used = new HashSet<int>();
                foreach (var id in p.SongIds)
                {
                    if (used.Contains(id)) continue;
                    used.Add(id);
                    if (validIds.Contains(id) && idToSong.TryGetValue(id, out var song))
                        songs.Add(song);
                }
                if (songs.Count == 0) continue; // 空歌单丢弃

                result.Add(new AiPlaylist { Name = name, Reason = reason, SongIds = p.SongIds, Songs = songs });
                if (result.Count >= 5) break; // 最多 5 张
            }
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] AI 歌单解析失败: {ex.Message}");
        }
        return result;
    }

    /// <summary>从磁盘读取当天的 AI 歌单缓存；日期不匹配或为空时返回 null</summary>
    private async Task<List<AiPlaylist>?> LoadAiPlaylistsCacheAsync(string date)
    {
        try
        {
            if (!File.Exists(_aiPlaylistsCacheFilePath)) return null;
            var json = await File.ReadAllTextAsync(_aiPlaylistsCacheFilePath);
            var cache = System.Text.Json.JsonSerializer.Deserialize<AiPlaylistsCache>(json);
            if (cache?.Date != date || cache.Playlists == null || cache.Playlists.Count == 0) return null;

            // 缓存只存 ID，需要按当前曲库重新映射（曲库变化时自动丢弃失效项）
            var pool = BuildAiCandidatePool();
            var idToSong = pool.ToDictionary(s => s.Id);
            var result = new List<AiPlaylist>();
            foreach (var p in cache.Playlists)
            {
                var songs = p.SongIds.Where(id => idToSong.ContainsKey(id)).Select(id => idToSong[id]).Distinct().ToList();
                if (songs.Count == 0) continue;
                result.Add(new AiPlaylist { Name = p.Name, Reason = p.Reason, SongIds = p.SongIds, Songs = songs });
            }
            return result;
        }
        catch { return null; }
    }

    /// <summary>将当天的 AI 歌单整批写入磁盘缓存（仅存 ID）</summary>
    private async Task SaveAiPlaylistsCacheAsync(string date, List<AiPlaylist> playlists)
    {
        try
        {
            var dir = Path.GetDirectoryName(_aiPlaylistsCacheFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var cache = new AiPlaylistsCache { Date = date, Playlists = playlists };
            await File.WriteAllTextAsync(_aiPlaylistsCacheFilePath, System.Text.Json.JsonSerializer.Serialize(cache));
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] AI 歌单缓存写入失败: {ex.Message}");
        }
    }

    /// <summary>使 AI 歌单缓存失效（扫描后调用）：只重置内存标记，保留当天磁盘缓存
    /// （Ensure 会先命中磁盘按当前曲库重新映射，避免扫描后重复调用 AI）。</summary>
    private void InvalidateAiPlaylistsCache()
    {
        _aiPlaylistsDate = null;
        _aiPlaylistsAttemptDate = null;
        _aiPlaylistsLoaded = false;
        AiPlaylists.Clear();
        OnPropertyChanged(nameof(IsAiPlaylistsVisible));
        OnPropertyChanged(nameof(IsAiPlaylistsSectionVisible));
    }
}
