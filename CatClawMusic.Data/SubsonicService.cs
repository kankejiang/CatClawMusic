using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

/// <summary>
/// Subsonic / Navidrome API 客户端
/// </summary>
public class SubsonicService : ISubsonicService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static string Md5(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string AuthParams(ConnectionProfile profile)
    {
        var salt = Guid.NewGuid().ToString("N")[..12];
        var token = Md5(profile.Password + salt);
        return $"u={HttpUtility.UrlEncode(profile.UserName)}" +
               $"&t={token}&s={salt}" +
               $"&v={profile.ApiVersion}&c={HttpUtility.UrlEncode(profile.ClientName)}&f=json";
    }

    private string ApiUrl(string endpoint, ConnectionProfile profile)
    {
        var baseUrl = profile.GetBaseUrl();
        var sep = endpoint.Contains('?') ? "&" : "?";
        return $"{baseUrl}/rest/{endpoint}{sep}{AuthParams(profile)}";
    }

    public async Task<(bool Success, string Message)> PingAsync(ConnectionProfile profile)
    {
        try
        {
            var url = ApiUrl("ping.view", profile);
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                return (false, $"HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}");
            var json = await resp.Content.ReadAsStringAsync();
            if (json.Contains("\"status\":\"ok\""))
                return (true, "连接成功");
            // 尝试解析错误信息
            if (json.Contains("error"))
                return (false, "认证失败，请检查用户名和密码");
            return (false, "服务器返回异常状态");
        }
        catch (TaskCanceledException)
        {
            return (false, "连接超时，请检查服务器地址和端口");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"无法连接服务器: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"连接失败: {ex.Message}");
        }
    }

    public async Task<List<Song>> SearchAsync(string query, ConnectionProfile profile)
    {
        try
        {
            var url = ApiUrl($"search3.view?query={HttpUtility.UrlEncode(query)}", profile);
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var songs = new List<Song>();

            var searchResult = doc.RootElement.GetProperty("subsonic-response").GetProperty("searchResult3");
            if (searchResult.TryGetProperty("song", out var songArray))
            {
                foreach (var item in songArray.EnumerateArray())
                {
                    var song = ParseSong(item, profile);
                    song.FilePath = GetStreamUrl(song.FilePath, profile);
                    songs.Add(song);
                }
            }
            return songs;
        }
        catch { return new List<Song>(); }
    }

    public async Task<List<Song>> GetSongsAsync(ConnectionProfile profile)
    {
        var songs = new List<Song>();
        var seenIds = new HashSet<string>(); // 重复检测：防止 offset 无效时死循环
        const int pageSize = 500;
        const int maxPages = 100; // 安全上限 50,000 首
        int offset = 0;
        int page = 0;

        try
        {
            while (page < maxPages)
            {
                page++;
                var url = ApiUrl($"search3.view?query=&songCount={pageSize}&albumCount=0&artistCount=0&offset={offset}", profile);
                System.Diagnostics.Debug.WriteLine($"[CatClaw] GetSongs Page {page} URL: {url}");
                var json = await _http.GetStringAsync(url);
                var pageCount = 0;
                var duplicateCount = 0;

                using (var doc = JsonDocument.Parse(json))
                {
                    var resp = doc.RootElement.GetProperty("subsonic-response");
                    if (resp.TryGetProperty("status", out var status) && status.GetString() == "failed")
                    {
                        var err = resp.TryGetProperty("error", out var e) ? e.TryGetProperty("message", out var m) ? m.GetString() : "" : "";
                        System.Diagnostics.Debug.WriteLine($"[CatClaw] GetSongs API错误: {err}");
                        break;
                    }
                    if (resp.TryGetProperty("searchResult3", out var searchResult))
                    {
                        if (searchResult.TryGetProperty("song", out var arr))
                        {
                            var songsInPage = EnumerateSongArray(arr);
                            foreach (var item in songsInPage)
                            {
                                var songId = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                                // 重复检测：如果 ID 已存在说明 offset 无效，退出
                                if (!seenIds.Add(songId))
                                {
                                    duplicateCount++;
                                    continue;
                                }
                                var song = ParseSong(item, profile);
                                song.FilePath = GetStreamUrl(song.FilePath, profile);
                                songs.Add(song);
                            }
                            pageCount = songsInPage.Count;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[CatClaw] GetSongs Page {page}: {pageCount} 首 (重复 {duplicateCount})");
                if (duplicateCount > pageCount / 2) break; // 超过一半重复 → offset 无效
                if (pageCount < pageSize) break;             // 最后一页
                offset += pageSize;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] GetSongsAsync 失败: {ex.GetType().Name}: {ex.Message}");
        }
        System.Diagnostics.Debug.WriteLine($"[CatClaw] GetSongsAsync 总计返回 {songs.Count} 首歌曲");
        return songs;
    }

    public async Task<List<Album>> GetAlbumsAsync(ConnectionProfile profile)
    {
        var albums = new List<Album>();
        try
        {
            var url = ApiUrl("getAlbumList2.view?type=newest&size=200", profile);
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var resp = doc.RootElement.GetProperty("subsonic-response").GetProperty("albumList2");
            if (resp.TryGetProperty("album", out var arr))
            {
                foreach (var item in arr.EnumerateArray())
                {
                    albums.Add(new Album
                    {
                        Name = item.GetProperty("name").GetString() ?? "",
                        Artist = item.GetProperty("artist").GetString() ?? "",
                        CoverArtPath = GetCoverArtUrl(GetString(item, "coverArt"), profile),
                        Year = GetInt(item, "year")
                    });
                }
            }
        }
        catch { }
        return albums;
    }

    public string GetStreamUrl(string songId, ConnectionProfile profile)
    {
        return ApiUrl($"stream.view?id={HttpUtility.UrlEncode(songId)}", profile);
    }

    public string GetCoverArtUrl(string coverArtId, ConnectionProfile profile)
    {
        var baseUrl = profile.GetBaseUrl();
        return $"{baseUrl}/rest/getCoverArt.view?{AuthParams(profile)}&id={HttpUtility.UrlEncode(coverArtId)}";
    }

    public async Task<byte[]?> GetCoverArtAsync(string coverArtId, ConnectionProfile profile)
    {
        try
        {
            var url = GetCoverArtUrl(coverArtId, profile);
            return await _http.GetByteArrayAsync(url);
        }
        catch { return null; }
    }

    public async Task<string?> GetLyricsAsync(string songId, ConnectionProfile profile)
    {
        try
        {
            // OpenSubsonic: getLyricsBySongId.view
            var url = ApiUrl($"getLyricsBySongId.view?id={HttpUtility.UrlEncode(songId)}", profile);
            System.Diagnostics.Debug.WriteLine($"[CatClaw] GetLyrics URL: {url}");
            var json = await _http.GetStringAsync(url);
            System.Diagnostics.Debug.WriteLine($"[CatClaw] GetLyrics 响应: {(json.Length > 300 ? json[..300] : json)}");
            using var doc = JsonDocument.Parse(json);
            var resp = doc.RootElement.GetProperty("subsonic-response");

            // 1. OpenSubsonic 结构化歌词: { "lyricsList": { "structuredLyrics": [{ "line": [...] }] } }
            if (resp.TryGetProperty("lyricsList", out var lyricsList) &&
                lyricsList.TryGetProperty("structuredLyrics", out var structured) &&
                structured.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in structured.EnumerateArray())
                {
                    if (entry.TryGetProperty("line", out var lines) && lines.ValueKind == JsonValueKind.Array)
                    {
                        var lrcBuilder = new System.Text.StringBuilder();
                        foreach (var line in lines.EnumerateArray())
                        {
                            var ms = line.TryGetProperty("start", out var s) && s.TryGetInt64(out var msVal) ? msVal : 0;
                            var text = line.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                            var ts = TimeSpan.FromMilliseconds(ms);
                            lrcBuilder.AppendLine($"[{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}]{text}");
                        }
                        var lrcText = lrcBuilder.ToString();
                        System.Diagnostics.Debug.WriteLine($"[CatClaw] GetLyrics 结构化歌词 {lrcText.Length} 字符");
                        return lrcText;
                    }
                }
            }

            // 2. OpenSubsonic 简单歌词: { "lyricsBySongId": { "value": "..." } }
            if (resp.TryGetProperty("lyricsBySongId", out var lrcById) &&
                lrcById.ValueKind != JsonValueKind.Null &&
                lrcById.TryGetProperty("value", out var val))
            {
                var text = val.GetString();
                System.Diagnostics.Debug.WriteLine($"[CatClaw] GetLyrics lyricsBySongId {text?.Length ?? 0} 字符");
                return text;
            }

            // 3. 旧版 Subsonic: { "lyrics": { "value": "..." } }
            if (resp.TryGetProperty("lyrics", out var lrc) &&
                lrc.ValueKind != JsonValueKind.Null &&
                lrc.TryGetProperty("value", out var val2))
            {
                return val2.GetString();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] GetLyrics 失败: {ex.Message}");
        }
        return null;
    }

    private static Song ParseSong(JsonElement item, ConnectionProfile profile)
    {
        var songId = GetString(item, "id");
        return new Song
        {
            Title = GetString(item, "title"),
            Artist = GetString(item, "artist"),
            Album = GetString(item, "album"),
            Duration = GetInt(item, "duration"),
            Bitrate = GetInt(item, "bitRate"),
            FileSize = GetLong(item, "size"),
            FilePath = songId,
            CoverArtPath = GetString(item, "coverArt"),
            Source = SongSource.WebDAV,
            RemoteId = songId
        };
    }

    private static string GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

    private static int GetInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;

    private static long GetLong(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.TryGetInt64(out var l) ? l : 0;

    /// <summary>安全枚举 song 数组（兼容 JSON 对象/数组两种格式）</summary>
    private static List<JsonElement> EnumerateSongArray(JsonElement element)
    {
        var result = new List<JsonElement>();
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                result.Add(item);
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            result.Add(element); // 单首歌：JSON 对象而非数组
        }
        return result;
    }
}
