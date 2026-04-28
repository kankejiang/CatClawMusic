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
    private readonly HttpClient _http = new();

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
        return $"{baseUrl}/rest/{endpoint}?{AuthParams(profile)}";
    }

    public async Task<(bool Success, string Message)> PingAsync(ConnectionProfile profile)
    {
        try
        {
            var url = ApiUrl("ping", profile);
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                return (false, $"HTTP {(int)resp.StatusCode}");
            var json = await resp.Content.ReadAsStringAsync();
            return json.Contains("\"status\":\"ok\"")
                ? (true, "连接成功")
                : (false, "服务器返回异常状态");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<List<Song>> SearchAsync(string query, ConnectionProfile profile)
    {
        try
        {
            var url = ApiUrl($"search3?query={HttpUtility.UrlEncode(query)}", profile);
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var songs = new List<Song>();

            var searchResult = doc.RootElement.GetProperty("subsonic-response").GetProperty("searchResult3");
            if (searchResult.TryGetProperty("song", out var songArray))
            {
                foreach (var item in songArray.EnumerateArray())
                {
                    songs.Add(ParseSong(item, profile));
                }
            }
            return songs;
        }
        catch { return new List<Song>(); }
    }

    public async Task<List<Song>> GetSongsAsync(ConnectionProfile profile)
    {
        var songs = new List<Song>();
        try
        {
            var url = ApiUrl("getRandomSongs?size=500", profile);
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var resp = doc.RootElement.GetProperty("subsonic-response").GetProperty("randomSongs");
            if (resp.TryGetProperty("song", out var arr))
            {
                foreach (var item in arr.EnumerateArray())
                    songs.Add(ParseSong(item, profile));
            }
        }
        catch { }
        return songs;
    }

    public async Task<List<Album>> GetAlbumsAsync(ConnectionProfile profile)
    {
        var albums = new List<Album>();
        try
        {
            var url = ApiUrl("getAlbumList2?type=newest&size=200", profile);
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
        return ApiUrl($"stream?id={HttpUtility.UrlEncode(songId)}", profile);
    }

    public string GetCoverArtUrl(string coverArtId, ConnectionProfile profile)
    {
        var baseUrl = profile.GetBaseUrl();
        return $"{baseUrl}/rest/getCoverArt?{AuthParams(profile)}&id={HttpUtility.UrlEncode(coverArtId)}";
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
            var url = ApiUrl($"getLyrics?id={HttpUtility.UrlEncode(songId)}", profile);
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var resp = doc.RootElement.GetProperty("subsonic-response");
            if (resp.TryGetProperty("lyrics", out var lrc))
            {
                if (!lrc.ValueKind.ToString().Contains("Null"))
                    return lrc.GetProperty("value").GetString();
            }
        }
        catch { }
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
            Source = SongSource.WebDAV
        };
    }

    private static string GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

    private static int GetInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;

    private static long GetLong(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.TryGetInt64(out var l) ? l : 0;
}
