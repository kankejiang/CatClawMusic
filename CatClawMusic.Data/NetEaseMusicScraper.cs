using CatClawMusic.Core.Models;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Data;

/// <summary>
/// 网易云音乐元数据刮削服务，用于获取艺术家头像
/// </summary>
public class NetEaseMusicScraper : IArtistMetadataScraper
{
    /// <summary>HTTP 客户端，用于访问网易云 API</summary>
    private readonly HttpClient _httpClient;
    /// <summary>数据库访问实例，用于查询艺术家封面缓存</summary>
    private readonly MusicDatabase _db;
    /// <summary>艺术家封面缓存目录绝对路径</summary>
    private readonly string _artistCoverCacheDir;
    /// <summary>专辑封面缓存目录绝对路径</summary>
    private readonly string _albumCoverCacheDir;

    /// <summary>数据源名称：网易云</summary>
    public string SourceName => "网易云";

    /// <summary>艺术家详细信息</summary>
    public class ArtistInfo
    {
        public string? Alias { get; set; }
        public string? Country { get; set; }
        public string? Description { get; set; }
        public string? Gender { get; set; }
        public string? Birthday { get; set; }
    }

    /// <summary>专辑详细信息</summary>
    public class AlbumInfo
    {
        public string? Description { get; set; }
        public string? Year { get; set; }
        public string? CoverUrl { get; set; }
    }

    /// <summary>
    /// 初始化网易云音乐刮削器。
    /// </summary>
    /// <param name="db">数据库访问实例，用于查询已有艺术家封面。</param>
    /// <param name="artistCoverCacheDir">艺术家封面缓存目录（不存在会自动创建）。</param>
    /// <param name="albumCoverCacheDir">专辑封面缓存目录（不存在会自动创建）。</param>
    public NetEaseMusicScraper(MusicDatabase db, string artistCoverCacheDir, string albumCoverCacheDir)
    {
        _db = db;
        _artistCoverCacheDir = artistCoverCacheDir;
        Directory.CreateDirectory(_artistCoverCacheDir);
        _albumCoverCacheDir = albumCoverCacheDir;
        Directory.CreateDirectory(_albumCoverCacheDir);

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _httpClient.DefaultRequestHeaders.Add("Referer", "http://music.163.com/");
        _httpClient.DefaultRequestHeaders.Add("Cookie", "os=pc; appver=1.5.0.75771");
    }

    /// <summary>获取艺术家封面图片路径（优先缓存，否则从网易云搜索）</summary>
    public async Task<string?> GetArtistCoverAsync(string artistName)
    {
        // 1. 检查本地缓存文件
        var cachePath = GetArtistCoverPath(artistName);
        if (File.Exists(cachePath)) return cachePath;

        // 2. 检查数据库中的 Cover 字段
        try
        {
            var artists = await _db.GetAllArtistsAsync();
            var artist = artists.FirstOrDefault(a => a.Name == artistName);
            if (artist?.Cover != null && File.Exists(artist.Cover))
            {
                // 复制到缓存
                File.Copy(artist.Cover, cachePath, true);
                return cachePath;
            }
        }
        catch { }

        // 3. 从网易云搜索
        try
        {
            var neteaseId = await SearchArtistAsync(artistName);
            if (neteaseId == null) return null;

            var coverUrl = await GetArtistCoverUrlAsync(neteaseId.Value);
            if (string.IsNullOrEmpty(coverUrl)) return null;

            // 下载并缓存
            var bytes = await _httpClient.GetByteArrayAsync(coverUrl);
            if (bytes.Length > 0)
            {
                await File.WriteAllBytesAsync(cachePath, bytes);
                return cachePath;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("NetEaseMusicScraper", $"[NetEaseScraper] 获取艺术家封面失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>批量刮削所有艺术家封面</summary>
    public async Task ScrapeAllArtistCoversAsync(IProgress<(string name, int current, int total)>? progress = null)
    {
        var artists = await _db.GetAllArtistsAsync();
        var total = artists.Count;

        for (var i = 0; i < total; i++)
        {
            var artist = artists[i];
            progress?.Report((artist.Name, i + 1, total));

            var cachePath = GetArtistCoverPath(artist.Name);
            if (File.Exists(cachePath)) continue;

            try
            {
                await GetArtistCoverAsync(artist.Name);
                await Task.Delay(300); // 避免请求过快
            }
            catch { }
        }
    }

    /// <summary>获取艺术家封面缓存路径（不发起网络请求）</summary>
    public string? GetCachedCoverPath(string artistName)
    {
        var cachePath = GetArtistCoverPath(artistName);
        return File.Exists(cachePath) ? cachePath : null;
    }

    /// <summary>获取艺术家详细信息（别名、国家/地区、简介），从网易云API刮削</summary>
    public async Task<ArtistInfo?> GetArtistInfoAsync(string artistName)
    {
        try
        {
            var neteaseId = await SearchArtistAsync(artistName);
            if (neteaseId == null) return null;

            var url = $"http://music.163.com/api/artist/albums/{neteaseId}?id={neteaseId}&offset=0&total=true&limit=1";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            var info = new ArtistInfo();

            if (doc.RootElement.TryGetProperty("artist", out var artist))
            {
                // 别名
                if (artist.TryGetProperty("transNames", out var transNames) && transNames.ValueKind == JsonValueKind.Array)
                {
                    var aliases = transNames.EnumerateArray()
                        .Select(a => a.GetString())
                        .Where(a => !string.IsNullOrEmpty(a))
                        .ToList();
                    if (aliases.Count > 0)
                        info.Alias = string.Join(" / ", aliases);
                }
                if (string.IsNullOrEmpty(info.Alias) && artist.TryGetProperty("alias", out var aliasArr) && aliasArr.ValueKind == JsonValueKind.Array)
                {
                    var aliases = aliasArr.EnumerateArray()
                        .Select(a => a.GetString())
                        .Where(a => !string.IsNullOrEmpty(a))
                        .ToList();
                    if (aliases.Count > 0)
                        info.Alias = string.Join(" / ", aliases);
                }

                // 国家/地区
                if (artist.TryGetProperty("country", out var country) && country.ValueKind == JsonValueKind.String)
                    info.Country = country.GetString();
                if (string.IsNullOrEmpty(info.Country) && artist.TryGetProperty("area", out var area))
                {
                    if (area.ValueKind == JsonValueKind.Number)
                    {
                        var areaCode = area.GetInt32();
                        info.Country = areaCode switch
                        {
                            1 => "中国",
                            2 => "日本",
                            3 => "韩国",
                            4 => "欧美",
                            5 => "其他",
                            _ => ""
                        };
                    }
                    else
                    {
                        info.Country = area.GetString();
                    }
                }

                // 简介
                if (artist.TryGetProperty("briefDesc", out var briefDesc))
                    info.Description = briefDesc.GetString();
                if (string.IsNullOrEmpty(info.Description) && artist.TryGetProperty("description", out var desc))
                    info.Description = desc.GetString();

                // 性别
                if (artist.TryGetProperty("gender", out var gender) && gender.ValueKind == JsonValueKind.Number)
                {
                    var g = gender.GetInt32();
                    info.Gender = g switch { 1 => "男", 2 => "女", _ => "" };
                }
                else if (artist.TryGetProperty("sex", out var sex) && sex.ValueKind == JsonValueKind.Number)
                {
                    var s = sex.GetInt32();
                    info.Gender = s switch { 1 => "男", 2 => "女", _ => "" };
                }

                // 生日
                if (artist.TryGetProperty("birthday", out var birthdateVal))
                    info.Birthday = birthdateVal.GetString();
                if (string.IsNullOrEmpty(info.Birthday) && artist.TryGetProperty("birth", out var birthVal))
                    info.Birthday = birthVal.GetString();
            }

            return info;
        }
        catch (Exception ex)
        {
            Log.Debug("NetEaseMusicScraper", $"[NetEaseScraper] 获取艺术家信息失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>搜索结果项</summary>
    public class SearchResult
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string? CoverUrl { get; set; }
        public string? Alias { get; set; }
        public int SongCount { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>搜索艺术家，返回多个匹配结果（供用户手动选择）</summary>
    public async Task<List<SearchResult>> SearchArtistsAsync(string name, int limit = 10)
    {
        var results = new List<SearchResult>();
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["s"] = name,
                ["type"] = "100",
                ["offset"] = "0",
                ["limit"] = limit.ToString()
            });

            var response = await _httpClient.PostAsync("http://music.163.com/api/search/pc", content);
            if (!response.IsSuccessStatusCode) return results;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("result", out var result)) return results;
            if (!result.TryGetProperty("artists", out var artists)) return results;

            foreach (var artist in artists.EnumerateArray())
            {
                var item = new SearchResult();
                if (artist.TryGetProperty("id", out var idProp))
                    item.Id = idProp.GetInt64();
                if (artist.TryGetProperty("name", out var nameProp))
                    item.Name = nameProp.GetString() ?? "";
                if (artist.TryGetProperty("picUrl", out var picUrl))
                    item.CoverUrl = picUrl.GetString();
                if (artist.TryGetProperty("alias", out var aliasArr) && aliasArr.ValueKind == JsonValueKind.Array)
                {
                    var aliases = aliasArr.EnumerateArray()
                        .Select(a => a.GetString())
                        .Where(a => !string.IsNullOrEmpty(a))
                        .ToList();
                    if (aliases.Count > 0) item.Alias = string.Join(" / ", aliases);
                }
                if (artist.TryGetProperty("albumSize", out var albumSize))
                    item.SongCount = albumSize.GetInt32();

                results.Add(item);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("NetEaseMusicScraper", $"[NetEaseScraper] 搜索艺术家列表失败: {ex.Message}");
        }
        return results;
    }

    /// <summary>下载并保存艺术家封面到缓存</summary>
    public async Task<string?> DownloadAndCacheArtistCoverAsync(string coverUrl, string artistName)
    {
        try
        {
            var cachePath = GetArtistCoverPath(artistName);
            var bytes = await _httpClient.GetByteArrayAsync(coverUrl);
            if (bytes.Length > 0)
            {
                await File.WriteAllBytesAsync(cachePath, bytes);
                return cachePath;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("NetEaseMusicScraper", $"[NetEaseScraper] 下载艺术家封面失败: {ex.Message}");
        }
        return null;
    }

    /// <summary>搜索艺术家，返回网易云艺术家 ID</summary>
    private async Task<long?> SearchArtistAsync(string name)
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["s"] = name,
                ["type"] = "100",
                ["offset"] = "0",
                ["limit"] = "5"
            });

            var response = await _httpClient.PostAsync("http://music.163.com/api/search/pc", content);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("result", out var result)) return null;
            if (!result.TryGetProperty("artists", out var artists)) return null;

            // 优先精确匹配
            foreach (var artist in artists.EnumerateArray())
            {
                if (artist.TryGetProperty("name", out var nameProp) &&
                    nameProp.GetString() == name &&
                    artist.TryGetProperty("id", out var idProp))
                    return idProp.GetInt64();
            }

            // 没有精确匹配则返回第一个
            var first = artists.EnumerateArray().FirstOrDefault();
            if (first.TryGetProperty("id", out var firstId))
                return firstId.GetInt64();
        }
        catch (Exception ex)
        {
            Log.Debug("NetEaseMusicScraper", $"[NetEaseScraper] 搜索艺术家失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>获取艺术家封面 URL</summary>
    private async Task<string?> GetArtistCoverUrlAsync(long artistId)
    {
        try
        {
            var url = $"http://music.163.com/api/artist/albums/{artistId}?id={artistId}&offset=0&total=true&limit=1";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("artist", out var artist))
            {
                if (artist.TryGetProperty("picUrl", out var picUrl))
                {
                    var picUrlStr = picUrl.GetString();
                    if (!string.IsNullOrEmpty(picUrlStr)) return picUrlStr;
                }
            }

            // 从专辑封面获取
            if (doc.RootElement.TryGetProperty("hotAlbums", out var albums))
            {
                foreach (var album in albums.EnumerateArray())
                {
                    if (album.TryGetProperty("picUrl", out var albumPic))
                    {
                        var albumPicStr = albumPic.GetString();
                        if (!string.IsNullOrEmpty(albumPicStr)) return albumPicStr;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("NetEaseMusicScraper", $"[NetEaseScraper] 获取艺术家封面URL失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 根据艺术家名称生成封面缓存文件路径。
    /// 文件名中的非法字符会被替换为下划线。
    /// </summary>
    /// <param name="artistName">艺术家名称。</param>
    /// <returns>缓存文件绝对路径（如 /cache/周杰伦.jpg）。</returns>
    private string GetArtistCoverPath(string artistName)
    {
        var safeName = string.Join("_", artistName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_artistCoverCacheDir, $"{safeName}.jpg");
    }

    /// <summary>获取专辑封面图片路径（优先缓存，否则从网易云搜索）</summary>
    public async Task<string?> GetAlbumCoverAsync(string albumTitle, string? albumArtist = null)
    {
        // 1. 检查本地缓存文件
        var cachePath = GetAlbumCoverPath(albumTitle);
        if (File.Exists(cachePath)) return cachePath;

        // 2. 从网易云搜索
        try
        {
            var neteaseId = await SearchAlbumAsync(albumTitle, albumArtist);
            if (neteaseId == null) return null;

            // 从搜索结果获取 picUrl
            var coverUrl = await GetAlbumCoverUrlAsync(neteaseId.Value);
            if (string.IsNullOrEmpty(coverUrl)) return null;

            // 下载并缓存
            var bytes = await _httpClient.GetByteArrayAsync(coverUrl);
            if (bytes.Length > 0)
            {
                await File.WriteAllBytesAsync(cachePath, bytes);
                return cachePath;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("NetEaseMusicScraper", $"[NetEaseScraper] 获取专辑封面失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>获取专辑详细信息（简介、年份、封面URL），从网易云API刮削</summary>
    public async Task<AlbumInfo?> GetAlbumInfoAsync(string albumTitle, string? albumArtist = null)
    {
        try
        {
            var neteaseId = await SearchAlbumAsync(albumTitle, albumArtist);
            if (neteaseId == null) return null;

            var url = $"http://music.163.com/api/album/{neteaseId}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            var info = new AlbumInfo();

            if (doc.RootElement.TryGetProperty("album", out var album))
            {
                // 简介
                if (album.TryGetProperty("briefDesc", out var briefDesc))
                    info.Description = briefDesc.GetString();
                if (string.IsNullOrEmpty(info.Description) && album.TryGetProperty("description", out var desc))
                    info.Description = desc.GetString();

                // 发行年份
                if (album.TryGetProperty("publishTime", out var publishTime))
                {
                    var ts = publishTime.GetInt64();
                    info.Year = DateTimeOffset.FromUnixTimeMilliseconds(ts).Year.ToString();
                }

                // 封面URL
                if (album.TryGetProperty("picUrl", out var picUrl))
                    info.CoverUrl = picUrl.GetString();
            }

            return info;
        }
        catch (Exception ex)
        {
            Log.Debug("NetEaseMusicScraper", $"[NetEaseScraper] 获取专辑信息失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>搜索专辑，返回网易云专辑 ID</summary>
    private async Task<long?> SearchAlbumAsync(string albumTitle, string? albumArtist = null)
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["s"] = albumTitle,
                ["type"] = "10",
                ["offset"] = "0",
                ["limit"] = "5"
            });

            var response = await _httpClient.PostAsync("http://music.163.com/api/search/pc", content);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("result", out var result)) return null;
            if (!result.TryGetProperty("albums", out var albums)) return null;

            // 优先精确匹配标题（可选也匹配艺术家名）
            foreach (var album in albums.EnumerateArray())
            {
                if (!album.TryGetProperty("name", out var nameProp) || nameProp.GetString() != albumTitle)
                    continue;
                if (!album.TryGetProperty("id", out var idProp))
                    continue;

                // 如果提供了艺术家名，尝试匹配
                if (!string.IsNullOrEmpty(albumArtist) &&
                    album.TryGetProperty("artist", out var artist) &&
                    artist.TryGetProperty("name", out var artistName))
                {
                    if (artistName.GetString() == albumArtist)
                        return idProp.GetInt64();
                }
                else
                {
                    return idProp.GetInt64();
                }
            }

            // 没有精确匹配则返回第一个
            var first = albums.EnumerateArray().FirstOrDefault();
            if (first.TryGetProperty("id", out var firstId))
                return firstId.GetInt64();
        }
        catch (Exception ex)
        {
            Log.Debug("NetEaseMusicScraper", $"[NetEaseScraper] 搜索专辑失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>获取专辑封面 URL</summary>
    private async Task<string?> GetAlbumCoverUrlAsync(long albumId)
    {
        try
        {
            var url = $"http://music.163.com/api/album/{albumId}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("album", out var album))
            {
                if (album.TryGetProperty("picUrl", out var picUrl))
                {
                    var picUrlStr = picUrl.GetString();
                    if (!string.IsNullOrEmpty(picUrlStr)) return picUrlStr;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("NetEaseMusicScraper", $"[NetEaseScraper] 获取专辑封面URL失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>获取专辑封面缓存路径</summary>
    private string GetAlbumCoverPath(string albumTitle)
    {
        var safeName = string.Join("_", albumTitle.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_albumCoverCacheDir, $"{safeName}.jpg");
    }

    /// <summary>
    /// 显式实现 IArtistMetadataScraper.SearchArtistsAsync，
    /// 转发至内部方法以将网易云原生结果转换为统一模型。
    /// </summary>
    Task<List<ArtistSearchResult>> IArtistMetadataScraper.SearchArtistsAsync(string name, int limit)
    {
        return SearchArtistsInternalAsync(name, limit);
    }

    /// <summary>
    /// 内部实现：将网易云原生的 SearchResult 列表转换为统一的 ArtistSearchResult 列表。
    /// 由 IArtistMetadataScraper.SearchArtistsAsync 显式实现调用。
    /// </summary>
    /// <param name="name">艺术家名称关键词。</param>
    /// <param name="limit">最大返回数量。</param>
    /// <returns>转换后的统一搜索结果列表。</returns>
    private async Task<List<ArtistSearchResult>> SearchArtistsInternalAsync(string name, int limit)
    {
        var oldResults = await SearchArtistsAsync(name, limit);
        return oldResults.Select(r => new ArtistSearchResult
        {
            Source = SourceName,
            Id = r.Id.ToString(),
            Name = r.Name,
            CoverUrl = r.CoverUrl,
            Alias = r.Alias,
            Gender = null,
            Region = null,
            Description = r.Description,
            ExtraInfo = r.SongCount > 0 ? $"{r.SongCount} 张专辑" : null
        }).ToList();
    }
}
