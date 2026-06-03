using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

/// <summary>
/// 探索页面数据服务，封装每日推荐、艺术家、专辑、最多播放、最新音乐等查询
/// </summary>
public class ExploreDataService
{
    private readonly MusicDatabase _db;
    private readonly MusicLibraryService _library;
    private readonly string _cacheFilePath;

    /// <summary>每日推荐缓存：Key 为日期字符串 "yyyy-MM-dd"，Value 为歌曲列表</summary>
    private string? _dailyRecommendDate;
    private List<Song>? _dailyRecommendCache;

    /// <summary>来源筛选：all, local, network</summary>
    private string _sourceFilter = "all";

    public ExploreDataService(MusicDatabase db, MusicLibraryService library, string cacheDir)
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
        var diskCache = LoadDailyRecommendFromDisk(today);
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

    /// <summary>从磁盘缓存加载每日推荐</summary>
    private List<Song>? LoadDailyRecommendFromDisk(string date)
    {
        try
        {
            if (!File.Exists(_cacheFilePath)) return null;
            var json = File.ReadAllText(_cacheFilePath);
            var cache = System.Text.Json.JsonSerializer.Deserialize<DailyRecommendCache>(json);
            if (cache?.Date != date) return null;
            var allSongs = _db.GetSongsAsync().GetAwaiter().GetResult();
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

    /// <summary>获取所有艺术家及其歌曲数量</summary>
    public async Task<List<ArtistWithCount>> GetArtistsWithSongCountAsync()
    {
        await _db.EnsureInitializedAsync();
        var artists = await _db.GetAllArtistsAsync();
        var songs = await GetFilteredSongsAsync();

        var artistSongCount = songs.GroupBy(s => s.ArtistId)
            .ToDictionary(g => g.Key, g => g.Count());

        return artists
            .Where(a => artistSongCount.ContainsKey(a.Id))
            .Select(a => new ArtistWithCount
            {
                Id = a.Id,
                Name = a.Name,
                Cover = a.Cover,
                SongCount = artistSongCount.GetValueOrDefault(a.Id, 0)
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

        return albums
            .Where(a => albumSongCount.ContainsKey(a.Id))
            .Select(a => new AlbumWithCount
            {
                Id = a.Id,
                Title = a.Title,
                CoverArtPath = a.CoverArtPath,
                Cover = a.Cover,
                ArtistName = a.ArtistId > 0 ? artistDict.GetValueOrDefault(a.ArtistId, "未知艺术家") : "未知艺术家",
                SongCount = albumSongCount.GetValueOrDefault(a.Id, 0)
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
}
