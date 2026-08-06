using System.Text.Json;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.OnlineMusic;

/// <summary>
/// 网易云音乐音源插件：搜索 / 播放直链 / 歌词（含翻译）。
/// </summary>
public class NetEaseMusicPlugin : OnlineMusicPluginBase
{
    public override string PluginId => "netEaseMusic";
    public override string Name => "网易云音乐";
    public override string Description => "网易云音乐在线音源（搜索/播放/歌词）";
    public override string PlatformName => "netease";

    public override async Task<List<OnlineSong>?> SearchAsync(string keyword, int page = 1, int pageSize = 8)
    {
        try
        {
            var offset = (page - 1) * pageSize;
            var url = "https://music.163.com/api/cloudsearch/pc";
            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["s"] = keyword, ["type"] = "1", ["offset"] = offset.ToString(), ["limit"] = pageSize.ToString()
            });
            var resp = await Http.PostAsync(url, body);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var songs = doc.RootElement.TryGetProperty("result", out var result) &&
                        result.TryGetProperty("songs", out var sArr) ? sArr : default;
            if (songs.ValueKind != JsonValueKind.Array) return null;

            var results = new List<OnlineSong>();
            foreach (var s in songs.EnumerateArray())
            {
                var artists = new List<string>();
                if (s.TryGetProperty("ar", out var ar))
                    foreach (var a in ar.EnumerateArray())
                    {
                        var an = a.GetProperty("name").GetString();
                        if (!string.IsNullOrWhiteSpace(an)) artists.Add(an);
                    }

                var song = MakeSong(
                    id: s.GetProperty("id").GetInt64().ToString(),
                    title: s.GetProperty("name").GetString() ?? "",
                    artist: string.Join(" / ", artists),
                    album: s.TryGetProperty("al", out var al) ? al.GetProperty("name").GetString() : null,
                    durationMs: s.TryGetProperty("dt", out var dt) ? dt.GetInt64() : 0,
                    coverUrl: s.TryGetProperty("al", out var alc) && alc.TryGetProperty("picUrl", out var pic)
                        ? pic.GetString() : null);
                results.Add(song);
            }
            return results;
        }
        catch { return null; }
    }

    public override async Task<string?> GetPlayUrlAsync(OnlineSong song, int quality = 0)
    {
        if (string.IsNullOrWhiteSpace(song.Id)) return null;
        try
        {
            // br 档位：0=默认(320k) 1=128k 2=320k（quality 仅作占位，统一按 320k 取）
            var url = $"https://music.163.com/api/song/enhance/player/url?id={song.Id}&ids=[{song.Id}]&br=320000";
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("url", out var u))
                {
                    var playUrl = u.GetString();
                    if (!string.IsNullOrWhiteSpace(playUrl)) return playUrl;
                }
            }
            return null;
        }
        catch { return null; }
    }

    public override async Task<(string? Lrc, string? TLrc)?> GetLyricsAsync(OnlineSong song)
    {
        if (string.IsNullOrWhiteSpace(song.Id)) return null;
        try
        {
            var url = $"https://music.163.com/api/song/lyric?id={song.Id}&lv=1&kv=1&tv=-1";
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            string? lrc = null, tlyric = null;
            if (doc.RootElement.TryGetProperty("lrc", out var ln) && ln.TryGetProperty("lyric", out var lt))
                lrc = lt.GetString();
            if (doc.RootElement.TryGetProperty("tlyric", out var tn) && tn.TryGetProperty("lyric", out var tt))
                tlyric = tt.GetString();
            if (string.IsNullOrWhiteSpace(lrc)) return null;
            return (lrc, string.IsNullOrWhiteSpace(tlyric) ? null : tlyric);
        }
        catch { return null; }
    }
}
