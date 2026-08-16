using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 网易云音乐歌词服务（宿主直连，不依赖插件）。
/// 走 eapi 加密接口 /eapi/song/lyric/v1（与 lx-music 网易云音源同款），一次返回多流：
/// lrc（原文，可能为增强 JSON 格式）、tlyric（译文）、romalrc（罗马音）。
/// 老接口 /api/song/lyric 已降级（不再返回 tlyric/romalrc），故用 eapi 版。
/// </summary>
public static class NetEaseLyricsService
{
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 6,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    })
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    /// <summary>eapi 加密密钥（lx-music 同款）</summary>
    private static readonly byte[] EapiKey = Encoding.UTF8.GetBytes("e82ckenh8dichen8");

    static NetEaseLyricsService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/60.0.3112.90 Safari/537.36");
        _http.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
        _http.DefaultRequestHeaders.Add("origin", "https://music.163.com");
        // 与 lx-music 同款请求 cookie（os=pc 等设备指纹，接口校验用）
        _http.DefaultRequestHeaders.Add("cookie",
            "os=pc; deviceId=A9C064BB4584D038B1565B58CB05F95290998EE8B025AA2D07AE; " +
            "osver=Microsoft-Windows-10-Home-China-build-19043-64bit; appver=2.5.2.197409; channel=netease; " +
            "__csrf=05b50d54082694f945d7de75c210ef94; mode=Z7M-KP5(7)GZ; " +
            "NMTID=00OZLp2VVgq9QdwokUgq3XNfOddQyIAAAF_6i8eJg; ntes_kaola_ad=1");
    }

    /// <summary>获取网易云歌曲歌词（原文行 + 逐字时间戳 + 译文/罗马音已并入），失败返回 null。</summary>
    /// <param name="songId">网易云歌曲 ID</param>
    public static async Task<LrcLyrics?> GetLyricsAsync(long songId)
    {
        try
        {
            // 正文歌词：yrc 逐字流优先（有逐字时间戳）；yrc 缺失时回退老接口 lrc（纯 LRC 文本含正文，
            // eapi 的 lrc 只有署名行）。译文/罗马音：eapi 的 tlyric/romalrc。
            var (yrc, tlrcText, rlrcText) = await FetchEapiLyricsAsync(songId);
            Log.Debug("LyricsService", $"[Lyrics] 网易云 id={songId}: yrc={(yrc != null ? yrc.Length + "B" : "null")}, tlyric={(tlrcText != null ? tlrcText.Length + "B" : "null")}, romalrc={(rlrcText != null ? rlrcText.Length + "B" : "null")}");

            List<LrcLyricLine> lrcLines;
            if (!string.IsNullOrEmpty(yrc))
            {
                lrcLines = ParseYrcLines(yrc);
            }
            else
            {
                var legacyLrc = await FetchLegacyLrcAsync(songId);
                Log.Debug("LyricsService", $"[Lyrics] 网易云 id={songId}: 老接口 lrc={(legacyLrc != null ? legacyLrc.Length + "B" : "null")}");
                lrcLines = ParseSimpleLrcText(legacyLrc) ?? new List<LrcLyricLine>();
            }
            if (lrcLines.Count == 0)
            {
                Log.Debug("LyricsService", $"[Lyrics] 网易云 id={songId}: 正文行为空");
                return null;
            }

            var lyrics = new LrcLyrics { Lines = lrcLines };
            lyrics.TranslationLines = ParseSimpleLrcText(tlrcText);
            lyrics.RomaLines = ParseSimpleLrcText(rlrcText);
            MergeExternalLines(lyrics);
            return lyrics;
        }
        catch (Exception ex)
        {
            Log.Debug("LyricsService", $"[Lyrics] 网易云取歌词异常 id={songId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>eapi 加密接口：取 yrc 逐字正文 + 译文(tlyric) + 罗马音(romalrc)。</summary>
    private static async Task<(string? Yrc, string? TLrc, string? RLrc)> FetchEapiLyricsAsync(long songId)
    {
        try
        {
            // eapi payload 中的 url 必须是 /api/song/lyric/v1（不带 eapi 前缀），md5 校验用
            const string payloadUrl = "/api/song/lyric/v1";
            var data = JsonSerializer.Serialize(new
            {
                id = songId.ToString(),
                cp = false,
                tv = 0,
                lv = 0,
                rv = 0,
                kv = 0,
                yv = 0,
                ytv = 0,
                yrv = 0
            });
            var message = $"nobody{payloadUrl}use{data}md5forencrypt";
            var digest = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
            var plain = $"{payloadUrl}-36cd479b6b5-{data}-36cd479b6b5-{digest}";
            var paramsHex = AesEcbEncryptHex(plain).ToUpperInvariant();

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://interface3.music.163.com/eapi/song/lyric/v1");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["params"] = paramsHex });

            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return (null, null, null);
            var raw = await response.Content.ReadAsByteArrayAsync();
            var body = DecodeBody(raw);
            if (string.IsNullOrWhiteSpace(body)) return (null, null, null);

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("code", out var codeProp) || codeProp.GetInt32() != 200)
                return (null, null, null);

            string? GetRawLyric(string prop)
            {
                if (doc.RootElement.TryGetProperty(prop, out var node)
                    && node.ValueKind == JsonValueKind.Object
                    && node.TryGetProperty("lyric", out var lyric)
                    && lyric.ValueKind == JsonValueKind.String)
                {
                    var text = lyric.GetString() ?? "";
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }
                return null;
            }

            return (GetRawLyric("yrc"), GetRawLyric("tlyric"), GetRawLyric("romalrc"));
        }
        catch
        {
            return (null, null, null);
        }
    }

    /// <summary>老接口（免登录）：lrc 为纯 LRC 文本且含完整正文（eapi 的 lrc 只有署名行）。</summary>
    private static async Task<string?> FetchLegacyLrcAsync(long songId)
    {
        try
        {
            var url = $"https://music.163.com/api/song/lyric?id={songId}&lv=1&kv=1&tv=-1&rv=-1";
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("code", out var codeProp) || codeProp.GetInt32() != 200)
                return null;
            if (doc.RootElement.TryGetProperty("lrc", out var lrcNode)
                && lrcNode.ValueKind == JsonValueKind.Object
                && lrcNode.TryGetProperty("lyric", out var lyric)
                && lyric.ValueKind == JsonValueKind.String)
            {
                var text = lyric.GetString() ?? "";
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 网易云 yrc 逐字歌词格式 → 歌词行（含逐字时间戳，无需 LRC 文本中间态）。
    /// 格式：[行起始ms,行时长ms](字起始ms,字时长ms,0)字(字起始ms,字时长ms,0)字 ...
    /// </summary>
    private static List<LrcLyricLine> ParseYrcLines(string yrc)
    {
        var lines = new List<LrcLyricLine>();
        foreach (var rawLine in yrc.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var lineMatch = System.Text.RegularExpressions.Regex.Match(line, @"^\[(\d+),(\d+)\]");
            if (!lineMatch.Success) continue;
            var lineStartMs = long.Parse(lineMatch.Groups[1].Value);

            var wordMatches = System.Text.RegularExpressions.Regex.Matches(line, @"\((\d+),(\d+),\d+\)");
            var text = new StringBuilder();
            var words = new List<WordTimestamp>();
            foreach (System.Text.RegularExpressions.Match wm in wordMatches)
            {
                var wordStart = wm.Index + wm.Length;
                var next = wm.NextMatch();
                var wordEnd = next.Success ? next.Index : line.Length;
                var word = line.Substring(wordStart, wordEnd - wordStart).Trim();
                if (word.Length == 0) continue;

                var startMs = long.Parse(wm.Groups[1].Value);
                var durMs = long.Parse(wm.Groups[2].Value);
                words.Add(new WordTimestamp
                {
                    Word = word,
                    Start = TimeSpan.FromMilliseconds(startMs),
                    Duration = TimeSpan.FromMilliseconds(durMs)
                });
                text.Append(word);
            }
            if (text.Length == 0) continue;

            lines.Add(new LrcLyricLine
            {
                Timestamp = TimeSpan.FromMilliseconds(lineStartMs),
                Text = text.ToString(),
                WordTimestamps = words.Count > 0 ? words : null
            });
        }
        return lines;
    }

    /// <summary>
    /// 网易云增强歌词 JSON（逐行 {"t":毫秒,"c":[{"tx":片段}]}）→ 歌词行。
    /// </summary>
    private static List<LrcLyricLine> ParseEnhancedJsonLines(string? json)
    {
        var lines = new List<LrcLyricLine>();
        if (string.IsNullOrWhiteSpace(json) || !json.Contains("\"t\":")) return lines;
        foreach (var rawLine in json.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("t", out var tProp)) continue;
                var ms = tProp.GetInt64();
                var text = new StringBuilder();
                if (root.TryGetProperty("c", out var c) && c.ValueKind == JsonValueKind.Array)
                {
                    foreach (var seg in c.EnumerateArray())
                    {
                        if (seg.TryGetProperty("tx", out var tx) && tx.ValueKind == JsonValueKind.String)
                            text.Append(tx.GetString());
                    }
                }
                if (text.Length == 0) continue;
                lines.Add(new LrcLyricLine
                {
                    Timestamp = TimeSpan.FromMilliseconds(ms),
                    Text = text.ToString()
                });
            }
            catch { }
        }
        return lines;
    }

    /// <summary>
    /// 标准 LRC 文本流（译文 tlyric / 罗马音 romalrc）→ 行列表（仅取 [mm:ss(.xxx)]文本 行）。
    /// </summary>
    private static List<LrcLyricLine>? ParseSimpleLrcText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // 增强 JSON 流（部分源译文也用 JSON）
        if (text.Contains("\"t\":"))
            return ParseEnhancedJsonLines(text);

        var lines = new List<LrcLyricLine>();
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            var m = System.Text.RegularExpressions.Regex.Match(line, @"^\[(\d+):(\d+)(?:\.(\d+))?\]");
            if (!m.Success) continue;
            var minutes = int.Parse(m.Groups[1].Value);
            var seconds = int.Parse(m.Groups[2].Value);
            var millis = m.Groups[3].Success
                ? int.Parse(m.Groups[3].Value.PadRight(3, '0').Substring(0, 3))
                : 0;
            var lyric = line.Substring(m.Index + m.Length).Trim();
            if (lyric.Length == 0) continue;
            lines.Add(new LrcLyricLine
            {
                Timestamp = new TimeSpan(0, 0, minutes, seconds, millis),
                Text = lyric
            });
        }
        return lines.Count > 0 ? lines : null;
    }

    /// <summary>把译文流/罗马音流按时间戳（容差 300ms）并入主行。</summary>
    private static void MergeExternalLines(LrcLyrics lyrics)
    {
        if (lyrics.TranslationLines != null)
        {
            foreach (var t in lyrics.TranslationLines)
            {
                var target = FindLineAt(lyrics.Lines, t.Timestamp);
                if (target != null && string.IsNullOrEmpty(target.Translation))
                    target.Translation = t.Text;
            }
        }
        if (lyrics.RomaLines != null)
        {
            foreach (var r in lyrics.RomaLines)
            {
                var target = FindLineAt(lyrics.Lines, r.Timestamp);
                if (target != null && string.IsNullOrEmpty(target.Roma))
                    target.Roma = r.Text;
            }
        }
    }

    private static LrcLyricLine? FindLineAt(List<LrcLyricLine> lines, TimeSpan ts)
    {
        foreach (var l in lines)
        {
            if (Math.Abs((l.Timestamp - ts).TotalMilliseconds) < 300)
                return l;
        }
        return null;
    }

    private sealed class NeteaseSearchSong
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("artists")] public List<NeteaseArtist>? Artists { get; set; }
    }

    private sealed class NeteaseArtist
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    private sealed class NeteaseSearchRoot
    {
        [JsonPropertyName("result")] public NeteaseSearchResult? Result { get; set; }
    }

    private sealed class NeteaseSearchResult
    {
        [JsonPropertyName("songs")] public List<NeteaseSearchSong>? Songs { get; set; }
    }

    /// <summary>按关键词搜索网易云歌曲（免登录接口），返回最多 8 条</summary>
    public static async Task<List<(long Id, string Name, string Artists)>?> SearchAsync(string keyword)
    {
        try
        {
            var url = "https://music.163.com/api/search/get/web?csrf_token=&type=1&s="
                      + Uri.EscapeDataString(keyword) + "&limit=8";
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsStringAsync();
            var root = JsonSerializer.Deserialize<NeteaseSearchRoot>(body);
            return root?.Result?.Songs?
                .Select(s => (s.Id, s.Name, string.Join(", ", s.Artists?.Select(a => a.Name) ?? new List<string>())))
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>按本地歌曲标题/歌手匹配网易云歌词：搜索 → 排序（标题/歌手匹配优先）→ 取歌词。</summary>
    public static async Task<LrcLyrics?> MatchLocalSongAsync(string title, string? artist)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var keyword = string.IsNullOrWhiteSpace(artist)
            ? title
            : $"{title} {artist}";
        var results = await SearchAsync(keyword);
        // 标题+歌手搜不到（歌手名解析差异/特殊字符）时，回退只按标题搜索
        if (results == null || results.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(artist) && !title.Equals(keyword, StringComparison.Ordinal))
                results = await SearchAsync(title);
        }
        if (results == null || results.Count == 0)
        {
            Log.Debug("LyricsService", $"[Lyrics] 网易云搜索无结果: '{keyword}'");
            return null;
        }
        Log.Debug("LyricsService", $"[Lyrics] 网易云搜索 '{keyword}' 命中 {results.Count} 个候选");

        // 排序：歌手匹配优先，其次标题精确/包含匹配
        List<(long Id, string Name, string Artists)> ordered;
        if (!string.IsNullOrWhiteSpace(artist))
        {
            ordered = results
                .OrderByDescending(r => r.Artists.Contains(artist, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(r => r.Name.Equals(title, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else
        {
            ordered = results
                .OrderByDescending(r => r.Name.Equals(title, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(r => r.Name.Contains(title, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // 前 3 个候选尝试取歌词（防标题撞车），第一个成功即返回
        foreach (var cand in ordered.Take(3))
        {
            var lyrics = await GetLyricsAsync(cand.Id);
            Log.Debug("LyricsService", $"[Lyrics] 网易云候选 {cand.Id}({cand.Name}-{cand.Artists}) 歌词行数: {lyrics?.Lines.Count ?? 0}");
            if (lyrics != null && lyrics.Lines.Count > 0)
                return lyrics;
        }
        return null;
    }

    /// <summary>响应解码：兼容明文与 eapi 密文两种返回</summary>
    private static string? DecodeBody(byte[] raw)
    {
        // 明文 JSON 以 { 开头
        if (raw.Length > 0 && raw[0] == (byte)'{')
            return Encoding.UTF8.GetString(raw);
        try { return AesEcbDecrypt(raw); }
        catch { return null; }
    }

    private static string AesEcbEncryptHex(string text)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = EapiKey;
        using var enc = aes.CreateEncryptor();
        var bytes = enc.TransformFinalBlock(Encoding.UTF8.GetBytes(text), 0, Encoding.UTF8.GetByteCount(text));
        return Convert.ToHexString(bytes);
    }

    private static string AesEcbDecrypt(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = EapiKey;
        using var dec = aes.CreateDecryptor();
        var bytes = dec.TransformFinalBlock(data, 0, data.Length);
        return Encoding.UTF8.GetString(bytes);
    }
}
