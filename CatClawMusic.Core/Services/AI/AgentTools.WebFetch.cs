using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// 网页抓取工具：下载指定 URL 的页面并提取可读正文（HTML → 纯文本/markdown 风格）。
/// 与 web_search 配合使用：搜索找到 URL → 本工具抓取正文 → 供模型综合回答。
/// 复刻宿主端 Agent 的"直接抓取页面"工作方式：搜索引擎只负责发现 URL，
/// 深入阅读靠抓取页面本身，而不是停留在搜索结果摘要。
/// </summary>
public class FetchWebPageTool : IAgentTool
{
    private static readonly Regex RegexScriptStyle = new(@"<(script|style|noscript|svg|iframe)[\s\S]*?</\1>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RegexTag = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex RegexWhitespace = new(@"[ \t]+", RegexOptions.Compiled);
    private static readonly Regex RegexBlankLines = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex RegexBlock = new(@"(</?(?:p|h[1-6]|li|tr|div|br|section|article|blockquote|pre)[^>]*>)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RegexHref = new(@"<a[^>]*href=""([^""]+)""[^>]*>([\s\S]*?)</a>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>抓取正文最大长度（字符），超出截断</summary>
    private const int MaxTextLength = 8000;
    /// <summary>返回链接数量上限（附在正文后供继续抓取）</summary>
    private const int MaxLinks = 12;
    /// <summary>页面最大下载字节数（限流读取，避免大页面全量下载拖慢响应）</summary>
    private const int MaxHtmlBytes = 600_000;

    private readonly HttpClient _httpClient;

    public string Name => "fetch_web_page";
    public string Description => "抓取指定网页的正文内容（输入 URL），返回页面标题与可读文本。当搜索结果只给了链接和摘要、需要了解具体内容时使用此工具深入阅读页面。";

    public FetchWebPageTool()
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        });
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
    }

    public ToolDefinition GetDefinition() => new()
    {
        Function = new ToolFunctionDef
        {
            Name = Name,
            Description = Description,
            Parameters = new ToolParameterDef
            {
                Properties = new Dictionary<string, ToolParameterProperty>
                {
                    ["url"] = new() { Type = "string", Description = "要抓取的网页完整 URL（http/https）" }
                },
                Required = new List<string> { "url" }
            }
        }
    };

    public async Task<string> ExecuteAsync(string arguments)
    {
        var url = ArgHelper.ExtractStringArgFallback(arguments, "url")?.Trim();
        if (string.IsNullOrWhiteSpace(url))
            return JsonSerializer.Serialize(new { error = "请提供要抓取的 URL" });
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { error = "URL 必须以 http:// 或 https:// 开头" });

        try
        {
            // 用 Task.Run 包裹避免调用方同步上下文影响（工具在 Agent 线程池中执行，实际无碍）
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { error = $"抓取失败: HTTP {(int)response.StatusCode}" });

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase)
                && !contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase)
                && contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                // JSON/其他格式：原样截断返回
                var raw = await response.Content.ReadAsStringAsync();
                var truncated = raw.Length > 6000 ? raw[..6000] : raw;
                return JsonSerializer.Serialize(new { success = true, url = url, title = "", content = truncated });
            }

            using var ms = new MemoryStream();
            // 限流读取：正文抓取最多 600KB（提取后仅需 8KB 文本），避免大页面全量下载拖慢响应
            using (var stream = await response.Content.ReadAsStreamAsync())
            {
                var buffer = new byte[8192];
                int total = 0;
                while (total < MaxHtmlBytes)
                {
                    var read = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, MaxHtmlBytes - total));
                    if (read == 0) break;
                    ms.Write(buffer, 0, read);
                    total += read;
                }
            }
            var bytes = ms.ToArray();

            // 编码探测：BOM / charset 头 / 常见编码
            var html = DecodeHtml(bytes, response);

            var (title, text, links) = ExtractContent(html, url);

            if (string.IsNullOrWhiteSpace(text))
                return JsonSerializer.Serialize(new { error = "页面没有可提取的正文内容（可能是 JS 渲染页面）" });

            return JsonSerializer.Serialize(new
            {
                success = true,
                url = url,
                title = title,
                content = text,
                links = links
            });
        }
        catch (Exception ex)
        {
            Log.Debug("AgentTools", $"[WebFetch] 抓取失败: {ex.Message}");
            return JsonSerializer.Serialize(new { error = $"抓取失败: {ex.Message}" });
        }
    }

    /// <summary>编码探测解码：UTF-8 BOM / charset 头 / UTF-8 严格校验 / GBK 回退</summary>
    private static string DecodeHtml(byte[] bytes, HttpResponseMessage response)
    {
        try
        {
            var charset = response.Content.Headers.ContentType?.CharSet?.ToLowerInvariant();
            if (charset?.Contains("gb", StringComparison.OrdinalIgnoreCase) == true)
                return Encoding.GetEncoding("GBK").GetString(bytes);

            if (charset?.Contains("utf-8", StringComparison.OrdinalIgnoreCase) == true || bytes.StartsWithUtf8Bom())
                return Encoding.UTF8.GetString(bytes);

            // 严格 UTF-8 校验：有效则用 UTF-8，否则按 GBK（中文站点常见）
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                try { return Encoding.GetEncoding("GBK").GetString(bytes); }
                catch { return Encoding.UTF8.GetString(bytes); }
            }
        }
        catch
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    /// <summary>提取页面标题、正文纯文本与链接列表（复刻宿主端 HTML→可读文本的转换思路）</summary>
    private static (string title, string text, List<string> links) ExtractContent(string html, string baseUrl)
    {
        // 标题
        var title = "";
        var titleMatch = Regex.Match(html, @"<title[^>]*>([\s\S]*?)</title>", RegexOptions.IgnoreCase);
        if (titleMatch.Success)
            title = WebUtility.HtmlDecode(RegexTag.Replace(titleMatch.Groups[1].Value, "")).Trim();

        // 移除脚本/样式等非内容块
        var body = RegexScriptStyle.Replace(html, " ");

        // 提取链接（正文范围内）
        var links = new List<string>();
        foreach (Match m in RegexHref.Matches(body))
        {
            var href = m.Groups[1].Value.Trim();
            var linkText = WebUtility.HtmlDecode(RegexTag.Replace(m.Groups[2].Value, "")).Trim();
            if (string.IsNullOrEmpty(href) || string.IsNullOrEmpty(linkText)) continue;
            if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("#")) continue;
            // 相对路径补全
            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                try { href = new Uri(new Uri(baseUrl), href).ToString(); }
                catch { continue; }
            }
            if (links.Count >= MaxLinks) break;
            links.Add($"{linkText} → {href}");
        }

        // 块级标签 → 换行
        var marked = RegexBlock.Replace(body, m => "\n" + m.Value + "\n");
        // 去标签 + 解码实体
        var text = WebUtility.HtmlDecode(RegexTag.Replace(marked, " "));
        // 压缩空白
        text = RegexWhitespace.Replace(text, " ");
        // 清理空行
        text = RegexBlankLines.Replace(text, "\n\n");
        text = text.Trim();

        if (text.Length > MaxTextLength)
            text = text[..MaxTextLength] + "\n...[内容过长已截断]";

        return (title, text, links);
    }
}

internal static class WebFetchExtensions
{
    public static bool StartsWithUtf8Bom(this byte[] bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
}
