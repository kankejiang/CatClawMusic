using System.Net.Http.Json;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.MusicTagPlugin;

/// <summary>
/// 封面搜索结果数据模型，包含封面图片的来源、元数据和图片字节数据。
/// </summary>
public class CoverSearchResult
{
    /// <summary>
    /// 图片来源标识，如 "iTunes"、"Deezer" 等。
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// 专辑名称。
    /// </summary>
    public string AlbumName { get; set; } = "";

    /// <summary>
    /// 艺术家名称。
    /// </summary>
    public string ArtistName { get; set; } = "";

    /// <summary>
    /// 封面图片的 URL 地址。
    /// </summary>
    public string ImageUrl { get; set; } = "";

    /// <summary>
    /// 封面图片宽度（像素）。
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 封面图片高度（像素）。
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 封面图片的字节数据，如果尚未下载则为 null。
    /// </summary>
    public byte[]? ImageBytes { get; set; }
}

/// <summary>
/// MusicTag 封面插件，实现 <see cref="ICoverProviderPlugin"/> 接口，
/// 通过 iTunes、Deezer、QQ音乐、酷狗、酷我、网易云音乐等多源搜索并获取专辑封面图片。
/// </summary>
public class MusicTagCoverPlugin : ICoverProviderPlugin
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(12) };

    /// <summary>
    /// 插件唯一标识符。
    /// </summary>
    public string PluginId => "musictag.cover";

    /// <summary>
    /// 插件显示名称。
    /// </summary>
    public string Name => "MusicTag 封面搜索";

    /// <summary>
    /// 插件版本号。
    /// </summary>
    public string Version => "1.0.0";

    /// <summary>
    /// 插件作者。
    /// </summary>
    public string Author => "CatClawMusic";

    /// <summary>
    /// 插件功能描述。
    /// </summary>
    public string Description => "多源封面搜索引擎，依次尝试 iTunes API、Deezer API 获取专辑封面图片。";

    /// <summary>
    /// 指示插件当前是否可用。
    /// </summary>
    public bool IsAvailable => true;

    /// <summary>
    /// 插件支持的能力列表。
    /// </summary>
    public List<string> Capabilities => new()
    {
        "iTunes Search: 通过 Apple Music 数据库匹配专辑封面",
        "Deezer Search: 通过 Deezer 音乐数据库获取高清封面",
        "QQ音乐: 通过QQ音乐API获取专辑封面",
        "酷狗: 通过酷狗API获取专辑封面",
        "酷我: 通过酷我API获取专辑封面",
        "网易云音乐: 通过网易云API获取专辑封面",
        "智能匹配: 优先按专辑名 + 艺术家匹配，回退到仅标题匹配",
        "高清封面: 优先获取 1000x1000 以上分辨率"
    };

    /// <summary>
    /// 异步初始化插件。
    /// </summary>
    /// <returns>表示初始化操作的任务。</returns>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// 异步关闭并释放插件资源，包括释放内部 <see cref="HttpClient"/>。
    /// </summary>
    /// <returns>表示关闭操作的任务。</returns>
    public Task ShutdownAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 根据给定的歌曲信息搜索并返回最匹配的封面图片字节数据。
    /// 依次尝试多个数据源，返回首个有效的结果。
    /// </summary>
    /// <param name="song">包含标题、艺术家、专辑等信息的歌曲对象。</param>
    /// <returns>封面图片的字节数组，若未找到有效封面则返回 null。</returns>
    public async Task<byte[]?> GetCoverAsync(Song song)
    {
        var results = await SearchCoversAsync(song);
        foreach (var r in results)
        {
            if (r.ImageBytes != null && r.ImageBytes.Length > 500) return r.ImageBytes;
        }
        return null;
    }

    /// <summary>
    /// 根据给定的歌曲信息从多个数据源搜索封面图片，返回所有匹配的结果列表。
    /// 依次从 iTunes 和 Deezer 两个源收集结果。
    /// </summary>
    /// <param name="song">包含标题、艺术家、专辑等信息的歌曲对象。</param>
    /// <returns>所有匹配的封面搜索结果列表。</returns>
    public async Task<List<CoverSearchResult>> SearchCoversAsync(Song song)
    {
        var allResults = new List<CoverSearchResult>();

        if (string.IsNullOrWhiteSpace(song.Title)) return allResults;

        var searchMethods = new Func<Task<List<CoverSearchResult>>>[]
        {
            () => SearchiTunesAsync(song),
            () => SearchDeezerAsync(song),
            () => SearchQQMusicCoverAsync(song),
            () => SearchKugouCoverAsync(song),
            () => SearchKuwoCoverAsync(song),
            () => SearchNeteaseCoverAsync(song)
        };

        var tasks = searchMethods.Select(async method =>
        {
            try { return await method(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MusicTagCover] Search failed: {ex.Message}"); return new List<CoverSearchResult>(); }
        });

        var results = await Task.WhenAll(tasks);
        foreach (var r in results) allResults.AddRange(r);

        return allResults;
    }

    /// <summary>
    /// 通过 Apple iTunes API 搜索歌曲封面信息。
    /// 优先使用专辑名 + 艺术家进行匹配，回退到歌曲标题 + 艺术家。
    /// 将 100x100 的缩略图 URL 替换为 600x600 的高分辨率版本。
    /// </summary>
    /// <param name="song">包含标题、艺术家、专辑等信息的歌曲对象。</param>
    /// <returns>iTunes 搜索返回的封面结果列表，最多 5 条。</returns>
    public async Task<List<CoverSearchResult>> SearchiTunesAsync(Song song)
    {
        var results = new List<CoverSearchResult>();
        try
        {
            var query = !string.IsNullOrWhiteSpace(song.Album)
                ? $"{song.Album} {song.Artist}"
                : $"{song.Title} {song.Artist}";
            var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query ?? "")}&media=music&entity=song&limit=5";

            var response = await _client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return results;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("results", out var searchResults)) return results;
            if (searchResults.ValueKind != JsonValueKind.Array || searchResults.GetArrayLength() == 0) return results;

            foreach (var item in searchResults.EnumerateArray())
            {
                if (!item.TryGetProperty("artworkUrl100", out var artUrl)) continue;
                var artStr = artUrl.GetString();
                if (string.IsNullOrWhiteSpace(artStr)) continue;

                var highResUrl = artStr.Replace("100x100bb.jpg", "600x600bb.jpg");

                var result = new CoverSearchResult { Source = "iTunes" };
                result.ImageUrl = highResUrl;
                result.Width = 600;
                result.Height = 600;

                if (item.TryGetProperty("collectionName", out var cn)) result.AlbumName = cn.GetString() ?? "";
                if (item.TryGetProperty("artistName", out var an)) result.ArtistName = an.GetString() ?? "";

                results.Add(result);

                if (results.Count >= 5) break;
            }
        }
        catch
        {
        }

        return results;
    }

    /// <summary>
    /// 通过 QQ 音乐 API 搜索专辑封面
    /// </summary>
    public async Task<List<CoverSearchResult>> SearchQQMusicCoverAsync(Song song)
    {
        var results = new List<CoverSearchResult>();
        try
        {
            var query = !string.IsNullOrWhiteSpace(song.Album) ? $"{song.Album} {song.Artist}" : $"{song.Title} {song.Artist}";
            var url = $"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?w={Uri.EscapeDataString(query ?? "")}&format=json&n=5&t=0";

            _client.DefaultRequestHeaders.Referrer = new Uri("https://y.qq.com/");
            _client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
            var response = await _client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("data", out var data)) return results;
            if (!data.TryGetProperty("song", out var songList)) return results;
            if (!songList.TryGetProperty("list", out var list)) return results;

            foreach (var item in list.EnumerateArray())
            {
                if (results.Count >= 3) break;
                var mid = item.TryGetProperty("albummid", out var am) ? am.GetString() : "";
                if (string.IsNullOrEmpty(mid)) continue;

                var coverUrl = $"https://y.gtimg.cn/music/photo_new/T002R500x500M000{mid}.jpg";
                var result = new CoverSearchResult { Source = "QQ音乐" };
                result.ImageUrl = coverUrl;
                result.Width = 500; result.Height = 500;
                if (item.TryGetProperty("name", out var sn)) result.AlbumName = sn.GetString() ?? "";
                if (item.TryGetProperty("singer", out var singers))
                    result.ArtistName = string.Join(", ", singers.EnumerateArray().Select(s => s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "").Where(n => !string.IsNullOrEmpty(n)));
                results.Add(result);
            }
        }
        catch { }

        return results;
    }

    /// <summary>
    /// 通过酷狗 API 搜索专辑封面
    /// </summary>
    public async Task<List<CoverSearchResult>> SearchKugouCoverAsync(Song song)
    {
        var results = new List<CoverSearchResult>();
        try
        {
            var query = !string.IsNullOrWhiteSpace(song.Album) ? $"{song.Album} {song.Artist}" : $"{song.Title} {song.Artist}";
            var url = $"https://complexsearch.kugou.com/v2/search/song?keyword={Uri.EscapeDataString(query ?? "")}&page=1&pagesize=5";

            var response = await _client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return results;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data)) return results;
            if (!data.TryGetProperty("lists", out var lists)) return results;

            foreach (var item in lists.EnumerateArray())
            {
                if (results.Count >= 3) break;
                var albumId = item.TryGetProperty("AlbumID", out var aid) ? aid.GetInt32() : 0;
                if (albumId <= 0) continue;

                var hash = item.TryGetProperty("FileHash", out var fh) ? fh.GetString() : "";
                var coverUrl = $"https://img1.krcdn.com/kugou_mobile/mssrc/{hash}";
                var result = new CoverSearchResult { Source = "酷狗" };
                result.ImageUrl = coverUrl;
                result.Width = 500; result.Height = 500;
                if (item.TryGetProperty("AlbumName", out var an)) result.AlbumName = an.GetString() ?? "";
                if (item.TryGetProperty("SingerName", out var sn)) result.ArtistName = sn.GetString() ?? "";
                results.Add(result);
            }
        }
        catch { }

        return results;
    }

    /// <summary>
    /// 通过酷我音乐 API 搜索专辑封面
    /// </summary>
    public async Task<List<CoverSearchResult>> SearchKuwoCoverAsync(Song song)
    {
        var results = new List<CoverSearchResult>();
        try
        {
            var query = !string.IsNullOrWhiteSpace(song.Album) ? $"{song.Album} {song.Artist}" : $"{song.Title} {song.Artist}";
            var url = $"https://search.kuwo.cn/r.s?all={Uri.EscapeDataString(query)}&ft=music&rn=5&encoding=utf8";

            var response = await _client.GetStringAsync(url);
            if (string.IsNullOrWhiteSpace(response) || response.Contains("NO_RESULT")) return results;

            var match = System.Text.RegularExpressions.Regex.Match(response, @"pic=""([^""]+)""");
            if (!match.Success) return results;

            var picPath = match.Groups[1].Value;
            if (string.IsNullOrEmpty(picPath)) return results;

            var coverUrl = picPath.StartsWith("http") ? picPath : $"https://img2.kuwo.cn/star/albumcover/500{picPath}";
            var result = new CoverSearchResult { Source = "酷我" };
            result.ImageUrl = coverUrl;
            result.Width = 500; result.Height = 500;
            result.AlbumName = song.Album ?? song.Title ?? "";
            result.ArtistName = song.Artist ?? "";
            results.Add(result);
        }
        catch { }

        return results;
    }

    /// <summary>
    /// 通过网易云音乐 API 搜索专辑封面
    /// </summary>
    public async Task<List<CoverSearchResult>> SearchNeteaseCoverAsync(Song song)
    {
        var results = new List<CoverSearchResult>();
        try
        {
            var query = $"{song.Title} {song.Artist}";
            var searchUrl = $"https://music.163.com/api/search/get?s={Uri.EscapeDataString(query)}&type=1&limit=5";

            _client.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
            var response = await _client.GetStringAsync(searchUrl);
            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("result", out var searchResult)) return results;
            if (!searchResult.TryGetProperty("songs", out var songs)) return results;

            int count = 0;
            foreach (var songItem in songs.EnumerateArray())
            {
                if (count >= 3) break;
                if (!songItem.TryGetProperty("album", out var albumObj) || albumObj.ValueKind != JsonValueKind.Object) continue;

                var picUrl = albumObj.TryGetProperty("picUrl", out var pic) ? pic.GetString() : null;
                if (string.IsNullOrEmpty(picUrl)) continue;

                var result = new CoverSearchResult { Source = "网易云音乐" };
                result.ImageUrl = picUrl;
                result.Width = 500; result.Height = 500;
                if (albumObj.TryGetProperty("name", out var an)) result.AlbumName = an.GetString() ?? "";
                if (songItem.TryGetProperty("artists", out var artists))
                    result.ArtistName = string.Join(", ", artists.EnumerateArray().Select(a => a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "").Where(n => !string.IsNullOrEmpty(n)));

                results.Add(result);
                count++;
            }
        }
        catch { }

        return results;
    }

    /// <summary>
    /// 通过 Deezer API 搜索歌曲封面信息。
    /// 优先使用专辑名 + 艺术家进行匹配，回退到歌曲标题 + 艺术家。
    /// 优先获取 1000x1000 的封面（cover_xl），若无则降级为 500x500（cover_big）。
    /// </summary>
    /// <param name="song">包含标题、艺术家、专辑等信息的歌曲对象。</param>
    /// <returns>Deezer 搜索返回的封面结果列表，最多 5 条。</returns>
    public async Task<List<CoverSearchResult>> SearchDeezerAsync(Song song)
    {
        var results = new List<CoverSearchResult>();
        try
        {
            var query = !string.IsNullOrWhiteSpace(song.Album)
                ? $"album:\"{song.Album}\" artist:\"{song.Artist}\""
                : $"track:\"{song.Title}\" artist:\"{song.Artist}\"";
            var url = $"https://api.deezer.com/search?q={Uri.EscapeDataString(query ?? "")}&limit=5";

            var response = await _client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return results;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data)) return results;
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) return results;

            foreach (var item in data.EnumerateArray())
            {
                var album = item.TryGetProperty("album", out var alb) ? alb : default;
                if (album.ValueKind != JsonValueKind.Object) continue;

                string coverUrl = "";
                int width = 0, height = 0;

                if (album.TryGetProperty("cover_xl", out var coverXl))
                {
                    coverUrl = coverXl.GetString() ?? "";
                    width = 1000;
                    height = 1000;
                }
                else if (album.TryGetProperty("cover_big", out var coverBig))
                {
                    coverUrl = coverBig.GetString() ?? "";
                    width = 500;
                    height = 500;
                }

                if (string.IsNullOrWhiteSpace(coverUrl)) continue;

                var result = new CoverSearchResult { Source = "Deezer" };
                result.ImageUrl = coverUrl;
                result.Width = width;
                result.Height = height;

                if (album.TryGetProperty("title", out var at)) result.AlbumName = at.GetString() ?? "";
                if (item.TryGetProperty("artist", out var artist) && artist.ValueKind == JsonValueKind.Object)
                {
                    if (artist.TryGetProperty("name", out var nameProp)) result.ArtistName = nameProp.GetString() ?? "";
                }

                results.Add(result);

                if (results.Count >= 5) break;
            }
        }
        catch
        {
        }

        return results;
    }

    /// <summary>
    /// 通过 Apple iTunes API 搜索并直接下载第一张匹配的封面图片。
    /// 与 <see cref="SearchiTunesAsync"/> 不同，此方法会立即下载图片数据并返回。
    /// </summary>
    /// <param name="song">包含标题、艺术家、专辑等信息的歌曲对象。</param>
    /// <returns>封面图片的字节数组，若未找到则返回 null。</returns>
    private async Task<byte[]?> TryiTunesAsync(Song song)
    {
        try
        {
            var query = !string.IsNullOrWhiteSpace(song.Album)
                ? $"{song.Album} {song.Artist}"
                : $"{song.Title} {song.Artist}";
            var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query ?? "")}&media=music&entity=song&limit=3";

            var response = await _client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("results", out var results)) return null;
            if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0) return null;

            foreach (var item in results.EnumerateArray())
            {
                if (!item.TryGetProperty("artworkUrl100", out var artUrl)) continue;
                var artStr = artUrl.GetString();
                if (string.IsNullOrWhiteSpace(artStr)) continue;

                var highResUrl = artStr.Replace("100x100bb.jpg", "600x600bb.jpg");
                var imgBytes = await DownloadCoverAsync(highResUrl);

                if (imgBytes == null && artStr.Contains("100x100"))
                {
                    imgBytes = await DownloadCoverAsync(artStr);
                }

                if (imgBytes != null) return imgBytes;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 通过 Deezer API 搜索并直接下载第一张匹配的封面图片。
    /// 优先尝试获取 cover_xl（1000x1000）的高清封面。
    /// </summary>
    /// <param name="song">包含标题、艺术家、专辑等信息的歌曲对象。</param>
    /// <returns>封面图片的字节数组，若未找到则返回 null。</returns>
    private async Task<byte[]?> TryDeezerAsync(Song song)
    {
        try
        {
            var query = !string.IsNullOrWhiteSpace(song.Album)
                ? $"album:\"{song.Album}\" artist:\"{song.Artist}\""
                : $"track:\"{song.Title}\" artist:\"{song.Artist}\"";
            var url = $"https://api.deezer.com/search?q={Uri.EscapeDataString(query ?? "")}&limit=3";

            var response = await _client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) return null;

            foreach (var item in data.EnumerateArray())
            {
                var album = item.TryGetProperty("album", out var alb) ? alb : default;
                if (album.ValueKind != JsonValueKind.Object) continue;

                if (!album.TryGetProperty("cover_xl", out var coverXl)) continue;

                var coverUrl = coverXl.GetString();
                if (string.IsNullOrWhiteSpace(coverUrl)) continue;

                var imgBytes = await DownloadCoverAsync(coverUrl);
                if (imgBytes != null) return imgBytes;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 根据给定的图片 URL 异步下载封面图片数据。
    /// 仅当下载的图片数据大小超过 500 字节时才视为有效结果。
    /// </summary>
    /// <param name="url">封面图片的 URL 地址。</param>
    /// <returns>下载的图片字节数组，若下载失败或数据过小则返回 null。</returns>
    private async Task<byte[]?> DownloadCoverAsync(string url)
    {
        try
        {
            var response = await _client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            return bytes.Length > 500 ? bytes : null;
        }
        catch
        {
            return null;
        }
    }
}
