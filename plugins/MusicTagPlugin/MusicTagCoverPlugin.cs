using System.Net.Http.Json;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.MusicTagPlugin;

public class MusicTagCoverPlugin : ICoverProviderPlugin
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(12) };

    public string PluginId => "musictag.cover";
    public string Name => "MusicTag 封面搜索";
    public string Version => "1.0.0";
    public string Author => "CatClawMusic";
    public string Description => "多源封面搜索引擎，依次尝试 iTunes API、Deezer API 获取专辑封面图片。";

    public bool IsAvailable => true;

    public List<string> Capabilities => new()
    {
        "iTunes Search: 通过 Apple Music 数据库匹配专辑封面",
        "Deezer Search: 通过 Deezer 音乐数据库获取高清封面",
        "智能匹配: 优先按专辑名 + 艺术家匹配，回退到仅标题匹配",
        "高清封面: 优先获取 1000x1000 以上分辨率"
    };

    public Task InitializeAsync() => Task.CompletedTask;

    public Task ShutdownAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    public async Task<byte[]?> GetCoverAsync(Song song)
    {
        if (string.IsNullOrWhiteSpace(song.Title)) return null;

        var cover = await TryiTunesAsync(song);
        if (cover != null) return cover;

        cover = await TryDeezerAsync(song);
        if (cover != null) return cover;

        return null;
    }

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
