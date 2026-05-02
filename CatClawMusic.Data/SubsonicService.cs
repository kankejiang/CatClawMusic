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

    public async Task<List<Song>> GetSongsAsync(ConnectionProfile profile,
        IProgress<(int done, int total, string status)>? progress = null,
        Func<List<Song>, Task>? songCallback = null)
    {
        var songs = new List<Song>();
        var seenIds = new HashSet<string>();

        try
        {
            // 策略：先拉专辑列表，再逐个专辑获取完整歌曲信息
            // search3 空查询可能不返回 artist/album 字段，改用 getAlbum + getAlbumList2
            var albums = new List<(string Id, string Name, string Artist, string CoverArt)>();
            const int pageSize = 200;
            int offset = 0;
            int maxPages = 50;

            // 1. 获取所有专辑
            progress?.Report((0, 0, "获取专辑列表..."));
            for (int page = 0; page < maxPages; page++)
            {
                var albumUrl = ApiUrl($"getAlbumList2.view?type=alphabeticalByArtist&size={pageSize}&offset={offset}", profile);
                try
                {
                    var json = await _http.GetStringAsync(albumUrl);
                    using var doc = JsonDocument.Parse(json);
                    var resp = doc.RootElement.GetProperty("subsonic-response");
                    if (resp.TryGetProperty("albumList2", out var list) &&
                        list.TryGetProperty("album", out var arr))
                    {
                        int count = 0;
                        foreach (var item in EnumerateSongArray(arr))
                        {
                            var id = GetString(item, "id");
                            var name = GetString(item, "name");
                            var artist = GetString(item, "artist");
                            var coverId = GetString(item, "coverArt");
                            albums.Add((id, name, artist, coverId));
                            count++;
                        }
                        if (count < pageSize) break;
                    }
                    else break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CatClaw] getAlbumList2 第{page}页失败: {ex.Message}");
                    break;
                }
                offset += pageSize;
            }

            System.Diagnostics.Debug.WriteLine($"[CatClaw] GetSongs 共获取 {albums.Count} 张专辑");
            progress?.Report((0, albums.Count, $"共 {albums.Count} 张专辑，开始拉取歌曲..."));

            // 2. 逐个专辑获取歌曲（完整元数据）——每处理完一个专辑即回调
            int albumIdx = 0;
            foreach (var (albumId, albumName, albumArtist, coverArt) in albums)
            {
                albumIdx++;
                try
                {
                    var songsUrl = ApiUrl($"getAlbum.view?id={HttpUtility.UrlEncode(albumId)}", profile);
                    var json = await _http.GetStringAsync(songsUrl);
                    using var doc = JsonDocument.Parse(json);
                    var resp = doc.RootElement.GetProperty("subsonic-response");
                    if (resp.TryGetProperty("album", out var album) &&
                        album.TryGetProperty("song", out var songArr))
                    {
                        var albumSongs = new List<Song>();
                        foreach (var item in EnumerateSongArray(songArr))
                        {
                            var songId = GetString(item, "id");
                            if (!seenIds.Add(songId)) continue;

                            // Navidrome: getAlbum 返回的歌曲元素不保证有 artist/album 字段
                            // 直接使用专辑级别的艺术家和专辑名
                            var songArtist = GetString(item, "artist").Trim();
                            var songAlbum = GetString(item, "album").Trim();
                            var songCoverId = GetString(item, "coverArt").Trim();

                            var song = new Song
                            {
                                Title = GetString(item, "title"),
                                Artist = songArtist.Length > 0 ? songArtist : albumArtist,
                                Album = songAlbum.Length > 0 ? songAlbum : albumName,
                                Duration = GetInt(item, "duration"),
                                Bitrate = GetInt(item, "bitRate"),
                                FileSize = GetLong(item, "size"),
                                FilePath = songId,
                                CoverArtPath = songCoverId.Length > 0 ? songCoverId : coverArt,
                                Source = SongSource.WebDAV,
                                RemoteId = songId,
                                Year = GetInt(item, "year"),
                                TrackNumber = GetInt(item, "track")
                            };
                            song.FilePath = GetStreamUrl(song.FilePath, profile);
                            songs.Add(song);
                            albumSongs.Add(song);
                        }
                        // 增量回调：每获取完一个专辑就通知调用方
                        if (albumSongs.Count > 0 && songCallback != null)
                            await songCallback(albumSongs);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CatClaw] GetSongs 专辑 {albumName} 失败: {ex.Message}");
                }

                // 每处理完一个专辑报告进度
                if (albumIdx % 3 == 0 || albumIdx == albums.Count)
                {
                    progress?.Report((albumIdx, albums.Count,
                        $"拉取歌曲中 ({albumIdx}/{albums.Count}) · {songs.Count} 首"));
                }
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
