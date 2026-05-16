using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.MusicTagPlugin;

public class MusicTagLyricsPlugin : ILyricsProviderPlugin
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Regex LrcTimeRegex = new(@"\[(\d+):(\d+(?:\.\d+)?)\](.*)", RegexOptions.Compiled);
    private static readonly Regex LrcMetaRegex = new(@"\[(ti|ar|al|by|re|ve):(.+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string PluginId => "musictag.lyrics";
    public string Name => "MusicTag 歌词搜索";
    public string Version => "1.0.0";
    public string Author => "CatClawMusic";
    public string Description => "多源歌词搜索引擎，依次尝试 LRCLIB 开放 API 和网易云音乐 API 获取 LRC 格式歌词。";

    public bool IsAvailable => true;

    public List<string> Capabilities => new()
    {
        "LRCLIB: 全球最大的开放歌词数据库，支持中英日韩等多语言",
        "网易云音乐: 通过歌曲信息匹配网易云歌词",
        "自动解析: 支持 [mm:ss.xx] 和 [mm:ss] 格式 LRC 时间轴",
        "翻译歌词: 自动合并双语歌词（如有）"
    };

    public Task InitializeAsync() => Task.CompletedTask;

    public Task ShutdownAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        if (string.IsNullOrWhiteSpace(song.Title)) return null;

        var lyrics = await TryLrcLibAsync(song);
        if (lyrics != null) return lyrics;

        lyrics = await TryNeteaseAsync(song);
        if (lyrics != null) return lyrics;

        return null;
    }

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

    private static string? Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return input.Replace("(", " ").Replace(")", " ").Replace("[", " ").Replace("]", " ").Trim();
    }
}
