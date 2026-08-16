using CatClawMusic.Core.Models;
using CatClawMusic.Data;

using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>AI 每日推荐域：候选池构建 / 批量拉取 / 磁盘缓存。</summary>
public partial class SearchViewModel
{
    private Song? PickAiRecommendedSong()
    {
        var candidates = new List<Song>();

        // 常听歌曲第2-10首（排除榜首，避免和"热门歌曲"重复）
        if (_allTopPlayedSongs.Count > 1)
            candidates.AddRange(_allTopPlayedSongs.Skip(1).Take(9));

        // 收藏中随机
        if (FavoriteSongs.Count > 0)
            candidates.AddRange(FavoriteSongs);

        // 每日推荐随机3首
        if (_allDailyRecommendSongs.Count > 0)
        {
            var random = new Random();
            candidates.AddRange(_allDailyRecommendSongs.OrderBy(_ => random.Next()).Take(3));
        }

        if (candidates.Count == 0)
        {
            // 回退：取每日推荐第一首
            return _allDailyRecommendSongs.FirstOrDefault();
        }

        // 去重并随机选一首
        var unique = candidates.GroupBy(s => s.Id).Select(g => g.First()).ToList();
        var rng = new Random();
        return unique[rng.Next(unique.Count)];
    }

    /// <summary>
    /// 确保当天的 AI 推荐批次已就绪：内存缓存 → 磁盘缓存 → 调用 AI（每天仅一次）。
    /// 命中缓存则不消耗 token；调用完成后可选地重新生成 Hero 卡以刷新展示。
    /// </summary>
    private async Task EnsureDailyAiRecommendationsAsync(bool regenerateAfter = false)
    {
        if (!IsAiRecommendationEnabled || !_agentService.IsConfigured) return;
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        // 内存命中
        if (_aiRecommendBatchDate == today && _aiRecommendBatch.Count > 0) return;
        if (_aiFetchInProgress) return;

        _aiFetchInProgress = true;
        var calledAi = false;
        try
        {
            // 磁盘命中（跨重启复用当天结果）
            var disk = await LoadAiCacheFromDiskAsync(today);
            if (disk != null && disk.Count > 0)
            {
                _aiRecommendBatch = disk;
                _aiRecommendBatchDate = today;
                _aiAttemptDate = today;
                return;
            }

            // 今天已尝试过且无缓存，避免失败后反复调用
            if (_aiAttemptDate == today) return;

            // 调用 AI 获取当天全部推荐（每天仅一次）
            calledAi = true;
            IsAiRecommending = true;
            if (regenerateAfter) MainThread.BeginInvokeOnMainThread(GenerateHeroCards);

            var batch = await FetchAiRecommendationBatchAsync();
            _aiAttemptDate = today;
            if (batch.Count > 0)
            {
                _aiRecommendBatch = batch;
                _aiRecommendBatchDate = today;
                // 用第一条推荐理由更新默认文案（占位卡也能显示合理文字）
                var firstReason = batch[0].Reason;
                if (!string.IsNullOrWhiteSpace(firstReason)) AiRecommendReason = firstReason;
                await SaveAiCacheToDiskAsync(today, batch);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] AI 每日推荐获取失败: {ex.Message}");
            _aiAttemptDate = today;
        }
        finally
        {
            if (calledAi) IsAiRecommending = false;
            _aiFetchInProgress = false;
            if (regenerateAfter) MainThread.BeginInvokeOnMainThread(GenerateHeroCards);
        }
    }

    /// <summary>
    /// 向 AI 请求当天推荐批次：把用户曲库中的候选歌（含 ID）交给 AI，让它挑选若干首并给出理由，
    /// 返回严格 JSON，随后按 ID 匹配回本地曲库，避免 AI 编造不存在的歌曲。
    /// </summary>
    private async Task<List<AiRecItem>> FetchAiRecommendationBatchAsync()
    {
        var pool = BuildAiCandidatePool();
        if (pool.Count == 0) return new();

        var sb = new StringBuilder();
        // 候选行用连续序号（1-N）而非数据库主键 ID：大数字 ID 模型易写错，解析时按序号映射
        for (int i = 0; i < pool.Count; i++)
        {
            var s = pool[i];
            sb.AppendLine($"{i + 1}. {s.Title ?? "未知"} - {s.Artist ?? "未知艺术家"}（{GuessSongLanguage(s)}）");
        }

        var count = Math.Min(8, pool.Count);
        var systemPrompt = "你是Yuki，猫爪音乐的AI音乐推荐助手，说话温柔可爱带点喵口癖。";
        var userPrompt =
            $"下面是用户曲库里的候选歌曲（每行格式：序号. 歌名 - 艺术家）：\n{sb}\n" +
            $"请从这些候选里挑选 {count} 首你最想推荐给用户的歌，为每首写一句温柔的推荐理由（不超过18字，不要加引号）。\n" +
            "只返回严格的 JSON 数组，不要任何多余文字或代码块标记，格式：[{\"id\":序号,\"reason\":\"理由\"}]";

        var raw = await _agentService.QuickAskAsync(systemPrompt, userPrompt);
        return ParseAiBatch(raw, pool);
    }

    /// <summary>
    /// 构建 AI 推荐候选池：个性化（常听/收藏/每日推荐）+ 全库均匀采样，
    /// 两者交替混合——避免无播放数据时 AI 只从收藏里挑歌，保证覆盖整个音乐库。
    /// </summary>
    private List<Song> BuildAiCandidatePool()
    {
        var personalized = new List<Song>();
        if (_allTopPlayedSongs.Count > 0) personalized.AddRange(_allTopPlayedSongs.Take(15));
        if (FavoriteSongs.Count > 0) personalized.AddRange(FavoriteSongs.Take(15));
        if (_allDailyRecommendSongs.Count > 0) personalized.AddRange(_allDailyRecommendSongs.Take(20));
        personalized = personalized.GroupBy(s => s.Id).Select(g => g.First()).ToList();

        // 全库均匀采样（本地+网络已过滤的全量列表；未加载完时跳过，仅个性化也够用）
        var librarySample = new List<Song>();
        try
        {
            var all = _allSongsForSearch;
            if (all is { Count: > 0 })
            {
                var existingIds = personalized.Select(s => s.Id).ToHashSet();
                var lib = all.Where(s => !existingIds.Contains(s.Id)).ToList();
                var target = Math.Max(0, 60 - personalized.Count);
                if (target > 0 && lib.Count > 0)
                {
                    var step = Math.Max(1, lib.Count / target);
                    for (int i = 0; i < lib.Count && librarySample.Count < target; i += step)
                        librarySample.Add(lib[i]);
                }
            }
        }
        catch { /* 全库采样失败不影响个性化候选 */ }

        // 交替混合：个性化与全库采样间隔排列 → AI 歌单 Take(25) 时自然混合两类来源
        var mixed = new List<Song>(60);
        int pi = 0, li = 0;
        while (mixed.Count < 60 && (pi < personalized.Count || li < librarySample.Count))
        {
            if (pi < personalized.Count) mixed.Add(personalized[pi++]);
            if (li < librarySample.Count && mixed.Count < 60) mixed.Add(librarySample[li++]);
        }
        return mixed;
    }

    /// <summary>解析 AI 返回的 JSON 推荐数组，仅保留候选池中真实存在的歌曲（id 为候选序号 1-N）</summary>
    private static List<AiRecItem> ParseAiBatch(string raw, List<Song> pool)
    {
        var result = new List<AiRecItem>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        try
        {
            var start = raw.IndexOf('[');
            var end = raw.LastIndexOf(']');
            if (start < 0 || end <= start) return result;
            var json = raw.Substring(start, end - start + 1);

            var items = System.Text.Json.JsonSerializer.Deserialize<List<AiRecItem>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (items == null) return result;

            var usedIndex = new HashSet<int>();
            foreach (var it in items)
            {
                var idx = it.SongId - 1; // 候选序号 1-based
                if (idx < 0 || idx >= pool.Count) continue;
                if (!usedIndex.Add(idx)) continue; // 去重
                var reason = it.Reason?.Trim()?.Trim('"', '「', '」', '\n', '\r') ?? "";
                if (reason.Length > 40) reason = reason.Substring(0, 40);
                result.Add(new AiRecItem { SongId = idx + 1, Reason = reason });
            }
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] AI 推荐解析失败: {ex.Message}");
        }
        return result;
    }

    /// <summary>按候选序号（1-based）从候选池解析出 Song 对象</summary>
    private Song? ResolveSongById(int seq)
    {
        var idx = seq - 1;
        if (idx < 0) return null;
        var pool = BuildAiCandidatePool();
        return idx < pool.Count ? pool[idx] : null;
    }

    /// <summary>从磁盘读取当天的 AI 推荐缓存；日期不匹配或为空时返回 null</summary>
    private async Task<List<AiRecItem>?> LoadAiCacheFromDiskAsync(string date)
    {
        try
        {
            if (!File.Exists(_aiCacheFilePath)) return null;
            var json = await File.ReadAllTextAsync(_aiCacheFilePath);
            var cache = System.Text.Json.JsonSerializer.Deserialize<AiRecCache>(json);
            if (cache?.Date != date || cache.Items == null || cache.Items.Count == 0) return null;
            return cache.Items;
        }
        catch { return null; }
    }

    /// <summary>将当天的 AI 推荐批次整批写入磁盘缓存</summary>
    private async Task SaveAiCacheToDiskAsync(string date, List<AiRecItem> items)
    {
        try
        {
            var dir = Path.GetDirectoryName(_aiCacheFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var cache = new AiRecCache { Date = date, Items = items };
            // 异步写入，避免阻塞调用方线程
            await File.WriteAllTextAsync(_aiCacheFilePath, System.Text.Json.JsonSerializer.Serialize(cache));
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] AI 推荐缓存写入失败: {ex.Message}");
        }
    }

    /// <summary>使 AI 每日推荐缓存失效（内存 + 磁盘 + 尝试标记），用于手动刷新或重新扫描</summary>
    private void InvalidateAiCache()
    {
        _aiRecommendBatch = new();
        _aiRecommendBatchDate = null;
        _aiAttemptDate = null;
        _aiHeroIndex = 0;
        try { if (File.Exists(_aiCacheFilePath)) File.Delete(_aiCacheFilePath); } catch { }
    }

    /// <summary>AI 推荐开关切换时：持久化、重建 Hero 卡、刷新 AI 歌单可见性并准备歌单数据</summary>
    partial void OnIsAiRecommendationEnabledChanged(bool value)
    {
        Preferences.Default.Set("ai_recommendation_enabled", value);
        if (HeroCards.Count > 0 || _allDailyRecommendSongs.Count > 0)
        {
            GenerateHeroCards();
        }
        // AI 歌单区可见性跟随开关；开启时准备当天歌单数据
        OnPropertyChanged(nameof(IsAiPlaylistsVisible));
        OnPropertyChanged(nameof(IsAiPlaylistsSectionVisible));
        if (value)
            _ = EnsureDailyAiPlaylistsAsync();
    }

    /// <summary>刷新数据</summary>
}
