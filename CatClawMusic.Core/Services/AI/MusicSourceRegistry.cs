using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// 音乐源注册表：源配置的存储、默认值、模板执行器。
/// 模板 = 平台接口协议的"可配置版本"：kuwo_jsonp（酷我 r.s 搜索 + mobi.s 免签名直链，
/// 支持真无损 FLAC）/ netease_eapi（网易云 eapi 加密取链，320K）。
/// 平台改版时只需更新配置（由 update_music_source 工具自动完成），无需改代码。
/// </summary>
public static class MusicSourceRegistry
{
    private const string StoreKey = "music_sources_v1";

    private static IAgentConfigStorage? _storage;
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 6,
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    static MusicSourceRegistry()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>初始化存储（MauiProgram 调用）</summary>
    public static void Initialize(IAgentConfigStorage storage) => _storage = storage;

    private static IAgentConfigStorage Storage =>
        _storage ?? throw new InvalidOperationException("MusicSourceRegistry 未初始化，请先调用 Initialize()");

    /// <summary>内置默认源（平台接口协议完全可配，失效时由 AI 更新）</summary>
    public static List<MusicSourceConfig> DefaultSources() => new()
    {
        new MusicSourceConfig
        {
            Id = "kuwo",
            Name = "酷我音乐",
            Template = "kuwo_jsonp",
            Search = new SourceHttpSpec
            {
                Url = "http://search.kuwo.cn/r.s",
                Params = new() { ["all"] = "{keyword}", ["ft"] = "music", ["itemset"] = "web_2013", ["rformat"] = "json", ["encoding"] = "utf8", ["pn"] = "0", ["rn"] = "10" },
                Encoding = "gbk"
            },
            UrlApi = new SourceHttpSpec
            {
                Url = "https://mobi.kuwo.cn/mobi.s",
                Params = new() { ["f"] = "web", ["rid"] = "{id}", ["br"] = "{quality}", ["source"] = "jiakong", ["type"] = "convert_url_with_sign", ["surl"] = "1" },
                Headers = new() { ["Referer"] = "https://www.kuwo.cn/" }
            },
            QualityMap = new() { ["flac"] = "2000kflac", ["320k"] = "320kmp3", ["128k"] = "128kmp3" },
            Regexes = new SourceRegexSpec
            {
                BlockSplit = @"\{'AARTIST'",
                BlockMarker = "'SONGNAME'",
                IdPattern = @"'MUSICRID':\s*'([^']*)'",
                NamePattern = @"'SONGNAME':\s*'([^']*)'",
                ArtistPattern = @"'ARTIST':\s*'([^']*)'",
                FormatsPattern = @"'FORMATS':\s*'([^']*)'"
            }
        },
        new MusicSourceConfig
        {
            Id = "netease",
            Name = "网易云音乐",
            Template = "netease_eapi",
            Search = new SourceHttpSpec
            {
                Url = "https://music.163.com/api/search/get/web",
                Params = new() { ["csrf_token"] = "", ["type"] = "1", ["s"] = "{keyword}", ["limit"] = "8" },
                Encoding = "utf8"
            },
            UrlApi = new SourceHttpSpec
            {
                Url = "https://interface3.music.163.com/eapi/song/enhance/player/url/v1",
                Params = new() { ["eapi_path"] = "/api/song/enhance/player/url/v1", ["eapi_key"] = "e82ckenh8dichen8", ["level"] = "{quality}", ["encode_type"] = "flac", ["immerse_type"] = "c51", ["device_id"] = "pyncm!" },
                Encoding = "utf8"
            },
            QualityMap = new() { ["flac"] = "lossless", ["320k"] = "exhigh", ["128k"] = "standard" },
            Regexes = new SourceRegexSpec()
        },
    };

    /// <summary>获取全部源配置（持久化覆盖默认）</summary>
    public static List<MusicSourceConfig> GetAll()
    {
        try
        {
            var json = Storage.GetString(StoreKey, "");
            if (!string.IsNullOrWhiteSpace(json))
            {
                var saved = JsonSerializer.Deserialize<List<MusicSourceConfig>>(json);
                if (saved != null && saved.Count > 0)
                    return saved;
            }
        }
        catch { }
        return DefaultSources();
    }

    /// <summary>按 ID 获取源（未找到返回 null）</summary>
    public static MusicSourceConfig? Get(string id)
        => GetAll().FirstOrDefault(s => s.Id == id && s.Enabled);

    /// <summary>保存全部源配置（保留未提及的内置源）</summary>
    public static void SaveAll(List<MusicSourceConfig> sources)
        => Storage.SetString(StoreKey, JsonSerializer.Serialize(sources));

    /// <summary>更新单个源（保留其他源，自动补默认）</summary>
    public static void Upsert(MusicSourceConfig source)
    {
        var all = GetAll();
        var idx = all.FindIndex(s => s.Id == source.Id);
        if (idx >= 0) all[idx] = source;
        else all.Add(source);
        SaveAll(all);
    }

    /// <summary>按模板执行搜索</summary>
    public static async Task<List<SourceSong>> SearchAsync(MusicSourceConfig cfg, string keyword)
    {
        if (cfg.Template == "kuwo_jsonp") return await KuwoJsonp.SearchAsync(cfg, keyword);
        if (cfg.Template == "netease_eapi") return await NeteaseEapi.SearchAsync(cfg, keyword);
        return new List<SourceSong>();
    }

    /// <summary>按模板执行取链（quality: flac/320k/128k）</summary>
    public static async Task<string?> GetUrlAsync(MusicSourceConfig cfg, SourceSong song, string quality)
    {
        if (cfg.Template == "kuwo_jsonp") return await KuwoJsonp.GetUrlAsync(cfg, song, quality);
        if (cfg.Template == "netease_eapi") return await NeteaseEapi.GetUrlAsync(cfg, song, quality);
        return null;
    }

    /// <summary>通用 GET（占位符替换 + 编码解码），供模板与验证共用</summary>
    internal static async Task<string?> HttpGetAsync(SourceHttpSpec spec, Dictionary<string, string> placeholders)
    {
        try
        {
            var url = Fill(spec.Url, placeholders);
            var query = spec.Params.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(Fill(kv.Value, placeholders))}");
            if (query.Any()) url += (url.Contains('?') ? "&" : "?") + string.Join("&", query);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var h in spec.Headers)
            {
                if (h.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase)) request.Headers.Referrer = new Uri(h.Value);
                else if (h.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) request.Headers.UserAgent.ParseAdd(h.Value);
                else request.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return spec.Encoding == "gbk" ? Encoding.GetEncoding("GBK").GetString(bytes) : Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    internal static string Fill(string template, Dictionary<string, string> values)
    {
        foreach (var kv in values)
            template = template.Replace("{" + kv.Key + "}", kv.Value);
        return template;
    }

    /// <summary>按块正则解析搜索结果（jsonp 单引号格式等）</summary>
    internal static List<SourceSong> ParseBlocks(MusicSourceConfig cfg, string text)
    {
        var songs = new List<SourceSong>();
        var r = cfg.Regexes;
        if (string.IsNullOrEmpty(r.BlockSplit) || string.IsNullOrEmpty(r.IdPattern)) return songs;
        foreach (var block in Regex.Split(text, r.BlockSplit))
        {
            if (!string.IsNullOrEmpty(r.BlockMarker) && !block.Contains(r.BlockMarker)) continue;
            var id = Regex.Match(block, r.IdPattern);
            if (!id.Success) continue;
            songs.Add(new SourceSong
            {
                Id = id.Groups[1].Value,
                Name = System.Net.WebUtility.HtmlDecode(!string.IsNullOrEmpty(r.NamePattern) ? Regex.Match(block, r.NamePattern).Groups[1].Value : ""),
                Artist = System.Net.WebUtility.HtmlDecode(!string.IsNullOrEmpty(r.ArtistPattern) ? Regex.Match(block, r.ArtistPattern).Groups[1].Value : ""),
                Formats = !string.IsNullOrEmpty(r.FormatsPattern) ? Regex.Match(block, r.FormatsPattern).Groups[1].Value : ""
            });
        }
        return songs;
    }

    /// <summary>酷我模板：r.s 搜索（单引号 JSON 块）+ mobi.s convert_url_with_sign（surl 字段）</summary>
    private static class KuwoJsonp
    {
        public static async Task<List<SourceSong>> SearchAsync(MusicSourceConfig cfg, string keyword)
        {
            // 必须 await，勿改回 GetAwaiter().GetResult()：同步阻塞网络在主线程会抛
            // Android NetworkOnMainThreadException（StrictMode 拦截 UI 线程网络操作）
            var text = await HttpGetAsync(cfg.Search, new() { ["keyword"] = keyword });
            if (string.IsNullOrEmpty(text)) return new List<SourceSong>();
            return ParseBlocks(cfg, text);
        }

        public static async Task<string?> GetUrlAsync(MusicSourceConfig cfg, SourceSong song, string quality)
        {
            var br = cfg.QualityMap.TryGetValue(quality, out var v) ? v : "128kmp3";
            var rid = song.Id.Replace("MUSIC_", "");
            var body = await HttpGetAsync(cfg.UrlApi, new() { ["id"] = rid, ["quality"] = br });
            if (string.IsNullOrEmpty(body)) return null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("surl", out var surl))
                {
                    var s = surl.GetString();
                    if (!string.IsNullOrWhiteSpace(s) && s.StartsWith("http")) return s;
                }
            }
            catch { }
            var m = Regex.Match(body, @"url=([^&\s]+)");
            return m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : null;
        }
    }

    /// <summary>网易云模板：搜索 JSON + eapi v1 加密取链（AES-128-ECB，硬编码密钥来自配置）</summary>
    private static class NeteaseEapi
    {
        private static readonly byte[] Salt = Encoding.ASCII.GetBytes("36cd479b6b5");

        public static async Task<List<SourceSong>> SearchAsync(MusicSourceConfig cfg, string keyword)
        {
            var songs = new List<SourceSong>();
            var text = await HttpGetAsync(cfg.Search, new() { ["keyword"] = keyword });
            if (string.IsNullOrEmpty(text)) return songs;
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("result", out var result)
                    && result.TryGetProperty("songs", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in arr.EnumerateArray())
                    {
                        var id = s.TryGetProperty("id", out var idProp) ? idProp.GetInt64().ToString() : "";
                        var name = s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var artists = "";
                        if (s.TryGetProperty("artists", out var ats) && ats.ValueKind == JsonValueKind.Array)
                            artists = string.Join(", ", ats.EnumerateArray().Select(a => a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : ""));
                        if (!string.IsNullOrEmpty(id))
                            songs.Add(new SourceSong { Id = id, Name = name, Artist = artists });
                    }
                }
            }
            catch { }
            return songs;
        }

        public static async Task<string?> GetUrlAsync(MusicSourceConfig cfg, SourceSong song, string quality)
        {
            var level = cfg.QualityMap.TryGetValue(quality, out var v) ? v : "standard";
            var eapiPath = cfg.UrlApi.Params.TryGetValue("eapi_path", out var p) ? p : "/api/song/enhance/player/url/v1";
            var key = cfg.UrlApi.Params.TryGetValue("eapi_key", out var k) ? k : "e82ckenh8dichen8";
            var encodeType = cfg.UrlApi.Params.TryGetValue("encode_type", out var e) ? e : "flac";
            var immerse = cfg.UrlApi.Params.TryGetValue("immerse_type", out var im) ? im : "c51";
            var deviceId = cfg.UrlApi.Params.TryGetValue("device_id", out var d) ? d : "pyncm!";

            var payload = $"{{\"ids\":[{song.Id}],\"level\":\"{level}\",\"encodeType\":\"{encodeType}\",\"immerseType\":\"{immerse}\"}}";
            var digest = Md5Hex($"nobody{eapiPath}use{payload}md5forencrypt");
            var data = $"{eapiPath}-{Encoding.ASCII.GetString(Salt)}-{payload}-{Encoding.ASCII.GetString(Salt)}-{digest}";
            var paramsHex = Aes128EcbHex(data, Encoding.ASCII.GetBytes(key));

            try
            {
                var body = await HttpPostFormAsync(cfg.UrlApi.Url, new() { ["params"] = paramsHex }, deviceId);
                if (string.IsNullOrEmpty(body)) return null;
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d2 in arr.EnumerateArray())
                    {
                        if (d2.TryGetProperty("url", out var u) && u.GetString() is { Length: > 10 } url)
                            return url;
                    }
                }
            }
            catch { }
            return null;
        }

        private static async Task<string?> HttpPostFormAsync(string url, Dictionary<string, string> form, string deviceId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(form)
            };
            request.Headers.TryAddWithoutValidation("Cookie", $"os=pc; appver=; osver=; deviceId={deviceId}");
            request.Headers.Referrer = new Uri("https://music.163.com/");
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync();
        }

        private static string Md5Hex(string input)
        {
            var hash = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string Aes128EcbHex(string input, byte[] key)
        {
            var plain = Encoding.UTF8.GetBytes(input);
            var pad = 16 - plain.Length % 16;
            var padded = new byte[plain.Length + pad];
            Array.Copy(plain, padded, plain.Length);
            Array.Fill(padded, (byte)pad, plain.Length, pad);
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Key = key;
            aes.Mode = System.Security.Cryptography.CipherMode.ECB;
            aes.Padding = System.Security.Cryptography.PaddingMode.None;
            using var enc = aes.CreateEncryptor();
            return Convert.ToHexString(enc.TransformFinalBlock(padded, 0, padded.Length));
        }
    }
}
