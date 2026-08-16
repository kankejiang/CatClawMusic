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

    /// <summary>AI 歌单生成中的实时思考过程（流式推理内容尾部，UI 展示让用户确认正在正确生成）</summary>
    [ObservableProperty]
    private string _aiPlaylistsThinking = "";

    /// <summary>AI 歌单区是否显示：开启智能推荐 且（有数据 或 正在生成）；模型未配置/生成失败时隐藏</summary>
    public bool IsAiPlaylistsVisible => IsAiRecommendationEnabled && AiPlaylists.Count > 0;

    /// <summary>AI 歌单区整体可见（含生成中的加载态）：开启智能推荐 且（有数据 或 正在生成）</summary>
    public bool IsAiPlaylistsSectionVisible => IsAiRecommendationEnabled && (AiPlaylists.Count > 0 || IsAiPlaylistsLoading);

    /// <summary>生成状态变化时同步区块可见性（加载中也要显示区块以便提示）</summary>
    partial void OnIsAiPlaylistsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAiPlaylistsSectionVisible));
    }

    private const int AiPlaylistCandidateCount = 120;

    private readonly string _aiPlaylistsCacheFilePath = Path.Combine(FileSystem.AppDataDirectory, "cache", "ai_playlists.json");
    private string? _aiPlaylistsDate;          // 内存中的歌单日期
    private string? _aiPlaylistsAttemptDate;   // 当天已尝试拉取的标记（失败也标记，避免反复调用）
    private bool _aiPlaylistsFetching;
    private bool _aiPlaylistsLoaded;           // 本次会话是否已检查（每天只走一次完整流程）
    private int _aiPlaylistsRetryCount;        // 候选池空时的延迟重试次数
    private int _aiPlaylistsGeneration;          // 来源/缓存失效时自增，用于丢弃正在进行的旧生成结果
    private bool _aiPlaylistsRegeneratePending;   // 生成中失效时置位，旧任务结束后自动按新来源重新生成

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
        if (_aiPlaylistsFetching)
        {
            return;
        }
        _aiPlaylistsLoaded = true;

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (_aiPlaylistsDate == today && AiPlaylists.Count > 0) return;

        // 候选池为空（发现页数据尚未加载，如刚开启开关/启动初期）：
        // 不标记"当天已尝试"，延迟重试几次（数据通常 1~3 秒内加载完成）
        var pool = BuildAiCandidatePool(AiPlaylistCandidateCount);
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

            // 调用 AI 生成当天歌单（每天仅一次）。逐张生成已实时上屏（Fetch 内部 Add），
            // 这里只负责存缓存；整批为空时区块自然隐藏。
            var generation = _aiPlaylistsGeneration;
            IsAiPlaylistsLoading = true;
            AiPlaylistsThinking = "";
            var batch = await FetchAiPlaylistsAsync(generation);
            AiPlaylistsThinking = "";
            // 生成期间来源/缓存已失效（如切换发现页来源）：丢弃本次结果，避免旧来源覆盖新缓存
            if (generation != _aiPlaylistsGeneration)
            {
                _aiPlaylistsLoaded = false;
                return;
            }
            _aiPlaylistsAttemptDate = today;
            if (batch.Count > 0)
            {
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
            if (_aiPlaylistsRegeneratePending)
            {
                _aiPlaylistsRegeneratePending = false;
                _aiPlaylistsLoaded = false;
                MainThread.BeginInvokeOnMainThread(() => _ = EnsureDailyAiPlaylistsAsync());
            }
        }
    }

    /// <summary>手动重新生成今天的 AI 歌单：清空内存与磁盘缓存后强制重新调用 AI。</summary>
    public async Task RegenerateAiPlaylistsAsync()
    {
        if (!IsAiRecommendationEnabled || !_agentService.IsConfigured) return;
        if (_aiPlaylistsFetching) return;
        InvalidateAiPlaylistsCache(clearDisk: true);
        await EnsureDailyAiPlaylistsAsync();
    }

    /// <summary>发现页来源切换时调用：清空 AI 歌单内存与磁盘缓存，下次加载按新来源重新生成。</summary>
    public void InvalidateAiPlaylistsForSourceChange()
    {
        InvalidateAiPlaylistsCache(clearDisk: true);
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

    /// <summary>按文字系统粗略判定歌曲语言（用于给 AI 候选列表标注，帮助风格/类型判断）</summary>
    private static string GuessSongLanguage(Song s)
    {
        var text = $"{s.Title ?? ""} {s.Artist ?? ""}";
        bool jp = false, ko = false, cjk = false, latin = false;
        foreach (var ch in text)
        {
            if (ch >= 0x3040 && ch <= 0x30FF) jp = true;                       // 假名
            else if (ch >= 0xAC00 && ch <= 0xD7AF) ko = true;                  // 谚文
            else if (ch >= 0x4E00 && ch <= 0x9FFF) cjk = true;                 // 汉字
            else if (char.IsLetter(ch) && ch <= 0x007F) latin = true;          // 拉丁
        }
        if (jp) return "日语";
        if (ko) return "韩语";
        if (cjk) return "中文";
        if (latin) return "英语/其他拉丁";
        return "未知";
    }

    /// <summary>向 AI 请求当天歌单：候选歌曲（含 ID）交给 AI，生成 5 个主题歌单（每歌单 20 首 + 推荐理由），
    /// 返回严格 JSON，随后按 ID 匹配回本地曲库，避免 AI 编造不存在的歌曲。
    /// 主题规则：①星期/周末 ②节日/节气/季节 + 当前时间段 ③常听偏好 ④音乐类型风格 ⑤自由发挥。
    /// 生成策略：串行逐张生成（一次只生成一张，完成后立即上屏，用户可实时看到进度）；
    /// 后续歌单会被告知前面已生成的歌单名，避免产出雷同歌单。</summary>
    private async Task<List<AiPlaylist>> FetchAiPlaylistsAsync(int generation)
    {
        // 候选池截断：推理模型会逐首分析候选歌曲（reasoning 占用大量 token）。
        // 5 张 × 20 首共需 100 首，因此提供 120 首候选，尽量让每张歌单都能选到不同歌曲。
        var pool = BuildAiCandidatePool(AiPlaylistCandidateCount);
        if (pool.Count == 0) return new();

        var sb = new StringBuilder();
        // 候选行用连续序号（1-N）而不是数据库主键 ID：大数字 ID 模型极易写错/编造，
        // 解析时全部过滤导致歌单只剩 1 首歌。序号对模型友好，按序号映射回真实歌曲。
        for (int i = 0; i < pool.Count; i++)
        {
            var s = pool[i];
            // 语言标注 + 专辑名：帮助 AI 判断风格/类型（否则日语歌可能被塞进国风歌单）
            sb.AppendLine($"{i + 1}. {s.Title ?? "未知"} - {s.Artist ?? "未知艺术家"}（{GuessSongLanguage(s)}）〔{s.Album ?? "未知专辑"}〕");
        }

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

        // 5 个主题 prompt（每个只生成 1 张歌单）
        var themes = new[]
        {
            $"第一张：以今天是{weekDay}（{isWeekend}）为主题（如{isWeekend}的放松时光、{weekDay}的能量补给）。",
            $"第二张：先判断今天是否节日或节气（如有则以其为主题，没有则用当前季节），并紧密结合当前时间段（{period}）的听歌场景。",
            "第三张：基于用户常听的音乐偏好（从候选歌曲中推断最常见的口味/情绪/风格）。",
            "第四张：基于明确的音乐类型或风格（如流行、民谣、古风、电子、摇滚等）。注意：必须严格根据候选歌曲的（语言）标注和专辑名判断实际风格——候选池里没有某风格的歌时，请选择最接近的已有风格，绝对不要硬凑（例如没有国风歌就不要把日语歌放进国风歌单）。",
            "第五张：自由发挥，创意不限。",
        };
        var dateContext = $"今天是 {now:yyyy年M月d日}（{weekDay}，{isWeekend}），当前季节：{season}，当前时间段：{period}。";

        // 串行逐张生成：一次只生成一张，完成后立即上屏。
        // 单张失败不中断整批，已成功的歌单仍会返回并落盘。
        var generated = new List<AiPlaylist>();
        var seqById = pool.Select((s, index) => (s.Id, Seq: index + 1))
            .ToDictionary(x => x.Id, x => x.Seq);
        var usedIndices = new HashSet<int>();
        foreach (var theme in themes)
        {
            // 告知前面已生成的歌单名，避免雷同
            var prevSummary = generated.Count > 0
                ? string.Join("、", generated.Select(p => $"「{p.Name}」"))
                : "还没有";
            var prevUsed = usedIndices.Count > 0
                ? string.Join("、", usedIndices.OrderBy(x => x))
                : "无";

            AiPlaylist? playlist = null;
            try
            {
                playlist = await GenerateOnePlaylistAsync(dateContext, theme, prevSummary, prevUsed, sb.ToString(), pool);
            }
            catch (Exception ex)
            {
                Log.Debug("SearchViewModel", $"[SearchVM] AI 歌单第 {generated.Count + 1} 张生成失败: {ex.Message}");
            }

            // 生成期间来源/缓存已失效：停止继续生成，丢弃本次剩余结果
            if (generation != _aiPlaylistsGeneration) return generated;

            if (playlist != null)
            {
                generated.Add(playlist);
                // 记录已使用歌曲序号，后续歌单尽量避开，减少重复
                foreach (var song in playlist.Songs)
                {
                    if (seqById.TryGetValue(song.Id, out var seq))
                        usedIndices.Add(seq);
                }

                // 生成一张显示一张：立即加入 UI 集合
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (generation != _aiPlaylistsGeneration) return;
                    AiPlaylists.Add(playlist);
                    OnPropertyChanged(nameof(IsAiPlaylistsVisible));
                    OnPropertyChanged(nameof(IsAiPlaylistsSectionVisible));
                });
            }
        }
        return generated;
    }

    /// <summary>生成单张 AI 歌单（1 个歌单 20 首 + 名字 + 理由），返回严格 JSON 对象。
    /// 流式：推理内容实时更新 <see cref="AiPlaylistsThinking"/>（截取尾部，UI 显示生成进度）。</summary>
    private async Task<AiPlaylist?> GenerateOnePlaylistAsync(string dateContext, string theme, string prevSummary, string prevUsed, string candidateList, List<Song> pool)
    {
        var systemPrompt = "你是Yuki，猫爪音乐的AI音乐推荐助手，说话温柔可爱带点喵口癖。";
        var userPrompt =
            $"{dateContext}\n" +
            $"下面是用户曲库里的候选歌曲（每行格式：序号. 歌名 - 艺术家）：\n{candidateList}\n" +
            $"请创建 1 个歌单：\n{theme}\n" +
            $"目前已生成的歌单：{prevSummary}。请确保本次歌单与已生成的歌单主题明显不同，不要重复。\n" +
            $"为避免歌单歌曲过度重复，请尽量优先选择之前歌单未使用过的歌曲；已使用过的候选序号：{prevUsed}。\n" +
            "歌单包含 20 首歌曲、一个歌单名和一句推荐理由（理由不超过20字，不要加引号）。\n" +
            "歌曲必须从上面的候选列表中选择，song_ids 里填歌曲序号（数字）。\n" +
            "重要：不要做任何逐步分析、解释或逐首歌点评，直接快速挑选并输出结果。\n" +
            "只返回严格的 JSON 对象，不要任何多余文字、代码块标记、换行或缩进（保持单行紧凑），格式：" +
            "{\"name\":\"歌单名\",\"reason\":\"推荐理由\",\"song_ids\":[数字,...]}";

        // 流式思考过程：累积推理内容，UI 只展示尾部（模拟"正在思考"的滚动感）。
        // 推理力度固定 low：歌单生成是简单挑选任务，不需要高推理深度（更快更省）。
        var thinking = new StringBuilder();
        var lastFlush = DateTime.UtcNow;
        var raw = await _agentService.QuickAskStreamAsync(systemPrompt, userPrompt, reasoning =>
        {
            lock (thinking)
            {
                thinking.Append(reasoning);
                // 节流：约每 250ms 刷新一次 UI（onDelta 每 token 触发，直接绑定会高频 PropertyChanged）
                if ((DateTime.UtcNow - lastFlush).TotalMilliseconds >= 250)
                {
                    lastFlush = DateTime.UtcNow;
                    var tail = thinking.Length > 120 ? thinking.ToString()[^120..] : thinking.ToString();
                    MainThread.BeginInvokeOnMainThread(() => AiPlaylistsThinking = tail);
                }
            }
        }, "low");
        // 生成完成：最终刷新一次
        lock (thinking)
        {
            if (thinking.Length > 0)
            {
                var tail = thinking.Length > 120 ? thinking.ToString()[^120..] : thinking.ToString();
                MainThread.BeginInvokeOnMainThread(() => AiPlaylistsThinking = tail);
            }
        }
        return ParseSingleAiPlaylist(raw, pool);
    }

    /// <summary>解析单张 AI 歌单 JSON 对象：song_ids 先按候选序号（1-N）映射回真实歌曲，
    /// 返回前把 SongIds 转换为真实数据库 ID，供磁盘缓存跨重启恢复使用；空歌单返回 null。</summary>
    private static AiPlaylist? ParseSingleAiPlaylist(string raw, List<Song> pool)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            var json = raw.Substring(start, end - start + 1);

            // 反序列化（失败时尝试补全被截断的 JSON 尾部括号）
            AiPlaylist? item = null;
            try
            {
                item = System.Text.Json.JsonSerializer.Deserialize<AiPlaylist>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (System.Text.Json.JsonException)
            {
                foreach (var suffix in new[] { "}", "]}", "}]}" })
                {
                    try
                    {
                        item = System.Text.Json.JsonSerializer.Deserialize<AiPlaylist>(json.TrimEnd() + suffix,
                            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (item != null) break;
                    }
                    catch { }
                }
            }
            if (item == null) return null;

            var name = item.Name?.Trim() ?? "";
            var reason = item.Reason?.Trim()?.Trim('"', '「', '」', '\n', '\r') ?? "";
            if (string.IsNullOrEmpty(name)) return null;
            if (reason.Length > 40) reason = reason.Substring(0, 40);

            // 按候选序号映射真实歌曲（1-N → pool 索引），去重
            var songs = new List<Song>();
            var used = new HashSet<int>();
            foreach (var id in item.SongIds)
            {
                if (used.Contains(id)) continue;
                used.Add(id);
                var idx = id - 1; // 候选行是 1-based 序号
                if (idx >= 0 && idx < pool.Count)
                    songs.Add(pool[idx]);
            }
            if (songs.Count == 0) return null; // 空歌单丢弃

            // SongIds 转成真实数据库 ID：磁盘缓存按真实 ID 重新匹配曲库，避免旧缓存把候选序号当 ID 用
            return new AiPlaylist
            {
                Name = name,
                Reason = reason,
                SongIds = songs.Select(s => s.Id).ToList(),
                Songs = songs
            };
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] AI 歌单单张解析失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>从磁盘读取当天的 AI 歌单缓存；日期不匹配或为空时返回 null</summary>
    private async Task<List<AiPlaylist>?> LoadAiPlaylistsCacheAsync(string date)
    {
        try
        {
            if (!File.Exists(_aiPlaylistsCacheFilePath)) return null;
            var json = await File.ReadAllTextAsync(_aiPlaylistsCacheFilePath);
            var cache = System.Text.Json.JsonSerializer.Deserialize<AiPlaylistsCache>(json);
            if (cache?.Date != date || cache.Version < 2 || cache.Playlists == null || cache.Playlists.Count == 0) return null;

            // 缓存只存 ID，需要按当前曲库重新映射（曲库变化时自动丢弃失效项）
            var pool = BuildAiCandidatePool(AiPlaylistCandidateCount);
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
            var cache = new AiPlaylistsCache { Version = 2, Date = date, Playlists = playlists };
            await File.WriteAllTextAsync(_aiPlaylistsCacheFilePath, System.Text.Json.JsonSerializer.Serialize(cache));
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] AI 歌单缓存写入失败: {ex.Message}");
        }
    }

    /// <summary>使 AI 歌单缓存失效（扫描后调用）：默认只重置内存标记，保留当天磁盘缓存
    /// （Ensure 会先命中磁盘按当前曲库重新映射，避免扫描后重复调用 AI）。
    /// 手动刷新时传 clearDisk=true 删除磁盘缓存，强制重新调用 AI 生成。</summary>
    private void InvalidateAiPlaylistsCache(bool clearDisk = false)
    {
        var wasFetching = _aiPlaylistsFetching;
        _aiPlaylistsGeneration++;
        _aiPlaylistsDate = null;
        _aiPlaylistsAttemptDate = null;
        _aiPlaylistsLoaded = false;
        AiPlaylists.Clear();
        if (clearDisk)
        {
            try { if (File.Exists(_aiPlaylistsCacheFilePath)) File.Delete(_aiPlaylistsCacheFilePath); }
            catch { }
        }
        if (wasFetching) _aiPlaylistsRegeneratePending = true;
        OnPropertyChanged(nameof(IsAiPlaylistsVisible));
        OnPropertyChanged(nameof(IsAiPlaylistsSectionVisible));
    }
}
