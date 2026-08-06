using System.Text;
using System.Text.Json;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.OnlineMusic;

/// <summary>
/// QQ音乐音源插件：搜索 / 播放直链（musicu vkey 接口）/ 歌词（含翻译，自动解 base64）。
/// </summary>
public class QQMusicPlugin : OnlineMusicPluginBase
{
    public override string PluginId => "qqMusic";
    public override string Name => "QQ音乐";
    public override string Description => "QQ音乐在线音源（搜索/播放/歌词）";
    public override string PlatformName => "qq";

    public override async Task<List<OnlineSong>?> SearchAsync(string keyword, int page = 1, int pageSize = 8)
    {
        try
        {
            var p = page < 1 ? 1 : page;
            var url = $"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?p={p}&n={pageSize}&w={Uri.EscapeDataString(keyword)}&format=json&platform=yqq";
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            if (!data.TryGetProperty("song", out var songData)) return null;
            if (!songData.TryGetProperty("list", out var list)) return null;

            var results = new List<OnlineSong>();
            foreach (var s in list.EnumerateArray())
            {
                var singers = new List<string>();
                if (s.TryGetProperty("singer", out var singerArr))
                    foreach (var si in singerArr.EnumerateArray())
                    {
                        var sn = si.GetProperty("name").GetString();
                        if (!string.IsNullOrWhiteSpace(sn)) singers.Add(sn);
                    }

                var songmid = s.GetProperty("songmid").GetString() ?? "";
                var albummid = s.TryGetProperty("albummid", out var am) ? am.GetString() : "";

                var song = MakeSong(
                    id: songmid,
                    title: s.GetProperty("songname").GetString() ?? "",
                    artist: string.Join(" / ", singers),
                    album: s.TryGetProperty("albumname", out var an) ? an.GetString() : null,
                    durationMs: s.TryGetProperty("interval", out var dur) ? dur.GetInt64() * 1000 : 0,
                    coverUrl: !string.IsNullOrEmpty(albummid)
                        ? $"https://y.gtimg.cn/music/photo_new/T002R1200x1200M000{albummid}.jpg"
                        : null);
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
            // musicu.fcg 批量取 vkey：返回 data.req_0.data.sip[0] + midurlinfo[].purl
            var payload = new Dictionary<string, object>
            {
                ["req_0"] = new Dictionary<string, object>
                {
                    ["module"] = "vkey.GetVkeyServer",
                    ["method"] = "CgiGetVkey",
                    ["param"] = new Dictionary<string, object>
                    {
                        ["guid"] = "10000",
                        ["songmid"] = new[] { song.Id },
                        ["songtype"] = new[] { 0 },
                        ["uin"] = "0",
                        ["loginflag"] = 1,
                        ["platform"] = "20"
                    }
                },
                ["comm"] = new Dictionary<string, object> { ["uin"] = 0, ["format"] = "json", ["ct"] = 24, ["cv"] = 0 }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "https://u.y.qq.com/cgi-bin/musicu.fcg")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Referrer = new Uri("https://y.qq.com/");
            var resp = await Http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("req_0", out var r0)) return null;
            if (!r0.TryGetProperty("data", out var data)) return null;
            var sip = data.TryGetProperty("sip", out var sipArr) && sipArr.ValueKind == JsonValueKind.Array && sipArr.GetArrayLength() > 0
                ? sipArr[0].GetString() : null;
            if (string.IsNullOrEmpty(sip)) return null;

            if (data.TryGetProperty("midurlinfo", out var infoArr) && infoArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var info in infoArr.EnumerateArray())
                {
                    if (info.TryGetProperty("purl", out var p) && !string.IsNullOrWhiteSpace(p.GetString()))
                        return sip + p.GetString();
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
            var url = $"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={song.Id}&format=json&nobase64=1";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Referrer = new Uri("https://y.qq.com/");
            var resp = await Http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            string? lrc = null, tlyric = null;
            if (doc.RootElement.TryGetProperty("lyric", out var ln))
            {
                var v = ln.ValueKind == JsonValueKind.String ? ln.GetString() : "";
                if (!string.IsNullOrWhiteSpace(v))
                {
                    // QQ 即使 nobase64=1 偶尔仍返回 base64
                    if (!v.Contains('[') && v.All(c => char.IsLetterOrDigit(c) || c == '/' || c == '+' || c == '=' || c == '\n'))
                    {
                        try { v = Encoding.UTF8.GetString(Convert.FromBase64String(v)); } catch { }
                    }
                    lrc = v;
                }
            }
            if (doc.RootElement.TryGetProperty("trans", out var tn))
            {
                var v = tn.ValueKind == JsonValueKind.String ? tn.GetString() : "";
                if (!string.IsNullOrWhiteSpace(v) && lrc != null)
                {
                    if (!v.Contains('[') && v.All(c => char.IsLetterOrDigit(c) || c == '/' || c == '+' || c == '=' || c == '\n'))
                    {
                        try { v = Encoding.UTF8.GetString(Convert.FromBase64String(v)); } catch { }
                    }
                    tlyric = v;
                }
            }

            if (string.IsNullOrWhiteSpace(lrc)) return null;
            return (lrc, string.IsNullOrWhiteSpace(tlyric) ? null : tlyric);
        }
        catch { return null; }
    }
}
