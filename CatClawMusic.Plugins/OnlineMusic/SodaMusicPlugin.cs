using System.Text.Json;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.OnlineMusic;

/// <summary>
/// 汽水音乐（抖音音乐）音源插件：仅搜索。播放直链需 Luna API 复杂签名，歌词亦暂不支持。
/// </summary>
public class SodaMusicPlugin : OnlineMusicPluginBase
{
    public override string PluginId => "sodaMusic";
    public override string Name => "汽水音乐";
    public override string Description => "汽水音乐（抖音音乐）在线音源（搜索）";
    public override string PlatformName => "soda";

    public override async Task<List<OnlineSong>?> SearchAsync(string keyword, int page = 1, int pageSize = 8)
    {
        try
        {
            var cursor = (page - 1) * pageSize;
            var url = $"https://www.douyin.com/aweme/v1/web/search/item/?keyword={Uri.EscapeDataString(keyword)}&type=music&cursor={cursor}&count={pageSize}&aid=6383";
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataArr) || dataArr.ValueKind != JsonValueKind.Array)
                return null;

            var results = new List<OnlineSong>();
            foreach (var item in dataArr.EnumerateArray())
            {
                if (!item.TryGetProperty("music_info", out var music)) continue;

                var singers = new List<string>();
                if (music.TryGetProperty("author_list", out var authorList) && authorList.ValueKind == JsonValueKind.Array)
                    foreach (var a in authorList.EnumerateArray())
                    {
                        var an = a.GetProperty("name").GetString();
                        if (!string.IsNullOrWhiteSpace(an)) singers.Add(an);
                    }
                if (singers.Count == 0 && music.TryGetProperty("author", out var auth))
                {
                    var an = auth.GetString();
                    if (!string.IsNullOrWhiteSpace(an)) singers.Add(an);
                }

                var coverUrl = "";
                if (music.TryGetProperty("cover_large", out var cov))
                {
                    if (cov.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in cov.EnumerateArray())
                        {
                            var u = c.GetString();
                            if (!string.IsNullOrWhiteSpace(u)) { coverUrl = u; break; }
                        }
                    }
                    else coverUrl = cov.GetString() ?? "";
                }

                var song = MakeSong(
                    id: music.TryGetProperty("id_str", out var idStr) ? idStr.GetString() ?? "" :
                         music.TryGetProperty("id", out var idNum) ? idNum.GetInt64().ToString() : "",
                    title: music.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    artist: string.Join(" / ", singers),
                    album: null,
                    durationMs: music.TryGetProperty("duration", out var dur) ? dur.GetInt64() * 1000 : 0,
                    coverUrl: coverUrl);
                results.Add(song);
            }
            return results;
        }
        catch { return null; }
    }

    public override Task<string?> GetPlayUrlAsync(OnlineSong song, int quality = 0)
        => Task.FromResult<string?>(null);

    public override Task<(string? Lrc, string? TLrc)?> GetLyricsAsync(OnlineSong song)
        => Task.FromResult<(string? Lrc, string? TLrc)?>(null);
}
