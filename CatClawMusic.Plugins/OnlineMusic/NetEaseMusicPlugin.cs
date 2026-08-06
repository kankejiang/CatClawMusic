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

        // 方案1：免登录外链（302 到 CDN，免 cookie，实测成功率最高）
        try
        {
            var outer = $"https://music.163.com/song/media/outer/url?id={song.Id}.mp3";
            using var resp = await Http.GetAsync(outer, HttpCompletionOption.ResponseHeadersRead);
            if (resp.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.PartialContent)
                return resp.RequestMessage?.RequestUri?.ToString() ?? outer;
        }
        catch { }

        // 方案2：enhance/player/url + 静态 cookie（免登录接口不带 cookie 会风控返回空 url）
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://music.163.com/api/song/enhance/player/url?id={song.Id}&ids=[{song.Id}]&br=320000");
            req.Headers.TryAddWithoutValidation("Cookie", "os=pc; appver=8.9.70");
            using var resp = await Http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
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

    /// <summary>热门歌单（支持分类；"全部"/空 = 全部分类）</summary>
    public override async Task<List<OnlinePlaylist>> GetPlaylistsAsync(string? category = null)
    {
        try
        {
            var cat = string.IsNullOrWhiteSpace(category) || category == "全部" ? "全部" : category.Trim();
            var url = $"https://music.163.com/api/playlist/list?cat={Uri.EscapeDataString(cat)}&order=hot&limit=60&offset=0";
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("playlists", out var pls) || pls.ValueKind != JsonValueKind.Array)
                return new List<OnlinePlaylist>();

            var list = new List<OnlinePlaylist>();
            foreach (var pl in pls.EnumerateArray())
            {
                list.Add(new OnlinePlaylist
                {
                    Id = pl.TryGetProperty("id", out var idEl) ? idEl.GetInt64().ToString() : "",
                    Platform = PlatformName,
                    Name = pl.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    CoverUrl = pl.TryGetProperty("coverImgUrl", out var c) ? c.GetString() : null,
                    Description = pl.TryGetProperty("description", out var d) ? d.GetString() : null,
                    SongCount = pl.TryGetProperty("trackCount", out var tc) ? tc.GetInt32() : 0,
                });
            }
            return list;
        }
        catch { return new List<OnlinePlaylist>(); }
    }

    /// <summary>歌单内歌曲（v6 歌单详情，n=1000 拉全量，支持分页）</summary>
    public override async Task<List<OnlineSong>?> GetPlaylistSongsAsync(OnlinePlaylist playlist, int page = 1, int pageSize = 50)
    {
        if (string.IsNullOrWhiteSpace(playlist.Id)) return null;
        try
        {
            var url = $"https://music.163.com/api/v6/playlist/detail?id={playlist.Id}&n=1000&s=8";
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("playlist", out var pl) ||
                !pl.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
                return null;

            var all = new List<OnlineSong>();
            foreach (var s in tracks.EnumerateArray())
            {
                var artists = new List<string>();
                if (s.TryGetProperty("ar", out var ar))
                    foreach (var a in ar.EnumerateArray())
                    {
                        var an = a.TryGetProperty("name", out var anEl) ? anEl.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(an)) artists.Add(an);
                    }

                all.Add(MakeSong(
                    id: s.TryGetProperty("id", out var idEl) ? idEl.GetInt64().ToString() : "",
                    title: s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    artist: string.Join(" / ", artists),
                    album: s.TryGetProperty("al", out var al) && al.TryGetProperty("name", out var aln) ? aln.GetString() : null,
                    durationMs: s.TryGetProperty("dt", out var dt) ? dt.GetInt64() : 0,
                    coverUrl: s.TryGetProperty("al", out var alc) && alc.TryGetProperty("picUrl", out var pic) ? pic.GetString() : null));
            }

            var start = (page - 1) * pageSize;
            if (start >= all.Count) return new List<OnlineSong>();
            return all.Skip(start).Take(pageSize).ToList();
        }
        catch { return null; }
    }
}
