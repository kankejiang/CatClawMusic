using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CatClawMusic.Data;

/// <summary>
/// 百度百科艺术家元数据刮削器。
/// 百度百科对中文艺术家的信息最全：本名、昵称、民族、国籍、出生地、生日、星座、身高、经纪公司、代表作品、职业等。
/// </summary>
public class BaiduBaikeScraper : IArtistMetadataScraper
{
    private readonly string _cacheDir;
    private readonly HttpClient _http;

    /// <summary>来源名称</summary>
    public string SourceName => "百度百科";

    public BaiduBaikeScraper(string cacheDir)
    {
        _cacheDir = cacheDir;
        _http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(3),
        });
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36");
    }

    /// <summary>搜索艺术家</summary>
    public async Task<List<ArtistSearchResult>> SearchArtistsAsync(string name, int limit = 10)
    {
        var results = new List<ArtistSearchResult>();
        try
        {
            // 先通过搜索页获取条目链接
            var entries = await SearchBaiduBaikeAsync(name);
            if (entries.Count == 0) return results;

            // 取前几个结果，逐个抓取详情
            foreach (var entry in entries.Take(limit))
            {
                var detail = await FetchArtistDetailAsync(entry.Url, name);
                if (detail != null)
                    results.Add(detail);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BaiduBaike] 搜索失败: {ex.Message}");
        }
        return results;
    }

    /// <summary>下载并缓存封面</summary>
    public async Task<string?> DownloadAndCacheArtistCoverAsync(string coverUrl, string artistName)
    {
        if (string.IsNullOrEmpty(coverUrl)) return null;
        try
        {
            Directory.CreateDirectory(_cacheDir);
            var ext = coverUrl.Contains(".jpg", StringComparison.OrdinalIgnoreCase) ? ".jpg"
                 : coverUrl.Contains(".png", StringComparison.OrdinalIgnoreCase) ? ".png"
                 : coverUrl.Contains(".webp", StringComparison.OrdinalIgnoreCase) ? ".webp"
                 : ".jpg";
            var safeName = Regex.Replace(artistName, @"[/\\:*?""<>|]", "_");
            var localPath = Path.Combine(_cacheDir, $"baidubaike_{safeName}{ext}");

            if (File.Exists(localPath)) return localPath;

            using var resp = await _http.GetAsync(coverUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return null;

            await using var fs = File.Create(localPath);
            await resp.Content.CopyToAsync(fs);
            return localPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BaiduBaike] DownloadAndCacheArtistCoverAsync 错误: {ex.Message}");
            return null;
        }
    }

    // ==================== 内部方法 ====================

    /// <summary>搜索百度百科，返回匹配的条目列表</summary>
    private async Task<List<BaikeEntry>> SearchBaiduBaikeAsync(string name)
    {
        var entries = new List<BaikeEntry>();
        try
        {
            // 使用百度百科搜索接口
            var url = $"https://baike.baidu.com/search?word={Uri.EscapeDataString(name)}&enc=utf8&rn=5";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return entries;

            var html = await response.Content.ReadAsStringAsync();

            // 从搜索结果中提取词条链接
            var linkPattern = new Regex(
                @"href=""(/item/[^""]+)""[^>]*>([^<]+)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in linkPattern.Matches(html))
            {
                var href = match.Groups[1].Value.Trim();
                var title = WebUtility.HtmlDecode(match.Groups[2].Value.Trim());

                // 过滤掉非相关链接（如"帮助"、"分类"等）
                if (!href.StartsWith("/item/", StringComparison.OrdinalIgnoreCase)) continue;
                if (title.Length < 2 || title.Length > 50) continue;
                // 排除一些非艺人页面
                if (title is "帮助" or "分类" or "首页" or "百度百科") continue;
                if (seenUrls.Contains(href)) continue;

                seenUrls.Add(href);
                entries.Add(new BaikeEntry(title, $"https://baike.baidu.com{href}"));

                if (entries.Count >= 5) break;
            }

            // 如果搜索没找到结果，尝试直接访问词条页
            if (entries.Count == 0)
            {
                entries.Add(new BaikeEntry(name, $"https://baike.baidu.com/item/{Uri.EscapeDataString(name)}"));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BaiduBaike] 搜索失败: {ex.Message}");
            // fallback：直接访问词条页
            entries.Clear();
            entries.Add(new BaikeEntry(name, $"https://baike.baidu.com/item/{Uri.EscapeDataString(name)}"));
        }
        return entries;
    }

    /// <summary>从百度百科详情页抓取艺术家信息</summary>
    private async Task<ArtistSearchResult?> FetchArtistDetailAsync(string url, string searchName)
    {
        try
        {
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var html = await response.Content.ReadAsStringAsync();

            // 解析基本信息框（infobox）
            var result = ParseInfobox(html, searchName);
            result.Source = SourceName;
            result.Id = $"baidubaike_{searchName.GetHashCode():x}";

            // 如果没有解析到名称，使用搜索名
            if (string.IsNullOrEmpty(result.Name))
                result.Name = searchName;

            // 尝试从页面中提取简介（正文第一段）
            if (string.IsNullOrEmpty(result.Description))
                result.Description = ExtractDescription(html);

            // 尝试从 infobox 中提取图片
            if (string.IsNullOrEmpty(result.CoverUrl))
                result.CoverUrl = ExtractCoverImage(html);

            // 清理字段中的 HTML 标签和多余空白
            CleanFields(result);

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BaiduBaike] 详情获取失败: {url}, {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 解析百度百科基本信息框（infobox）。
    /// 百度百科 infobox 结构：
    /// &lt;div class="basic-info"&gt;
    ///   &lt;dl class="basic-info-item"&gt;&lt;dt&gt;标签&lt;/dt&gt;&lt;dd&gt;值&lt;/dd&gt;&lt;/dl&gt;
    /// &lt;/div&gt;
    /// </summary>
    private ArtistSearchResult ParseInfobox(string html, string searchName)
    {
        var result = new ArtistSearchResult();
        result.Name = searchName;
        var extraInfo = "";  // 用局部变量代替 ref 属性

        // 提取 basic-info 块
        var infoBlockMatch = Regex.Match(html,
            @"<div[^>]*class=""[^""]*basic-info[^""]*""[^>]*>(.*?)</div\s*>\s*(?:</div>\s*)*$",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        if (!infoBlockMatch.Success)
        {
            infoBlockMatch = Regex.Match(html,
                @"class=""basic-info[^""]*""[^>]*>(.*?)<script",
                RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        if (!infoBlockMatch.Success) { result.ExtraInfo = extraInfo; return result; }

        var infoBlock = infoBlockMatch.Groups[1].Value;

        // 提取所有 key-value 对
        var itemPattern = new Regex(
            @"<dt[^>]*>(?<label>[^<]+)</dt>\s*<dd[^>]*>(?<value>.*?)</dd>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        foreach (Match item in itemPattern.Matches(infoBlock))
        {
            var label = StripHtml(item.Groups["label"].Value).Trim().Trim('：', ':');
            var value = StripHtml(item.Groups["value"].Value).Trim();

            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value)) continue;

            // 标签 → 字段映射
            switch (label)
            {
                case "艺名":
                    if (string.IsNullOrEmpty(result.Name) || !searchName.Equals(value, StringComparison.OrdinalIgnoreCase))
                        result.Name = value;
                    break;
                case "本名":
                case "真实姓名":
                case "真名":
                    result.RealName = value;
                    break;
                case "昵称":
                case "别名":
                case "外号":
                case "别号":
                    result.Nickname = value;
                    break;
                case "性别":
                    result.Gender = value;
                    break;
                case "民族":
                case "种族":
                    result.Ethnicity = value;
                    break;
                case "国籍":
                case "国家":
                    result.Region = value;
                    break;
                case "出生地":
                case "出生":
                case "出生地点":
                case "籍贯":
                    result.BirthPlace = value;
                    break;
                case "出生日期":
                case "出生时间":
                case "生辰":
                case "日期 of birth":
                    result.Birthday = NormalizeBirthday(value);
                    break;
                case "毕业院校":
                case "毕业学校":
                case "学历":
                case "教育程度":
                    result.Education = value;
                    break;
                case "星座":
                    result.Zodiac = value;
                    break;
                case "身高":
                    result.Height = value;
                    break;
                case "经纪公司":
                case "经纪":
                case "唱片公司":
                    result.Agency = value;
                    break;
                case "代表作品":
                case "代表作":
                case "主要作品":
                case "作品":
                    result.RepresentativeWorks = value;
                    break;
                case "职业":
                case "职业身份":
                case "身份":
                case "occupation":
                    result.Occupation = value;
                    break;
                default:
                    // 其他信息存入 ExtraInfo
                    extraInfo = AppendExtra(extraInfo, label, value);
                    break;
            }
        }

        result.ExtraInfo = extraInfo;
        return result;
    }

    /// <summary>提取正文第一段作为简介</summary>
    private static string? ExtractDescription(string html)
    {
        var summaryPatterns = new[]
        {
            new Regex(@"<div[^>]*id=""lemma_summary""[^>]*>(.*?)</div>", RegexOptions.Singleline),
            new Regex(@"<div[^>]*class=""lemma-summary[^""]*""[^>]*>(.*?)</div>", RegexOptions.Singleline),
            new Regex(@"<meta[^>]*name=""description""[^>]*content=""([^""]+)""", RegexOptions.IgnoreCase),
        };

        foreach (var pattern in summaryPatterns)
        {
            var match = pattern.Match(html);
            if (match.Success)
            {
                var desc = StripHtml(match.Groups[1].Value).Trim();
                if (!string.IsNullOrEmpty(desc) && desc.Length > 5)
                    return desc.Length > 2000 ? desc[..2000] + "..." : desc;
            }
        }

        // fallback：取正文中第一个较长的段落
        var paraPattern = new Regex(@"<p[^>]*>(.*?)</p>", RegexOptions.Singleline);
        foreach (Match para in paraPattern.Matches(html))
        {
            var text = StripHtml(para.Groups[1].Value).Trim();
            if (text.Length > 30 && text.Length < 3000)
                return text;
        }

        return null;
    }

    /// <summary>从 infobox 中提取封面图片</summary>
    private static string? ExtractCoverImage(string html)
    {
        var patterns = new[]
        {
            new Regex(@"<img[^>]+src=""(https?://[^""]+(?:\.(?:jpg|jpeg|png|webp|gif))(?:""|[^>])*)""",
                RegexOptions.IgnoreCase),
            new Regex(@"<img[^>]+data-src=""(https?://[^""]+)""", RegexOptions.IgnoreCase),
            new Regex(@"<a[^>]*href=""(/pic/[^""]+)""", RegexOptions.IgnoreCase),
        };

        foreach (var pattern in patterns)
        {
            var match = pattern.Match(html);
            if (match.Success)
            {
                var url = match.Groups[1].Value;
                if (url.StartsWith("//"))
                    url = "https:" + url;
                else if (url.StartsWith("/"))
                    url = "https://baike.baidu.com" + url;

                if (url.Contains("icon") || url.Contains("logo") || url.Contains("emoji")) continue;

                return url;
            }
        }

        return null;
    }

    /// <summary>清理字段值</summary>
    private static void CleanFields(ArtistSearchResult r)
    {
        r.RealName = CleanField(r.RealName);
        r.Nickname = CleanField(r.Nickname);
        r.Gender = CleanField(r.Gender);
        r.Birthday = CleanField(r.Birthday);
        r.Region = CleanField(r.Region);
        r.Description = CleanField(r.Description);
        r.Ethnicity = CleanField(r.Ethnicity);
        r.BirthPlace = CleanField(r.BirthPlace);
        r.Education = CleanField(r.Education);
        r.Zodiac = CleanField(r.Zodiac);
        r.Height = CleanField(r.Height);
        r.Agency = CleanField(r.Agency);
        r.RepresentativeWorks = CleanField(r.RepresentativeWorks);
        r.Occupation = CleanField(r.Occupation);
    }

    private static string? CleanField(string? val)
    {
        if (string.IsNullOrEmpty(val)) return null;
        val = StripHtml(val);
        val = Regex.Replace(val, @"\s+", " ").Trim();
        val = Regex.Replace(val, @"\[\d+\]", "").Trim();
        return string.IsNullOrEmpty(val) ? null : val;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        return Regex.Replace(html, @"<[^>]+>", "");
    }

    private static string? NormalizeBirthday(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        raw = Regex.Replace(raw, @"\[\d+\]", "").Trim();
        if (raw.Length > 4 && raw.Length <= 30)
            return raw;
        return null;
    }

    private static string AppendExtra(string current, string label, string value)
    {
        var item = $"{label}: {value}";
        return string.IsNullOrEmpty(current) ? item : current + $" | {item}";
    }

    /// <summary>百度百科搜索结果条目</summary>
    private record BaikeEntry(string Title, string Url);
}
