using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

/// <summary>
/// 探索页面数据服务，封装每日推荐、艺术家、专辑、最多播放、最新音乐等查询
/// </summary>
public class ExploreDataService
{
    private readonly MusicDatabase _db;
    private readonly IMusicLibraryService _library;
    private readonly string _cacheFilePath;

    /// <summary>每日推荐缓存：Key 为日期字符串 "yyyy-MM-dd"，Value 为歌曲列表</summary>
    private string? _dailyRecommendDate;
    private List<Song>? _dailyRecommendCache;

    /// <summary>来源筛选：all, local, network</summary>
    private string _sourceFilter = "all";

    public ExploreDataService(MusicDatabase db, IMusicLibraryService library, string cacheDir)
    {
        _db = db;
        _library = library;
        _cacheFilePath = Path.Combine(cacheDir, "daily_recommend.json");
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
            var cache = new DailyRecommendCache
            {
                Date = date,
                Ids = songs.Select(s => s.Id).ToList()
            };
            var json = System.Text.Json.JsonSerializer.Serialize(cache);
            File.WriteAllText(_cacheFilePath, json);
        }
        catch { }
    }

    /// <summary>获取所有艺术家及其歌曲数量（通过 SongArtists 多对多表计数）</summary>
    public async Task<List<ArtistWithCount>> GetArtistsWithSongCountAsync()
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

    /// <summary>获取所有专辑及其歌曲数量</summary>
    public async Task<List<AlbumWithCount>> GetAlbumsWithSongCountAsync()
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

    private class DailyRecommendCache
    {
        public string Date { get; set; } = "";
        public List<int> Ids { get; set; } = new();
    }
}

/// <summary>艺术家及其歌曲数量</summary>
public class ArtistWithCount
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Cover { get; set; }
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
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? CoverArtPath { get; set; }
    public string? Cover { get; set; }
    public string ArtistName { get; set; } = "";
    public int SongCount { get; set; }
    /// <summary>从该专辑第一首歌曲获取的封面路径，用于列表页快速显示</summary>
    public string? SampleCoverPath { get; set; }
    /// <summary>从该专辑第一首歌曲获取的 MediaStoreId，用于快速加载封面</summary>
    public long SampleMediaStoreId { get; set; }
    /// <summary>从该专辑第一首歌曲获取的文件路径，用于通过 MediaStore 查询封面</summary>
    public string? SampleFilePath { get; set; }
}
