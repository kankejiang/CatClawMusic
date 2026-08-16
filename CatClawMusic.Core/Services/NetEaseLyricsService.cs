using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>获取网易云歌曲歌词三流（原文/译文/罗马音），失败返回 null。</summary>
    /// <param name="songId">网易云歌曲 ID</param>
    public static async Task<(string? Lrc, string? TLrc, string? RLrc)?> GetLyricsAsync(long songId)
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
            if (!response.IsSuccessStatusCode) return null;
            var raw = await response.Content.ReadAsByteArrayAsync();
            var body = DecodeBody(raw);
            if (string.IsNullOrWhiteSpace(body)) return null;

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("code", out var codeProp) || codeProp.GetInt32() != 200)
                return null;

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

            // 正文歌词优先取 yrc（网易云新版把正文放逐字流 yrc，lrc 只含署名行）；
            // yrc 缺失时回退 lrc。译文/罗马音分别取 tlyric/romalrc。
            var yrc = GetRawLyric("yrc");
            var lrcText = yrc != null ? ConvertYrcToLrc(yrc) : ToLrcText(GetRawLyric("lrc"));
            if (string.IsNullOrEmpty(lrcText)) return null;
            return (lrcText, ToLrcText(GetRawLyric("tlyric")), ToLrcText(GetRawLyric("romalrc")));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 网易云 yrc 逐字歌词格式 → 标准 LRC 逐字行。
    /// 格式：[行起始ms,行时长ms](字起始ms,字时长ms,0)字(字起始ms,字时长ms,0)字 ...
    /// 输出：[mm:ss.xxx]字[mm:ss.xxx]字... （每字一个绝对时间戳，宿主逐字分支自动生成 WordTimestamps）
    /// </summary>
    private static string? ConvertYrcToLrc(string yrc)
    {
        var sb = new StringBuilder(yrc.Length);
        foreach (var rawLine in yrc.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var lineMatch = System.Text.RegularExpressions.Regex.Match(line, @"^\[(\d+),(\d+)\]");
            if (!lineMatch.Success) continue;

            var wordMatches = System.Text.RegularExpressions.Regex.Matches(line, @"\((\d+),(\d+),\d+\)");
            var row = new StringBuilder();
            foreach (System.Text.RegularExpressions.Match wm in wordMatches)
            {
                var wordStart = wm.Index + wm.Length;
                var next = wm.NextMatch();
                var wordEnd = next.Success ? next.Index : line.Length;
                var word = line.Substring(wordStart, wordEnd - wordStart).Trim();
                if (word.Length == 0) continue;

                var ms = long.Parse(wm.Groups[1].Value);
                var ts = TimeSpan.FromMilliseconds(ms);
                row.Append('[').Append(ts.Minutes.ToString("D2")).Append(':')
                   .Append(ts.Seconds.ToString("D2")).Append('.').Append(ts.Milliseconds.ToString("D3"))
                   .Append(']').Append(word);
            }
            if (row.Length == 0) continue;
            sb.AppendLine(row.ToString());
        }
        return sb.Length > 0 ? sb.ToString() : null;
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

    /// <summary>按本地歌曲标题/歌手匹配网易云歌词：搜索 → 排序（标题/歌手匹配优先）→ 取歌词三流。</summary>
    public static async Task<(string? Lrc, string? TLrc, string? RLrc)?> MatchLocalSongAsync(string title, string? artist)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var keyword = string.IsNullOrWhiteSpace(artist)
            ? title
            : $"{title} {artist}";
        var results = await SearchAsync(keyword);
        if (results == null || results.Count == 0) return null;

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
            if (lyrics != null && !string.IsNullOrWhiteSpace(lyrics.Value.Lrc))
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

    /// <summary>
    /// 网易云新版歌词为逐行 JSON 格式（每行一个对象 {"t":毫秒,"c":[{"tx":片段}]}），
    /// 转成标准 LRC 文本；纯 LRC 文本原样返回。
    /// </summary>
    private static string? ToLrcText(string? lyric)
    {
        if (string.IsNullOrWhiteSpace(lyric)) return null;
        if (!lyric.Contains("\"t\":"))
            return lyric;

        var sb = new StringBuilder(lyric.Length);
        foreach (var rawLine in lyric.Replace("\r\n", "\n").Split('\n'))
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
                var ts = TimeSpan.FromMilliseconds(ms);
                sb.Append('[').Append(ts.Minutes.ToString("D2")).Append(':')
                  .Append(ts.Seconds.ToString("D2")).Append('.').Append(ts.Milliseconds.ToString("D3"))
                  .Append(']').AppendLine(text.ToString());
            }
            catch { /* 忽略无法解析的行 */ }
        }
        return sb.Length > 0 ? sb.ToString() : null;
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
