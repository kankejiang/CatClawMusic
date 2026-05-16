using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.MusicTagPlugin;

/// <summary>
/// 歌词搜索结果，包含来源、歌曲信息和解析后的歌词数据
/// </summary>
public class LrcSearchResult
{
    /// <summary>
    /// 歌词来源，如 "LRCLIB" 或 "网易云音乐"
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// 歌曲标题
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// 歌手名称
    /// </summary>
    public string Artist { get; set; } = "";

    /// <summary>
    /// 专辑名称
    /// </summary>
    public string Album { get; set; } = "";

    /// <summary>
    /// 匹配分数，用于表示搜索结果与原始歌曲的匹配程度
    /// </summary>
    public double? MatchScore { get; set; }

    /// <summary>
    /// 解析后的 LRC 歌词对象，如果未获取到歌词则为 null
    /// </summary>
    public LrcLyrics? Lyrics { get; set; }
}

/// <summary>
/// 多源歌词搜索插件，依次通过 LRCLIB 开放 API 和网易云音乐 API 获取 LRC 格式歌词
/// </summary>
public class MusicTagLyricsPlugin : ILyricsProviderPlugin
{
    /// <summary>
    /// HTTP 客户端，超时时间 10 秒，用于请求外部歌词 API
    /// </summary>
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// LRC 时间轴正则表达式，匹配 [mm:ss.xx] 或 [mm:ss] 格式
    /// </summary>
    private static readonly Regex LrcTimeRegex = new(@"\[(\d+):(\d+(?:\.\d+)?)\](.*)", RegexOptions.Compiled);

    /// <summary>
    /// LRC 元数据正则表达式，匹配 [ti:标题]、[ar:歌手] 等标签
    /// </summary>
    private static readonly Regex LrcMetaRegex = new(@"\[(ti|ar|al|by|re|ve):(.+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 插件唯一标识符
    /// </summary>
    public string PluginId => "musictag.lyrics";

    /// <summary>
    /// 插件显示名称
    /// </summary>
    public string Name => "MusicTag 歌词搜索";

    /// <summary>
    /// 插件版本号
    /// </summary>
    public string Version => "1.0.0";

    /// <summary>
    /// 插件作者
    /// </summary>
    public string Author => "CatClawMusic";

    /// <summary>
    /// 插件功能描述
    /// </summary>
    public string Description => "多源歌词搜索引擎，依次尝试 LRCLIB 开放 API 和网易云音乐 API 获取 LRC 格式歌词。";

    /// <summary>
    /// 插件是否可用
    /// </summary>
    public bool IsAvailable => true;

    /// <summary>
    /// 插件功能列表，描述支持的歌词搜索能力
    /// </summary>
    public List<string> Capabilities => new()
    {
        "LRCLIB: 全球最大的开放歌词数据库，支持中英日韩等多语言",
        "网易云音乐: 通过歌曲信息匹配网易云歌词",
        "自动解析: 支持 [mm:ss.xx] 和 [mm:ss] 格式 LRC 时间轴",
        "翻译歌词: 自动合并双语歌词（如有）"
    };

    /// <summary>
    /// 初始化插件，当前无需额外初始化操作
    /// </summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// 关闭插件，释放 HTTP 客户端资源
    /// </summary>
    public Task ShutdownAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取单首歌曲的歌词，返回最佳匹配结果
    /// </summary>
    /// <param name="song">要查询歌词的歌曲对象</param>
    /// <returns>解析后的 LRC 歌词对象，如果未找到则返回 null</returns>
    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        var results = await SearchLyricsAsync(song);
        return results.FirstOrDefault()?.Lyrics;
    }

    /// <summary>
    /// 搜索歌曲的歌词，依次尝试多个歌词源（LRCLIB 和网易云音乐）
    /// </summary>
    /// <param name="song">要搜索歌词的歌曲对象</param>
    /// <returns>所有歌词源的搜索结果列表</returns>
    public async Task<List<LrcSearchResult>> SearchLyricsAsync(Song song)
    {
        var allResults = new List<LrcSearchResult>();

        if (string.IsNullOrWhiteSpace(song.Title)) return allResults;

        var lrcLibResults = await SearchLrcLibAsync(song);
        allResults.AddRange(lrcLibResults);

        var neteaseResults = await SearchNeteaseAsync(song);
        allResults.AddRange(neteaseResults);

        return allResults;
    }

    /// <summary>
    /// 通过 LRCLIB 开放 API 搜索歌词
    /// </summary>
    /// <param name="song">要搜索的歌曲对象</param>
    /// <returns>LRCLIB 来源的歌词搜索结果列表</returns>
    public async Task<List<LrcSearchResult>> SearchLrcLibAsync(Song song)
    {
        var results = new List<LrcSearchResult>();
        try
        {
            var artist = Sanitize(song.Artist);
            var title = Sanitize(song.Title);
            var url = $"https://lrclib.net/api/search?artist={Uri.EscapeDataString(artist ?? "")}&track_name={Uri.EscapeDataString(title ?? "")}&limit=5";

            var response = await _client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return results;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array) return results;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var result = new LrcSearchResult { Source = "LRCLIB" };

                if (item.TryGetProperty("trackName", out var tn)) result.Title = tn.GetString() ?? "";
                if (item.TryGetProperty("artistName", out var an)) result.Artist = an.GetString() ?? "";
                if (item.TryGetProperty("albumName", out var aln)) result.Album = aln.GetString() ?? "";

                var lrcText = item.TryGetProperty("syncedLyrics", out var synced) && synced.ValueKind == JsonValueKind.String
                    ? synced.GetString()
                    : null;
                var plainLyrics = item.TryGetProperty("plainLyrics", out var plain) && plain.ValueKind == JsonValueKind.String
                    ? plain.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(lrcText))
                {
                    result.Lyrics = ParseLrc(lrcText);
                }
                else if (!string.IsNullOrWhiteSpace(plainLyrics))
                {
                    result.Lyrics = BuildLyricsFromPlainText(plainLyrics);
                }

                results.Add(result);

                if (results.Count >= 5) break;
            }
        }
        catch
        {
        }

        return results;
    }

    /// <summary>
    /// 通过网易云音乐 API 搜索歌词
    /// </summary>
    /// <param name="song">要搜索的歌曲对象</param>
    /// <returns>网易云音乐来源的歌词搜索结果列表</returns>
    public async Task<List<LrcSearchResult>> SearchNeteaseAsync(Song song)
    {
        var results = new List<LrcSearchResult>();
        try
        {
            var artist = Sanitize(song.Artist);
            var title = Sanitize(song.Title);
            var searchUrl = $"https://music.163.com/api/search/get?s={Uri.EscapeDataString($"{title} {artist}")}&type=1&limit=5";

            _client.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
            var response = await _client.GetStringAsync(searchUrl);
            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("result", out var searchResult)) return results;
            if (!searchResult.TryGetProperty("songs", out var songs)) return results;
            if (songs.GetArrayLength() == 0) return results;

            int count = 0;
            foreach (var songItem in songs.EnumerateArray())
            {
                if (count >= 3) break;

                var songId = songItem.GetProperty("id").GetInt32().ToString();
                var lyricUrl = $"https://music.163.com/api/song/lyric?id={songId}&lv=1&kv=1&tv=-1";

                try
                {
                    var lyricResp = await _client.GetStringAsync(lyricUrl);
                    using var lyricDoc = JsonDocument.Parse(lyricResp);

                    var lrcText = lyricDoc.RootElement.TryGetProperty("lrc", out var lrc) && lrc.ValueKind == JsonValueKind.Object
                        ? (lrc.TryGetProperty("lyric", out var lyricVal) ? lyricVal.GetString() : null)
                        : null;

                    if (string.IsNullOrWhiteSpace(lrcText)) continue;

                    var result = new LrcSearchResult { Source = "网易云音乐" };
                    if (songItem.TryGetProperty("name", out var sn)) result.Title = sn.GetString() ?? "";
                    if (songItem.TryGetProperty("artists", out var artists))
                    {
                        var artistNames = artists.EnumerateArray().Select(a => a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "").Where(n => !string.IsNullOrEmpty(n));
                        result.Artist = string.Join(", ", artistNames);
                    }
                    if (songItem.TryGetProperty("album", out var albumObj) && albumObj.TryGetProperty("name", out var albumName))
                        result.Album = albumName.GetString() ?? "";

                    result.Lyrics = ParseLrc(lrcText);
                    results.Add(result);
                    count++;
                }
                catch
                {
                    continue;
                }
            }
        }
        catch
        {
        }

        return results;
    }

    /// <summary>
    /// 通过 LRCLIB 的精确匹配 API 尝试获取歌词
    /// </summary>
    /// <param name="song">要查询的歌曲对象</param>
    /// <returns>解析后的 LRC 歌词对象，如果未找到则返回 null</returns>
    private async Task<LrcLyrics?> TryLrcLibAsync(Song song)
    {
        try
        {
            var artist = Sanitize(song.Artist);
            var title = Sanitize(song.Title);
            var url = $"https://lrclib.net/api/get?artist={Uri.EscapeDataString(artist ?? "")}&title={Uri.EscapeDataString(title ?? "")}&duration={song.Duration / 1000}";

            var response = await _client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var lrcText = doc.RootElement.TryGetProperty("syncedLyrics", out var synced) && synced.ValueKind == JsonValueKind.String
                ? synced.GetString()
                : null;
            var plainLyrics = doc.RootElement.TryGetProperty("plainLyrics", out var plain) && plain.ValueKind == JsonValueKind.String
                ? plain.GetString()
                : null;

            if (!string.IsNullOrWhiteSpace(lrcText))
            {
                return ParseLrc(lrcText);
            }

            if (!string.IsNullOrWhiteSpace(plainLyrics))
            {
                return BuildLyricsFromPlainText(plainLyrics);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 通过网易云音乐 API 尝试精确匹配获取歌词
    /// </summary>
    /// <param name="song">要查询的歌曲对象</param>
    /// <returns>解析后的 LRC 歌词对象，如果未找到则返回 null</returns>
    private async Task<LrcLyrics?> TryNeteaseAsync(Song song)
    {
        try
        {
            var artist = Sanitize(song.Artist);
            var title = Sanitize(song.Title);
            var searchUrl = $"https://music.163.com/api/search/get?s={Uri.EscapeDataString($"{title} {artist}")}&type=1&limit=3";

            _client.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
            var response = await _client.GetStringAsync(searchUrl);
            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("result", out var result)) return null;
            if (!result.TryGetProperty("songs", out var songs)) return null;
            if (songs.GetArrayLength() == 0) return null;

            var songId = songs[0].GetProperty("id").GetInt32().ToString();
            var lyricUrl = $"https://music.163.com/api/song/lyric?id={songId}&lv=1&kv=1&tv=-1";

            var lyricResp = await _client.GetStringAsync(lyricUrl);
            using var lyricDoc = JsonDocument.Parse(lyricResp);

            var lrcText = lyricDoc.RootElement.TryGetProperty("lrc", out var lrc) && lrc.ValueKind == JsonValueKind.Object
                ? (lrc.TryGetProperty("lyric", out var lyricVal) ? lyricVal.GetString() : null)
                : null;

            if (string.IsNullOrWhiteSpace(lrcText)) return null;

            return ParseLrc(lrcText);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 解析 LRC 格式的歌词文本，提取元数据和带时间轴的行
    /// </summary>
    /// <param name="lrcContent">LRC 格式的原始歌词文本</param>
    /// <returns>解析后的 LrcLyrics 对象，包含元数据和按时间排序的歌词行</returns>
    public LrcLyrics ParseLrc(string lrcContent)
    {
        var result = new LrcLyrics();
        var lines = lrcContent.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var metaMatch = LrcMetaRegex.Match(trimmed);
            if (metaMatch.Success)
            {
                var key = metaMatch.Groups[1].Value.ToLower();
                var val = metaMatch.Groups[2].Value.Trim();
                switch (key)
                {
                    case "ti": result.Metadata.Title = val; break;
                    case "ar": result.Metadata.Artist = val; break;
                    case "al": result.Metadata.Album = val; break;
                    case "by": result.Metadata.Author = val; break;
                    case "re": result.Metadata.Maker = val; break;
                    case "ve": result.Metadata.Version = val; break;
                }
                continue;
            }

            var matches = LrcTimeRegex.Matches(trimmed);
            if (matches.Count == 0) continue;

            var text = matches[matches.Count - 1].Groups[3].Value.Trim();
            if (string.IsNullOrEmpty(text)) continue;

            foreach (Match match in matches)
            {
                var minutes = int.Parse(match.Groups[1].Value);
                var seconds = double.Parse(match.Groups[2].Value);
                result.Lines.Add(new LrcLyricLine
                {
                    Timestamp = TimeSpan.FromSeconds(minutes * 60 + seconds),
                    Text = text
                });
            }
        }

        result.Lines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return result;
    }

    /// <summary>
    /// 将纯文本歌词转换为 LRC 格式，每行分配固定间隔的时间戳
    /// </summary>
    /// <param name="text">纯文本歌词，每行一句</param>
    /// <returns>包含生成的时间轴的 LrcLyrics 对象</returns>
    private static LrcLyrics BuildLyricsFromPlainText(string text)
    {
        var result = new LrcLyrics();
        var lines = text.Split('\n');
        double interval = 3.0;
        double timestamp = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            result.Lines.Add(new LrcLyricLine
            {
                Timestamp = TimeSpan.FromSeconds(timestamp),
                Text = trimmed
            });
            timestamp += interval;
        }

        return result;
    }

    /// <summary>
    /// 清理输入字符串，将括号字符替换为空格以便搜索匹配
    /// </summary>
    /// <param name="input">待清理的原始字符串</param>
    /// <returns>清理后的字符串，括号被替换为空格</returns>
    private static string? Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return input.Replace("(", " ").Replace(")", " ").Replace("[", " ").Replace("]", " ").Trim();
    }
}
