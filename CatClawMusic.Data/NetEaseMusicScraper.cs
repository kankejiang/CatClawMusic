using CatClawMusic.Core.Models;
using System.Text.Json;

namespace CatClawMusic.Data;

/// <summary>
/// 网易云音乐元数据刮削服务，用于获取艺术家头像
/// </summary>
public class NetEaseMusicScraper
{
    private readonly HttpClient _httpClient;
    private readonly MusicDatabase _db;
    private readonly string _artistCoverCacheDir;
    private readonly string _albumCoverCacheDir;

    /// <summary>艺术家详细信息</summary>
    public class ArtistInfo
    {
        public string? Alias { get; set; }
        public string? Country { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>专辑详细信息</summary>
    public class AlbumInfo
    {
        public string? Description { get; set; }
        public string? Year { get; set; }
        public string? CoverUrl { get; set; }
    }

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
            System.Diagnostics.Debug.WriteLine($"[NetEaseScraper] 获取艺术家封面失败: {ex.Message}");
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
                if (artist.TryGetProperty("country", out var country))
                    info.Country = country.GetString();
                if (string.IsNullOrEmpty(info.Country) && artist.TryGetProperty("area", out var area))
                    info.Country = area.GetString();

                // 简介
                if (artist.TryGetProperty("briefDesc", out var briefDesc))
                    info.Description = briefDesc.GetString();
                if (string.IsNullOrEmpty(info.Description) && artist.TryGetProperty("description", out var desc))
                    info.Description = desc.GetString();
            }

            return info;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NetEaseScraper] 获取艺术家信息失败: {ex.Message}");
            return null;
        }
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
            System.Diagnostics.Debug.WriteLine($"[NetEaseScraper] 搜索艺术家失败: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"[NetEaseScraper] 获取艺术家封面URL失败: {ex.Message}");
        }

        return null;
    }

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
            System.Diagnostics.Debug.WriteLine($"[NetEaseScraper] 获取专辑封面失败: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"[NetEaseScraper] 获取专辑信息失败: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"[NetEaseScraper] 搜索专辑失败: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"[NetEaseScraper] 获取专辑封面URL失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>获取专辑封面缓存路径</summary>
    private string GetAlbumCoverPath(string albumTitle)
    {
        var safeName = string.Join("_", albumTitle.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_albumCoverCacheDir, $"{safeName}.jpg");
    }
}
