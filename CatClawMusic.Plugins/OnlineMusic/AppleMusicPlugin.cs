using System.Text.Json;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.OnlineMusic;

/// <summary>
/// Apple Music 音源插件：搜索（iTunes Search API）+ 预览直链（30 秒 previewUrl）+ 歌词（paxsenix 第三方 API）。
/// </summary>
public class AppleMusicPlugin : OnlineMusicPluginBase
{
    public override string PluginId => "appleMusic";
    public override string Name => "Apple Music";
    public override string Description => "Apple Music 在线音源（搜索/30秒预览播放/歌词）";
    public override string PlatformName => "apple";

    public override async Task<List<OnlineSong>?> SearchAsync(string keyword, int page = 1, int pageSize = 8)
    {
        try
        {
            var limit = pageSize < 1 ? 8 : pageSize;
            var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(keyword)}&media=music&entity=song&limit={limit}&country=CN&lang=zh_cn";
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var resultsArr)) return null;

            var results = new List<OnlineSong>();
            foreach (var r in resultsArr.EnumerateArray())
            {
                var song = MakeSong(
                    id: r.GetProperty("trackId").GetInt64().ToString(),
                    title: r.TryGetProperty("trackName", out var tn) ? tn.GetString() ?? "" : "",
                    artist: r.TryGetProperty("artistName", out var an) ? an.GetString() ?? "" : "",
                    album: r.TryGetProperty("collectionName", out var cn) ? cn.GetString() : null,
                    durationMs: r.TryGetProperty("trackTimeMillis", out var dur) ? dur.GetInt64() : 0,
                    coverUrl: r.TryGetProperty("artworkUrl100", out var art)
                        ? art.GetString()?.Replace("100x100", "600x600") : null);
                // iTunes 搜索结果自带 30 秒预览直链，聚合器直接复用
                song.DirectPlayUrl = r.TryGetProperty("previewUrl", out var pu) ? pu.GetString() : null;
                results.Add(song);
            }
            return results;
        }
        catch { return null; }
    }

    public override Task<string?> GetPlayUrlAsync(OnlineSong song, int quality = 0)
        => Task.FromResult(song.DirectPlayUrl);

    public override async Task<(string? Lrc, string? TLrc)?> GetLyricsAsync(OnlineSong song)
    {
        if (string.IsNullOrWhiteSpace(song.Id)) return null;
        try
        {
            // 使用 paxsenix 第三方 Apple Music 歌词 API（无需 Developer Token）
            var url = $"https://lyrics.paxsenix.org/apple-music/lyrics?id={Uri.EscapeDataString(song.Id)}&ttml=false";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("accept", "application/json");
            var resp = await Http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            string? rawLrc = null;
            if (doc.RootElement.TryGetProperty("lrc", out var lrcEl) && lrcEl.ValueKind == JsonValueKind.String)
                rawLrc = lrcEl.GetString();
            if (string.IsNullOrWhiteSpace(rawLrc) &&
                doc.RootElement.TryGetProperty("elrc", out var elrcEl) && elrcEl.ValueKind == JsonValueKind.String)
                rawLrc = elrcEl.GetString();
            if (string.IsNullOrWhiteSpace(rawLrc) &&
                doc.RootElement.TryGetProperty("ttmlContent", out var ttmlEl) && ttmlEl.ValueKind == JsonValueKind.String)
                rawLrc = ttmlEl.GetString();

            if (string.IsNullOrWhiteSpace(rawLrc)) return null;
            return (rawLrc, null);
        }
        catch { return null; }
    }
}
