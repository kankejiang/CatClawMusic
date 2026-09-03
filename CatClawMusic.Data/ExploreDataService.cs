using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CatClawMusic.Data;

/// <summary>
/// 探索页面数据服务，封装每日推荐、艺术家、专辑、最多播放、最新音乐等查询
/// </summary>
public class ExploreDataService
{
    /// <summary>数据库访问实例，用于查询歌曲、艺术家、专辑及播放历史</summary>
    private readonly MusicDatabase _db;
    /// <summary>音乐库服务，用于获取合并去重后的歌曲列表</summary>
    private readonly IMusicLibraryService _library;
    /// <summary>缓存目录绝对路径，用于存储每日推荐磁盘缓存文件</summary>
    private readonly string _cacheDir;
    /// <summary>每日推荐磁盘缓存文件完整路径（daily_recommend.json）</summary>
    private readonly string _cacheFilePath;

    /// <summary>每日推荐缓存：Key 为日期字符串 "yyyy-MM-dd"，Value 为歌曲列表</summary>
    private string? _dailyRecommendDate;
    /// <summary>每日推荐歌曲列表的内存缓存</summary>
    private List<Song>? _dailyRecommendCache;

    /// <summary>每日推荐艺人缓存（每天随机10个）</summary>
    private List<ArtistWithCount>? _dailyArtistsCache;
    /// <summary>每日推荐专辑缓存（每天随机10个）</summary>
    private List<AlbumWithCount>? _dailyAlbumsCache;
    /// <summary>全部艺术家聚合结果缓存（按其来源严格过滤后），避免每次进入艺术家页重复聚合+封面解析</summary>
    private List<ArtistWithCount>? _allArtistsCache;
    /// <summary>全部专辑聚合结果缓存，避免每次进入专辑页重复聚合</summary>
    private List<AlbumWithCount>? _allAlbumsCache;
    /// <summary>已筛选（来源过滤 + 填充 PlayCount）歌曲的内存缓存，避免探索页三路聚合重复整库加载与历史聚合（在 UI 线程造成卡顿）</summary>
    private List<Song>? _filteredSongsCache;
    /// <summary>筛选缓存对应的来源筛选键，来源切换时失效</summary>
    private string? _filteredSongsCacheKey;
    /// <summary>整库合并歌曲缓存构建锁：并发首次访问只让一个任务真正加载，避免启动阶段多路并发重复整库加载</summary>
    private readonly SemaphoreSlim _filteredSongsLock = new(1, 1);

    /// <summary>来源筛选：all, local, network</summary>
    private string _sourceFilter = "all";

    /// <summary>
    /// 初始化探索页面数据服务。
    /// </summary>
    /// <param name="db">数据库访问实例。</param>
    /// <param name="library">音乐库服务，用于获取合并去重后的歌曲列表。</param>
    /// <param name="cacheDir">缓存目录路径，用于存储每日推荐磁盘缓存。</param>
    public ExploreDataService(MusicDatabase db, IMusicLibraryService library, string cacheDir)
    {
        _db = db;
        _library = library;
        _cacheDir = cacheDir;
        _cacheFilePath = Path.Combine(cacheDir, "daily_recommend.json");
        try { Directory.CreateDirectory(cacheDir); } catch (Exception ex) { Log.Debug("ExploreDataService", $"创建缓存目录失败: {ex.Message}"); }
    }

    /// <summary>设置来源筛选</summary>
    public void SetSourceFilter(string filter)
    {
        if (_sourceFilter != filter)
        {
            _sourceFilter = filter;
            _dailyRecommendCache = null; // 清除缓存以重新筛选
            _filteredSongsCache = null;
            _filteredSongsCacheKey = null;
            // 全部艺术家/专辑聚合结果按当前来源筛选计算，切换时一并失效（避免发现页每日推荐
            // 复用旧来源统计出的共享缓存，也修正此前筛选切换后计数残留的旧数据）
            _allArtistsCache = null;
            _allAlbumsCache = null;
        }
    }

    /// <summary>当前生效的来源筛选：all / local / network</summary>
    public string CurrentSourceFilter => _sourceFilter;

    /// <summary>
    /// 使每日推荐缓存失效：清除内存缓存和磁盘缓存。
    /// 在音乐库扫描完成、歌曲发生变化后调用，确保探索页展示最新数据。
    /// </summary>
    public void InvalidateDailyRecommendCache()
    {
        _dailyRecommendCache = null;
        _dailyArtistsCache = null;
        _dailyAlbumsCache = null;
        _allArtistsCache = null;
        _allAlbumsCache = null;
        _dailyRecommendDate = null;
        _filteredSongsCache = null;
        _filteredSongsCacheKey = null;
        try
        {
            if (File.Exists(_cacheFilePath))
                File.Delete(_cacheFilePath);
        }
        catch (Exception ex) { Log.Debug("ExploreDataService", $"删除缓存文件失败: {ex.Message}"); }
    }

    /// <summary>根据来源筛选过滤歌曲列表</summary>
    private List<Song> ApplySourceFilter(List<Song> songs)
    {
        return _sourceFilter switch
        {
            "local" => songs.Where(s => s.Source == SongSource.Local).ToList(),
            "network" => songs.Where(s => s.Source != SongSource.Local).ToList(),
            _ => songs
        };
    }

    /// <summary>获取每日推荐（每天0点更新，随机20首）</summary>
    public async Task<List<Song>> GetDailyRecommendAsync()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        // 内存缓存命中
        if (_dailyRecommendCache != null && _dailyRecommendDate == today)
            return _dailyRecommendCache;

        // 尝试从磁盘缓存恢复
        var diskCache = await LoadDailyRecommendFromDiskAsync(today).ConfigureAwait(false);
        if (diskCache != null)
        {
            _dailyRecommendCache = diskCache;
            _dailyRecommendDate = today;
            return diskCache;
        }

        // 生成新的推荐（partial Fisher-Yates 抽样，替代全量随机排序）
        var allSongs = await GetFilteredSongsAsync().ConfigureAwait(false);
        var shuffled = RandomSampler.Sample(allSongs, 20);

        _dailyRecommendCache = shuffled;
        _dailyRecommendDate = today;
        await SaveDailyRecommendToDiskAsync(today, shuffled).ConfigureAwait(false);
        return shuffled;
    }

    /// <summary>从磁盘缓存加载原始缓存对象（含艺人/专辑 ID）</summary>
    private async Task<DailyRecommendCache?> LoadDailyCacheFromDiskAsync(string date)
    {
        try
        {
            if (!File.Exists(_cacheFilePath)) return null;
            var json = await File.ReadAllTextAsync(_cacheFilePath);
            var cache = System.Text.Json.JsonSerializer.Deserialize<DailyRecommendCache>(json);
            return cache?.Date == date ? cache : null;
        }
        catch { return null; }
    }

    /// <summary>从磁盘缓存加载每日推荐（异步版本，避免死锁）</summary>
    private async Task<List<Song>?> LoadDailyRecommendFromDiskAsync(string date)
    {
        try
        {
            if (!File.Exists(_cacheFilePath)) return null;
            var json = await File.ReadAllTextAsync(_cacheFilePath).ConfigureAwait(false);
            var cache = System.Text.Json.JsonSerializer.Deserialize<DailyRecommendCache>(json);
            if (cache?.Date != date) return null;
            var allSongs = await _db.GetSongsAsync().ConfigureAwait(false);
            var filtered = ApplySourceFilter(allSongs);
            // 用字典 O(1) 查找替代原 O(ids × 全库) 的 FirstOrDefault 循环（TryAdd 保留首个、容错重复 ID）
            var byId = new Dictionary<int, Song>(filtered.Count);
            foreach (var s in filtered)
                byId.TryAdd(s.Id, s);
            var result = new List<Song>();
            foreach (var id in cache.Ids)
            {
                if (byId.TryGetValue(id, out var song)) result.Add(song);
            }
            return result.Count > 0 ? result : null;
        }
        catch { return null; }
    }

    /// <summary>保存每日推荐到磁盘缓存</summary>
    private async Task SaveDailyRecommendToDiskAsync(string date, List<Song> songs)
    {
        try
        {
            // 读取已有缓存以保留 artist/album IDs
            DailyRecommendCache existing;
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    // 异步读取，避免阻塞调用方线程
                    var json = await File.ReadAllTextAsync(_cacheFilePath).ConfigureAwait(false);
                    existing = System.Text.Json.JsonSerializer.Deserialize<DailyRecommendCache>(json) ?? new DailyRecommendCache();
                }
                else
                {
                    existing = new DailyRecommendCache();
                }
            }
            catch { existing = new DailyRecommendCache(); }

            existing.Date = date;
            existing.Ids = songs.Select(s => s.Id).ToList();

            Directory.CreateDirectory(_cacheDir);
            var output = System.Text.Json.JsonSerializer.Serialize(existing);
            // 异步写入，避免阻塞调用方线程
            await File.WriteAllTextAsync(_cacheFilePath, output).ConfigureAwait(false);
        }
        catch (Exception ex) { Log.Debug("ExploreDataService", $"写入每日推荐缓存失败: {ex.Message}"); }
    }

    /// <summary>将艺人/专辑 ID 合并到已有磁盘缓存（不覆盖歌曲推荐）</summary>
    private async Task SaveArtistAlbumIdsToCacheAsync(string date, List<int> artistIds, List<int> albumIds)
    {
        try
        {
            DailyRecommendCache existing;
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    var json = await File.ReadAllTextAsync(_cacheFilePath);
                    existing = System.Text.Json.JsonSerializer.Deserialize<DailyRecommendCache>(json) ?? new DailyRecommendCache();
                }
                else
                {
                    existing = new DailyRecommendCache { Date = date };
                }
            }
            catch { existing = new DailyRecommendCache { Date = date }; }

            existing.Date = date;
            if (artistIds.Count > 0) existing.ArtistIds = artistIds;
            if (albumIds.Count > 0) existing.AlbumIds = albumIds;

            Directory.CreateDirectory(_cacheDir);
            var output = System.Text.Json.JsonSerializer.Serialize(existing);
            await File.WriteAllTextAsync(_cacheFilePath, output);
        }
        catch (Exception ex) { Log.Debug("ExploreDataService", $"写入艺人专辑缓存失败: {ex.Message}"); }
    }

    /// <summary>获取每日推荐艺术家（每天随机10个，带缓存）</summary>
    public async Task<List<ArtistWithCount>> GetArtistsWithSongCountAsync()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        // 内存缓存命中
        if (_dailyArtistsCache != null && _dailyRecommendDate == today)
            return _dailyArtistsCache;

        // 复用「全部艺术家」共享缓存（SQL 聚合实现，进程内只聚合一次，供发现页/音乐库页共用）
        var allArtists = await GetAllArtistsAsync().ConfigureAwait(false);

        // 尝试从磁盘缓存恢复艺人 ID 列表
        var diskCache = await LoadDailyCacheFromDiskAsync(today).ConfigureAwait(false);
        if (diskCache != null && diskCache.ArtistIds.Count > 0)
        {
            var cached = allArtists
                .Where(a => diskCache.ArtistIds.Contains(a.Id))
                .ToList();
            if (cached.Count > 0)
            {
                _dailyArtistsCache = cached;
                return cached;
            }
        }

        // 随机选10个（partial Fisher-Yates 抽样）
        var selected = RandomSampler.Sample(allArtists, 10);
        _dailyArtistsCache = selected;

        // 保存到磁盘缓存（合并到同一个 JSON 文件）
        await SaveArtistAlbumIdsToCacheAsync(today,
            selected.Select(a => a.Id).ToList(),
            new List<int>()).ConfigureAwait(false);

        return selected;
    }

    /// <summary>获取所有艺术家及其歌曲数量（内部方法，SQL 聚合版）
    /// 计数（主 ArtistId）、补充计数（SongArtists 多对多）与采样封面全部在数据库 GROUP BY 完成，
    /// 不再整表拉回万级歌曲行后做多次客户端 LINQ 遍历。</summary>
    private async Task<List<ArtistWithCount>> GetAllArtistsWithCountInternalAsync()
    {
        await _db.EnsureInitializedAsync().ConfigureAwait(false);
        // 四路独立查询并行：艺术家表（小表）、主计数、SongArtists 补充计数、每艺术家一首采样封面
        var artistsTask = _db.GetAllArtistsAsync();
        var countsTask = _db.GetArtistSongCountsAsync(_sourceFilter);
        var supplementaryTask = _db.GetSupplementaryArtistSongCountsAsync(_sourceFilter);
        var sampleCoverTask = _db.GetSampleSongsForArtistsAsync();
        await Task.WhenAll(artistsTask, countsTask, supplementaryTask, sampleCoverTask).ConfigureAwait(false);
        var artists = artistsTask.Result;

        // 主计数 + 补充计数（合作歌曲次要艺术家，语义与客户端聚合等价）
        var artistSongCount = new Dictionary<int, int>(countsTask.Result);
        foreach (var kv in supplementaryTask.Result)
            artistSongCount[kv.Key] = artistSongCount.GetValueOrDefault(kv.Key, 0) + kv.Value;

        // 每个艺术家第一首本地采样歌曲（SQL GROUP BY 已取首行）
        var sampleSongByArtist = new Dictionary<int, Song>();
        foreach (var s in sampleCoverTask.Result)
            if (s.ArtistId > 0 && !sampleSongByArtist.ContainsKey(s.ArtistId))
                sampleSongByArtist[s.ArtistId] = s;

        return artists
            .Where(a => artistSongCount.ContainsKey(a.Id))
            .Where(a => !IsCombinedArtistName(a.Name))
            .Select(a =>
            {
                var result = new ArtistWithCount
                {
                    Id = a.Id,
                    Name = a.Name,
                    Cover = a.Cover,
                    SongCount = artistSongCount.GetValueOrDefault(a.Id, 0)
                };
                if (sampleSongByArtist.TryGetValue(a.Id, out var sample))
                {
                    result.SampleCoverPath = sample.CoverArtPath;
                    result.SampleSongId = sample.Id;
                    result.SampleMediaStoreId = sample.MediaStoreId;
                    result.SampleFilePath = sample.FilePath;
                }
                return result;
            })
            .OrderBy(a => a.Name)
            .ToList();
    }

    /// <summary>获取每日推荐专辑（每天随机10个，带缓存）</summary>
    public async Task<List<AlbumWithCount>> GetAlbumsWithSongCountAsync()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        // 内存缓存命中
        if (_dailyAlbumsCache != null && _dailyRecommendDate == today)
            return _dailyAlbumsCache;

        // 复用「全部专辑」共享缓存（SQL 聚合实现，进程内只聚合一次，供发现页/音乐库页共用）
        var allAlbums = await GetAllAlbumsAsync().ConfigureAwait(false);

        // 尝试从磁盘缓存恢复专辑 ID 列表
        var diskCache = await LoadDailyCacheFromDiskAsync(today).ConfigureAwait(false);
        if (diskCache != null && diskCache.AlbumIds.Count > 0)
        {
            var cached = allAlbums
                .Where(a => diskCache.AlbumIds.Contains(a.Id))
                .ToList();
            if (cached.Count > 0)
            {
                _dailyAlbumsCache = cached;
                return cached;
            }
        }

        // 随机选10个（partial Fisher-Yates 抽样）
        var selected = RandomSampler.Sample(allAlbums, 10);
        _dailyAlbumsCache = selected;

        // 保存到磁盘缓存
        await SaveArtistAlbumIdsToCacheAsync(today,
            new List<int>(),
            selected.Select(a => a.Id).ToList()).ConfigureAwait(false);

        return selected;
    }

    /// <summary>获取所有专辑及歌曲数量（内部方法，SQL 聚合版）
    /// 专辑计数与采样封面在数据库 GROUP BY 完成，不再整表拉回歌曲行后多次客户端 LINQ 遍历。</summary>
    private async Task<List<AlbumWithCount>> GetAllAlbumsWithCountInternalAsync()
    {
        await _db.EnsureInitializedAsync().ConfigureAwait(false);
        // 四路独立查询并行：专辑表（小表）、计数、艺术家表（小表，用于名称映射）、每专辑一首采样封面
        var albumsTask = _db.GetAllAlbumsAsync();
        var countsTask = _db.GetAlbumSongCountsAsync(_sourceFilter);
        var artistsTask = _db.GetAllArtistsAsync();
        var sampleCoverTask = _db.GetSampleSongsForAlbumsAsync();
        await Task.WhenAll(albumsTask, countsTask, artistsTask, sampleCoverTask).ConfigureAwait(false);
        var albums = albumsTask.Result;
        var artistDict = artistsTask.Result.ToDictionary(a => a.Id, a => a.Name);

        var albumSongCount = countsTask.Result;

        // 每个专辑第一首本地采样歌曲（SQL GROUP BY 已取首行）
        var sampleSongByAlbum = new Dictionary<int, Song>();
        foreach (var s in sampleCoverTask.Result)
            if (s.AlbumId > 0 && !sampleSongByAlbum.ContainsKey(s.AlbumId))
                sampleSongByAlbum[s.AlbumId] = s;

        return albums
            .Where(a => albumSongCount.ContainsKey(a.Id))
            .Select(a =>
            {
                var result = new AlbumWithCount
                {
                    Id = a.Id,
                    Title = a.Title,
                    CoverArtPath = a.CoverArtPath,
                    Cover = a.Cover,
                    ArtistName = a.ArtistId > 0 ? artistDict.GetValueOrDefault(a.ArtistId, "未知艺术家") : "未知艺术家",
                    SongCount = albumSongCount.GetValueOrDefault(a.Id, 0),
                    Year = a.Year ?? a.ReleaseYear
                };
                if (sampleSongByAlbum.TryGetValue(a.Id, out var sample))
                {
                    result.SampleCoverPath = sample.CoverArtPath;
                    result.SampleSongId = sample.Id;
                    result.SampleMediaStoreId = sample.MediaStoreId;
                    result.SampleFilePath = sample.FilePath;
                }
                return result;
            })
            .OrderBy(a => a.Title)
            .ToList();
    }

    /// <summary>获取最多播放的歌曲（含播放次数）</summary>
    public async Task<List<Song>> GetTopPlayedSongsAsync(int limit = 50)
    {
        var songs = await _db.GetTopPlayedSongsAsync(limit).ConfigureAwait(false);
        return ApplySourceFilter(songs);
    }

    /// <summary>获取最近7天内入库的歌曲</summary>
    public async Task<List<Song>> GetRecentlyAddedSongsAsync(int limit = 50)
    {
        var allSongs = await GetFilteredSongsAsync().ConfigureAwait(false);
        var sevenDaysAgo = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeSeconds();
        return allSongs
            .Where(s => s.DateAdded >= sevenDaysAgo)
            .OrderByDescending(s => s.DateAdded)
            .Take(limit)
            .ToList();
    }

    /// <summary>按艺术家名称获取歌曲</summary>
    public async Task<List<Song>> GetSongsByArtistAsync(string artistName)
    {
        var songs = await _db.GetSongsByArtistAsync(artistName);
        return ApplySourceFilter(songs);
    }

    /// <summary>按专辑名称获取歌曲</summary>
    public async Task<List<Song>> GetSongsByAlbumAsync(string albumTitle)
    {
        var songs = await _db.GetSongsByAlbumAsync(albumTitle);
        return ApplySourceFilter(songs);
    }

    /// <summary>获取所有专辑列表（含歌曲数量）</summary>
    public async Task<List<AlbumWithCount>> GetAllAlbumsAsync()
    {
        if (_allAlbumsCache != null) return _allAlbumsCache;
        var list = await GetAllAlbumsWithCountInternalAsync().ConfigureAwait(false);
        _allAlbumsCache = list;
        return list;
    }

    /// <summary>获取所有艺术家列表（含歌曲数量）</summary>
    public async Task<List<ArtistWithCount>> GetAllArtistsAsync()
    {
        if (_allArtistsCache != null) return _allArtistsCache;
        var list = await GetAllArtistsWithCountInternalAsync().ConfigureAwait(false);
        _allArtistsCache = list;
        return list;
    }

    /// <summary>获取经过来源筛选和协议过滤的全部歌曲（含 PlayCount）。
    /// 结果按来源筛选键实例级缓存：探索页三路聚合（每日推荐/艺人/专辑）共用一份，
    /// 避免重复整库加载与万级历史聚合。ConfigureAwait(false) 使后续 LINQ 在后台线程执行，
    /// 不占用 UI 线程（原实现在 UI 线程重复 3 次整库 LINQ，导致进入发现页 ~9s 冻结）。</summary>
    private async Task<List<Song>> GetFilteredSongsAsync()
    {
        if (_filteredSongsCache != null && _filteredSongsCacheKey == _sourceFilter)
            return _filteredSongsCache;

        // 并发首次访问（启动阶段发现页/音乐库页/搜索预热的并行加载）只让一个任务真正构建，
        // 避免多路并发同时整库加载+历史聚合造成重复 IO 与内存峰值。
        await _filteredSongsLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_filteredSongsCache != null && _filteredSongsCacheKey == _sourceFilter)
                return _filteredSongsCache;

            // 使用 GetMergedSongsAsync 获取本地+网络歌曲（已去重、已过滤协议）
            var allSongs = await _library.GetMergedSongsAsync().ConfigureAwait(false);
            var filtered = ApplySourceFilter(allSongs);

            // 补充 PlayCount 数据
            await FillPlayCountAsync(filtered).ConfigureAwait(false);

            _filteredSongsCache = filtered;
            _filteredSongsCacheKey = _sourceFilter;
            return filtered;
        }
        finally { _filteredSongsLock.Release(); }
    }

    /// <summary>判断艺术家名是否为历史遗留的合并名称（如 "国风堂/哦漏"），应被过滤掉</summary>
    private static bool IsCombinedArtistName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!name.Contains('/')) return false;
        // 调用 SplitArtistNames 验证：能拆出多个名字 → 是合并名称
        var names = CatClawMusic.Core.Services.MusicUtility.SplitArtistNames(name);
        return names.Count > 1;
    }

    /// <summary>从 PlayHistory 表填充歌曲的播放次数（SQL 聚合，避免拉取万级历史行后在客户端 GroupBy）</summary>
    private async Task FillPlayCountAsync(List<Song> songs)
    {
        try
        {
            await _db.EnsureInitializedAsync().ConfigureAwait(false);
            // 改用 SQL GROUP BY 聚合：不再把 1 万条 PlayHistory 行拉回客户端做 GroupBy/Sum，
            // 既减少对象分配，也将工作留在数据库/后台线程，避免 UI 线程阻塞。
            var dict = await _db.GetPlayCountTotalsAsync().ConfigureAwait(false);
            foreach (var s in songs)
            {
                if (dict.TryGetValue(s.Id, out var count))
                    s.PlayCount = count;
            }
        }
        catch (Exception ex) { Log.Debug("ExploreDataService", $"填充播放次数失败: {ex.Message}"); }
    }

    /// <summary>
    /// 每日推荐磁盘缓存数据结构，序列化为 daily_recommend.json 持久化存储。
    /// 同一文件中同时保存歌曲、艺术家、专辑三组 ID，确保同一天的推荐结果一致。
    /// </summary>
    private class DailyRecommendCache
    {
        /// <summary>缓存日期，格式 "yyyy-MM-dd"，与当日推荐匹配时才使用</summary>
        public string Date { get; set; } = "";
        /// <summary>每日推荐歌曲 ID 列表</summary>
        public List<int> Ids { get; set; } = new();
        /// <summary>每日推荐艺术家 ID 列表</summary>
        public List<int> ArtistIds { get; set; } = new();
        /// <summary>每日推荐专辑 ID 列表</summary>
        public List<int> AlbumIds { get; set; } = new();
    }
}

/// <summary>艺术家及其歌曲数量</summary>
public class ArtistWithCount : INotifyPropertyChanged
{
    /// <summary>属性变更事件，用于封面在后台解析完成后通知绑定刷新</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>触发属性变更通知</summary>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>艺术家数据库 ID</summary>
    public int Id { get; set; }
    /// <summary>艺术家名称</summary>
    public string Name { get; set; } = "";
    /// <summary>艺术家封面 URL / 本地缓存路径（来自元数据刮削或内嵌封面提取）</summary>
    public string? Cover
    {
        get => _cover;
        set { if (_cover != value) { _cover = value; OnPropertyChanged(); } }
    }
    private string? _cover;
    /// <summary>该艺术家的歌曲总数（含合作歌曲）</summary>
    public int SongCount { get; set; }
    /// <summary>从该艺术家第一首歌曲获取的封面路径，用于列表页快速显示</summary>
    public string? SampleCoverPath { get; set; }
    /// <summary>从该艺术家第一首歌曲获取的歌曲 ID，用于解析封面缓存</summary>
    public int SampleSongId { get; set; }
    /// <summary>从该艺术家第一首歌曲获取的 MediaStoreId，用于快速加载封面</summary>
    public long SampleMediaStoreId { get; set; }
    /// <summary>从该艺术家第一首歌曲获取的文件路径，用于通过 MediaStore 查询封面</summary>
    public string? SampleFilePath { get; set; }
}

/// <summary>专辑及其歌曲数量</summary>
public class AlbumWithCount : INotifyPropertyChanged
{
    /// <summary>属性变更事件，用于封面在后台解析完成后通知绑定刷新</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>触发属性变更通知</summary>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>专辑数据库 ID</summary>
    public int Id { get; set; }
    /// <summary>专辑标题</summary>
    public string Title { get; set; } = "";
    /// <summary>专辑封面图本地路径（内嵌封面提取后写入缓存）</summary>
    public string? CoverArtPath
    {
        get => _coverArtPath;
        set { if (_coverArtPath != value) { _coverArtPath = value; OnPropertyChanged(); } }
    }
    private string? _coverArtPath;
    /// <summary>专辑封面 URL</summary>
    public string? Cover { get; set; }
    /// <summary>专辑所属艺术家名称</summary>
    public string ArtistName { get; set; } = "";
    /// <summary>该专辑的歌曲总数</summary>
    public int SongCount { get; set; }
    /// <summary>发行年份</summary>
    public int? Year { get; set; }
    /// <summary>从该专辑第一首歌曲获取的封面路径，用于列表页快速显示</summary>
    public string? SampleCoverPath { get; set; }
    /// <summary>从该专辑第一首歌曲获取的歌曲 ID，用于解析封面缓存</summary>
    public int SampleSongId { get; set; }
    /// <summary>从该专辑第一首歌曲获取的 MediaStoreId，用于快速加载封面</summary>
    public long SampleMediaStoreId { get; set; }
    /// <summary>从该专辑第一首歌曲获取的文件路径，用于通过 MediaStore 查询封面</summary>
    public string? SampleFilePath { get; set; }
}
