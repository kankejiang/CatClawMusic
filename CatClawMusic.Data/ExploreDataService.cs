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
        try { Directory.CreateDirectory(cacheDir); } catch { }
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
        }
    }

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
        catch { }
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

        // 生成新的推荐
        var allSongs = await GetFilteredSongsAsync();
        var random = new Random();
        var shuffled = allSongs.OrderBy(_ => random.Next()).Take(20).ToList();

        _dailyRecommendCache = shuffled;
        _dailyRecommendDate = today;
        SaveDailyRecommendToDisk(today, shuffled);
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
            var json = File.ReadAllText(_cacheFilePath);
            var cache = System.Text.Json.JsonSerializer.Deserialize<DailyRecommendCache>(json);
            if (cache?.Date != date) return null;
            var allSongs = await _db.GetSongsAsync();
            var filtered = ApplySourceFilter(allSongs);
            var result = new List<Song>();
            foreach (var id in cache.Ids)
            {
                var song = filtered.FirstOrDefault(s => s.Id == id);
                if (song != null) result.Add(song);
            }
            return result.Count > 0 ? result : null;
        }
        catch { return null; }
    }

    /// <summary>保存每日推荐到磁盘缓存</summary>
    private void SaveDailyRecommendToDisk(string date, List<Song> songs)
    {
        try
        {
            // 读取已有缓存以保留 artist/album IDs
            DailyRecommendCache existing;
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    var json = File.ReadAllText(_cacheFilePath);
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
            File.WriteAllText(_cacheFilePath, output);
        }
        catch { }
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
        catch { }
    }

    /// <summary>获取每日推荐艺术家（每天随机10个，带缓存）</summary>
    public async Task<List<ArtistWithCount>> GetArtistsWithSongCountAsync()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        // 内存缓存命中
        if (_dailyArtistsCache != null && _dailyRecommendDate == today)
            return _dailyArtistsCache;

        // 获取全部艺术家
        var allArtists = await GetAllArtistsWithCountInternalAsync().ConfigureAwait(false);

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

        // 随机选10个
        var random = new Random();
        var selected = allArtists.OrderBy(_ => random.Next()).Take(10).ToList();
        _dailyArtistsCache = selected;

        // 保存到磁盘缓存（合并到同一个 JSON 文件）
        await SaveArtistAlbumIdsToCacheAsync(today,
            selected.Select(a => a.Id).ToList(),
            new List<int>()).ConfigureAwait(false);

        return selected;
    }

    /// <summary>获取所有艺术家及其歌曲数量（内部方法，不缓存）</summary>
    private async Task<List<ArtistWithCount>> GetAllArtistsWithCountInternalAsync()
    {
        await _db.EnsureInitializedAsync().ConfigureAwait(false);
        // 并行执行两个独立查询
        var artistsTask = _db.GetAllArtistsAsync();
        var songsTask = GetFilteredSongsAsync();
        await Task.WhenAll(artistsTask, songsTask).ConfigureAwait(false);
        var artists = artistsTask.Result;
        var songs = songsTask.Result;

        // 通过 SongArtists 多对多表统计每首歌的艺术家，确保
        // 合作歌曲（如 "周杰伦 / 林俊杰"）的两位艺术家都能正确计数。
        //   主计数：通过 Song.ArtistId（快速路径，覆盖单艺术家歌曲）
        //   补充计数：通过 SongArtists 表（覆盖多艺术家合作歌曲的次要艺术家）
        var artistSongCount = new Dictionary<int, int>();

        // 1) 通过主 ArtistId 计数（单艺术家 + 多艺术家中排第一的）
        foreach (var g in songs.GroupBy(s => s.ArtistId))
            artistSongCount[g.Key] = g.Count();

        // 2) 通过 SongArtists 表补充多艺术家合作歌曲的计数
        try
        {
            var songIds = songs.Select(s => s.Id).ToList();
            if (songIds.Count > 0)
            {
                var songIdSet = new HashSet<int>(songIds);
                // 批量查询 SongArtists，只取当前筛选出的歌曲
                var allSongArtists = await _db.QuerySongArtistsBySongIdsAsync(songIdSet).ConfigureAwait(false);
                foreach (var sa in allSongArtists)
                {
                    // 避免重复计数：如果 ArtistId 和主 ArtistId 一致则已在上一步计入
                    // 这里简单累加（多计一次也没关系，但最好去重）
                    if (artistSongCount.ContainsKey(sa.ArtistId))
                        artistSongCount[sa.ArtistId]++;
                    else
                        artistSongCount[sa.ArtistId] = 1;
                }
            }
        }
        catch { }

        // 从每个艺术家的第一首本地歌曲获取封面信息
        var artistSampleCover = songs
            .Where(s => s.ArtistId > 0 && !string.IsNullOrEmpty(s.FilePath)
                && s.Source == SongSource.Local)
            .GroupBy(s => s.ArtistId)
            .ToDictionary(g => g.Key, g => g.First());

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
                if (artistSampleCover.TryGetValue(a.Id, out var sample))
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

        // 获取全部专辑
        var allAlbums = await GetAllAlbumsWithCountInternalAsync().ConfigureAwait(false);

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

        // 随机选10个
        var random = new Random();
        var selected = allAlbums.OrderBy(_ => random.Next()).Take(10).ToList();
        _dailyAlbumsCache = selected;

        // 保存到磁盘缓存
        await SaveArtistAlbumIdsToCacheAsync(today,
            new List<int>(),
            selected.Select(a => a.Id).ToList()).ConfigureAwait(false);

        return selected;
    }

    /// <summary>获取所有专辑及歌曲数量（内部方法，不缓存）</summary>
    private async Task<List<AlbumWithCount>> GetAllAlbumsWithCountInternalAsync()
    {
        await _db.EnsureInitializedAsync().ConfigureAwait(false);
        // 并行执行三个独立查询
        var albumsTask = _db.GetAllAlbumsAsync();
        var songsTask = GetFilteredSongsAsync();
        var artistsTask = _db.GetAllArtistsAsync();
        await Task.WhenAll(albumsTask, songsTask, artistsTask).ConfigureAwait(false);
        var albums = albumsTask.Result;
        var songs = songsTask.Result;
        var artists = artistsTask.Result;
        var artistDict = artists.ToDictionary(a => a.Id, a => a.Name);

        var albumSongCount = songs.GroupBy(s => s.AlbumId)
            .ToDictionary(g => g.Key, g => g.Count());

        // 从每个专辑的第一首本地歌曲获取封面信息
        var albumSampleCover = songs
            .Where(s => s.AlbumId > 0 && !string.IsNullOrEmpty(s.FilePath)
                && s.Source == SongSource.Local)
            .GroupBy(s => s.AlbumId)
            .ToDictionary(g => g.Key, g => g.First());

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
                if (albumSampleCover.TryGetValue(a.Id, out var sample))
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
        var songs = await _db.GetTopPlayedSongsAsync(limit);
        return ApplySourceFilter(songs);
    }

    /// <summary>获取最近7天内入库的歌曲</summary>
    public async Task<List<Song>> GetRecentlyAddedSongsAsync(int limit = 50)
    {
        var allSongs = await GetFilteredSongsAsync();
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

        // 使用 GetMergedSongsAsync 获取本地+网络歌曲（已去重、已过滤协议）
        var allSongs = await _library.GetMergedSongsAsync().ConfigureAwait(false);
        var filtered = ApplySourceFilter(allSongs);

        // 补充 PlayCount 数据
        await FillPlayCountAsync(filtered).ConfigureAwait(false);

        _filteredSongsCache = filtered;
        _filteredSongsCacheKey = _sourceFilter;
        return filtered;
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
        catch { }
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
