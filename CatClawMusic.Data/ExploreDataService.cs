using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

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
        }
    }

    /// <summary>
    /// 使每日推荐缓存失效：清除内存缓存和磁盘缓存。
    /// 在音乐库扫描完成、歌曲发生变化后调用，确保探索页展示最新数据。
    /// </summary>
    public void InvalidateDailyRecommendCache()
    {
        _dailyRecommendCache = null;
        _dailyRecommendDate = null;
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
        var diskCache = await LoadDailyRecommendFromDiskAsync(today);
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
        var allArtists = await GetAllArtistsWithCountInternalAsync();

        // 尝试从磁盘缓存恢复艺人 ID 列表
        var diskCache = await LoadDailyCacheFromDiskAsync(today);
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
            new List<int>());

        return selected;
    }

    /// <summary>获取所有艺术家及其歌曲数量（内部方法，不缓存）</summary>
    private async Task<List<ArtistWithCount>> GetAllArtistsWithCountInternalAsync()
    {
        await _db.EnsureInitializedAsync();
        var artists = await _db.GetAllArtistsAsync();
        var songs = await GetFilteredSongsAsync();

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
                var allSongArtists = await _db.QuerySongArtistsBySongIdsAsync(songIdSet);
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
        var allAlbums = await GetAllAlbumsWithCountInternalAsync();

        // 尝试从磁盘缓存恢复专辑 ID 列表
        var diskCache = await LoadDailyCacheFromDiskAsync(today);
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
            selected.Select(a => a.Id).ToList());

        return selected;
    }

    /// <summary>获取所有专辑及歌曲数量（内部方法，不缓存）</summary>
    private async Task<List<AlbumWithCount>> GetAllAlbumsWithCountInternalAsync()
    {
        await _db.EnsureInitializedAsync();
        var albums = await _db.GetAllAlbumsAsync();
        var songs = await GetFilteredSongsAsync();
        var artists = await _db.GetAllArtistsAsync();
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
                    SongCount = albumSongCount.GetValueOrDefault(a.Id, 0)
                };
                if (albumSampleCover.TryGetValue(a.Id, out var sample))
                {
                    result.SampleCoverPath = sample.CoverArtPath;
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
        return await GetAllAlbumsWithCountInternalAsync();
    }

    /// <summary>获取所有艺术家列表（含歌曲数量）</summary>
    public async Task<List<ArtistWithCount>> GetAllArtistsAsync()
    {
        return await GetAllArtistsWithCountInternalAsync();
    }

    /// <summary>获取经过来源筛选和协议过滤的全部歌曲（含 PlayCount）</summary>
    private async Task<List<Song>> GetFilteredSongsAsync()
    {
        // 使用 GetMergedSongsAsync 获取本地+网络歌曲（已去重、已过滤协议）
        var allSongs = await _library.GetMergedSongsAsync();
        var filtered = ApplySourceFilter(allSongs);

        // 补充 PlayCount 数据
        await FillPlayCountAsync(filtered);

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

    /// <summary>从 PlayHistory 表填充歌曲的播放次数</summary>
    private async Task FillPlayCountAsync(List<Song> songs)
    {
        try
        {
            await _db.EnsureInitializedAsync();
            var history = await _db.GetRecentPlaysAsync(10000);
            var dict = history.ToDictionary(h => h.SongId, h => h.PlayCount);
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
public class ArtistWithCount
{
    /// <summary>艺术家数据库 ID</summary>
    public int Id { get; set; }
    /// <summary>艺术家名称</summary>
    public string Name { get; set; } = "";
    /// <summary>艺术家封面 URL（来自元数据刮削）</summary>
    public string? Cover { get; set; }
    /// <summary>该艺术家的歌曲总数（含合作歌曲）</summary>
    public int SongCount { get; set; }
    /// <summary>从该艺术家第一首歌曲获取的封面路径，用于列表页快速显示</summary>
    public string? SampleCoverPath { get; set; }
    /// <summary>从该艺术家第一首歌曲获取的 MediaStoreId，用于快速加载封面</summary>
    public long SampleMediaStoreId { get; set; }
    /// <summary>从该艺术家第一首歌曲获取的文件路径，用于通过 MediaStore 查询封面</summary>
    public string? SampleFilePath { get; set; }
}

/// <summary>专辑及其歌曲数量</summary>
public class AlbumWithCount
{
    /// <summary>专辑数据库 ID</summary>
    public int Id { get; set; }
    /// <summary>专辑标题</summary>
    public string Title { get; set; } = "";
    /// <summary>专辑封面图本地路径</summary>
    public string? CoverArtPath { get; set; }
    /// <summary>专辑封面 URL</summary>
    public string? Cover { get; set; }
    /// <summary>专辑所属艺术家名称</summary>
    public string ArtistName { get; set; } = "";
    /// <summary>该专辑的歌曲总数</summary>
    public int SongCount { get; set; }
    /// <summary>从该专辑第一首歌曲获取的封面路径，用于列表页快速显示</summary>
    public string? SampleCoverPath { get; set; }
    /// <summary>从该专辑第一首歌曲获取的 MediaStoreId，用于快速加载封面</summary>
    public long SampleMediaStoreId { get; set; }
    /// <summary>从该专辑第一首歌曲获取的文件路径，用于通过 MediaStore 查询封面</summary>
    public string? SampleFilePath { get; set; }
}
