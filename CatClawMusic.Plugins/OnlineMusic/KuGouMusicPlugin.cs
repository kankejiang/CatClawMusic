using System.Text;
using System.Text.Json;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.OnlineMusic;

/// <summary>
/// 酷狗音乐音源插件：搜索 / 歌词。播放直链需 hash + 签名接口，一期暂不支持（返回 null，UI 提示不可播）。
/// </summary>
public class KuGouMusicPlugin : OnlineMusicPluginBase
{
    public override string PluginId => "kuGouMusic";
    public override string Name => "酷狗音乐";
    public override string Description => "酷狗音乐在线音源（搜索/歌词；播放直链待接入）";
    public override string PlatformName => "kugou";

    public override async Task<List<OnlineSong>?> SearchAsync(string keyword, int page = 1, int pageSize = 8)
    {
        try
        {
            var p = page < 1 ? 1 : page;
            var url = $"https://complexsearch.kugou.com/v2/search/song?keyword={Uri.EscapeDataString(keyword)}&page={p}&pagesize={pageSize}&clientver=20000&platform=WebFilter";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("x-router", "complexsearch.kugou.com");
            var resp = await Http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            if (!data.TryGetProperty("lists", out var lists)) return null;

            var results = new List<OnlineSong>();
            foreach (var s in lists.EnumerateArray())
            {
                var singers = new List<string>();
                if (s.TryGetProperty("Singers", out var singerArr))
                    foreach (var si in singerArr.EnumerateArray())
                    {
                        var sn = si.TryGetProperty("name", out var nn) ? nn.GetString() :
                                 si.TryGetProperty("Name", out var nm) ? nm.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(sn)) singers.Add(sn);
                    }

                var song = MakeSong(
                    id: s.TryGetProperty("ID", out var idEl) ? idEl.GetString() ?? "" : "",
                    title: s.TryGetProperty("SongName", out var snEl) ? snEl.GetString() ?? "" : "",
                    artist: string.Join(" / ", singers),
                    album: s.TryGetProperty("AlbumName", out var anEl) ? anEl.GetString() : null,
                    durationMs: s.TryGetProperty("Duration", out var dEl) ? dEl.GetInt64() * 1000 : 0,
                    coverUrl: s.TryGetProperty("Image", out var imgEl) ? imgEl.GetString() : null);
                song.Internal = new Dictionary<string, object>
                {
                    ["hash"] = s.TryGetProperty("FileHash", out var fh) ? fh.GetString() ?? "" : ""
                };
                results.Add(song);
            }
            return results;
        }
        catch { return null; }
    }

    public override Task<string?> GetPlayUrlAsync(OnlineSong song, int quality = 0)
        => Task.FromResult<string?>(null);

    public override async Task<(string? Lrc, string? TLrc)?> GetLyricsAsync(OnlineSong song)
    {
        try
        {
            var hash = song.Internal?.GetValueOrDefault("hash", "")?.ToString() ?? "";
            if (string.IsNullOrEmpty(hash)) return null;

            // Step 1: 按 hash 搜索歌词候选
            var searchUrl = $"https://lyrics.kugou.com/v1/search?" +
                $"keyword={Uri.EscapeDataString(song.Artist + " - " + song.Title)}" +
                $"&hash={hash}&duration={song.DurationMs}&lrctxt=1&man=no&clientver=20000&platform=WebFilter";
            var searchJson = await Http.GetStringAsync(searchUrl);
            using var searchDoc = JsonDocument.Parse(searchJson);
            if (!searchDoc.RootElement.TryGetProperty("candidates", out var cands) ||
                cands.GetArrayLength() == 0) return null;

            var candidate = cands[0];
            var accesskey = candidate.GetProperty("accesskey").GetString() ?? "";
            var lyricId = candidate.GetProperty("id").GetString() ?? "";

            // Step 2: 下载 LRC 歌词
            var dlUrl = $"https://lyrics.kugou.com/download?" +
                $"id={lyricId}&accesskey={accesskey}&charset=utf8&fmt=lrc&ver=1&clientver=20000&platform=WebFilter";
            var dlJson = await Http.GetStringAsync(dlUrl);
            using var dlDoc = JsonDocument.Parse(dlJson);

            string? lyricText = null;
            if (dlDoc.RootElement.TryGetProperty("content", out var content))
            {
                lyricText = content.GetString();
                if (!string.IsNullOrWhiteSpace(lyricText) &&
                    !lyricText.Contains('[') && lyricText.All(c => char.IsLetterOrDigit(c) || c == '/' || c == '+' || c == '=' || c == '\n'))
                {
                    try { lyricText = Encoding.UTF8.GetString(Convert.FromBase64String(lyricText)); } catch { }
                }
            }

            if (string.IsNullOrWhiteSpace(lyricText)) return null;
            return (lyricText, null);
        }
        catch { return null; }
    }
}
