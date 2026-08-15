using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// 搜索音乐库工具，按关键词（歌名、艺术家、专辑）检索本地与远程合并后的歌曲列表。
/// </summary>

public class WebSearchTool : IAgentTool
{
    // 静态编译正则：避免每次解析/清理都走 Regex.Match 非编译路径（含 RegexOptions.Compiled 的重复创建开销）
    private static readonly System.Text.RegularExpressions.Regex RegexTag = new(@"<[^>]+>", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex RegexWhitespace = new(@"\s+", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex RegexBingBlock = new(@"<li class=""b_algo[\s\S]*?</li>", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex RegexBingAnchor = new(@"<h2[^>]*>\s*<a[^>]*href=""([^""]+)""[^>]*>([\s\S]*?)</a>", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex RegexBingSnippet = new(@"<p[^>]*>([\s\S]*?)</p>", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex RegexBaiduBlock = new(@"<h3[^>]*>[\s\S]*?<a[^>]*href=""([^""]+)""[^>]*>([\s\S]*?)</a>[\s\S]*?</h3>", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex RegexDdgBlock = new(@"<a[^>]*class=""result__a""[^>]*href=""([^""]+)""[^>]*>([\s\S]*?)</a>", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex RegexDdgSnippet = new(@"<a[^>]*class=""result__snippet""[^>]*>([\s\S]*?)</a>", System.Text.RegularExpressions.RegexOptions.Compiled);
    /// <summary>搜索页 HTML 最大处理长度（2MB），防止畸形页面拉长正则回溯时间</summary>
    private const int MaxHtmlLength = 2 * 1024 * 1024;

    /// <summary>HTTP 客户端，用于发起搜索请求</summary>
    private readonly HttpClient _httpClient;
    /// <summary>工具名称</summary>
    public string Name => "web_search";
    /// <summary>工具描述</summary>
    public string Description => "在互联网上搜索信息，返回标题、链接与摘要，可以搜索新闻、知识、音乐资讯等内容。当用户询问实时信息、最新资讯或你不确定的知识时使用此工具。找到需要深入了解的页面时，请继续用 fetch_web_page 工具抓取正文。";
    public bool IsReadOnly => true;

    /// <summary>
    /// 构造 WebSearchTool 实例，初始化 HttpClient 并设置完整的浏览器请求头。
    /// </summary>
    public WebSearchTool()
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        });
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
    }

    /// <summary>
    /// 返回该工具的 OpenAI 兼容函数定义
    /// </summary>
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
                    ["query"] = new() { Type = "string", Description = "搜索关键词" }
                },
                Required = new List<string> { "query" }
            }
        }
    };

    /// <summary>
    /// 执行联网搜索操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，包含 query 字段</param>
    /// <returns>JSON 序列化结果，包含 success、query、results、message 字段</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var query = ArgHelper.ExtractStringArgFallback(arguments, "query");

        if (string.IsNullOrWhiteSpace(query))
            return JsonSerializer.Serialize(new { error = "请提供搜索关键词" });

        try
        {
            // 首选：Exa 托管 AI 搜索（opencode 同款方案，免 key）——
            // 返回 LLM-ready 结果文本，livecrawl 自动抓取页面内容（如天气数据表），
            // 通常 1-3s 返回，质量远高于自抓搜索引擎 HTML。
            var exaText = await SearchExaAsync(query);
            if (!string.IsNullOrWhiteSpace(exaText) && exaText.Length > 50)
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    query = query,
                    content = exaText,
                    message = "搜索完成（Exa AI 搜索）"
                });
            }

            // 回退：自抓搜索引擎（cn.bing 主源，结果充足即返回，不等慢源）
            using var ctsBing = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var ctsBaidu = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var ctsDdg = new CancellationTokenSource(TimeSpan.FromSeconds(6));

            var bingTask = SearchBingAsync(query, ctsBing.Token);
            var baiduTask = SearchBaiduAsync(query, ctsBaidu.Token);
            var ddgTask = SearchDuckDuckGoAsync(query, ctsDdg.Token);

            var results = new List<object>();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddUnique(IEnumerable<object> items)
            {
                foreach (var r in items)
                {
                    var url = GetResultUrl(r);
                    if (string.IsNullOrEmpty(url) || !seenUrls.Add(url)) continue;
                    results.Add(r);
                    if (results.Count >= 8) return;
                }
            }

            // 主源 Bing：等它（通常 <1s），充足即返回
            AddUnique(await bingTask);
            if (results.Count >= 3)
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    query = query,
                    results = results,
                    message = $"搜索完成，找到 {results.Count} 条相关结果"
                });
            }

            // 不足：补充百度（已并行执行，通常即到），再不足补 DDG
            AddUnique(await baiduTask);
            if (results.Count < 3)
                AddUnique(await ddgTask);

            if (results.Count > 0)
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    query = query,
                    results = results,
                    message = $"搜索完成，找到 {results.Count} 条相关结果"
                });
            }

            return JsonSerializer.Serialize(new
            {
                success = false,
                query = query,
                results = Array.Empty<object>(),
                message = $"已搜索「{query}」但未找到相关结果，建议换个关键词试试"
            });
        }
        catch (Exception ex)
        {
            // 明确说明是搜索源不可达而非设备断网，避免误导"无网络"
            return JsonSerializer.Serialize(new { error = $"联网搜索暂不可用（所有搜索源都无法访问，请稍后重试）：{ex.Message}" });
        }
    }

    /// <summary>提取匿名结果对象的 URL（匿名类型动态读取）</summary>
    private static string? GetResultUrl(object result)
    {
        try
        {
            var t = result.GetType();
            var p = t.GetProperty("url");
            return p?.GetValue(result)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 通过 DuckDuckGo HTML 版搜索（海外补充源；对中文结果有限，但对英文/技术查询有效）
    /// </summary>
    private async Task<List<object>> SearchDuckDuckGoAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var html = await ReadHtmlLimitedAsync(response, MaxPageBytes);
            return ParseDuckDuckGoResults(html);
        }
        catch (Exception ex)
        {
            Log.Debug("AgentTools", $"[WebSearch] DuckDuckGo 搜索失败: {ex.Message}");
            return new List<object>();
        }
    }

    /// <summary>解析 DuckDuckGo 结果（result__a 标题链接 + result__snippet 摘要）</summary>
    private List<object> ParseDuckDuckGoResults(string html)
    {
        var results = new List<object>();
        try
        {
            if (html.Length > MaxHtmlLength) html = html[..MaxHtmlLength];
            foreach (System.Text.RegularExpressions.Match m in RegexDdgBlock.Matches(html))
            {
                var url = m.Groups[1].Value;
                var title = CleanHtmlText(m.Groups[2].Value);
                if (string.IsNullOrEmpty(title)) continue;
                // DDG 链接为跳转链接（//duckduckgo.com/l/?uddg=...），解码出真实 URL
                if (url.Contains("/l/?uddg=", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var uddg = System.Net.WebUtility.UrlDecode(url[(url.IndexOf("uddg=", StringComparison.OrdinalIgnoreCase) + 5)..]);
                        url = uddg.Split('&')[0];
                    }
                    catch { }
                }
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                var snippet = "";
                var sMatch = RegexDdgSnippet.Match(html, m.Index, Math.Min(3000, html.Length - m.Index));
                if (sMatch.Success)
                    snippet = CleanHtmlText(sMatch.Groups[1].Value);
                results.Add(new { title, url, snippet });
                if (results.Count >= 8) break;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("AgentTools", $"[WebSearch] 解析 DuckDuckGo 结果失败: {ex.Message}");
        }
        return results;
    }

    /// <summary>清理 HTML 文本：去除标签、解码实体、压缩空白</summary>
    private static string CleanHtmlText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        // 去除所有 HTML 标签
        var text = RegexTag.Replace(html, "");
        // 解码常见 HTML 实体
        text = System.Net.WebUtility.HtmlDecode(text);
        // 压缩空白
        text = RegexWhitespace.Replace(text, " ").Trim();
        return text;
    }

    /// <summary>搜索结果页最多读取的字节数：搜索结果都在页面头部，截断大幅降低下载/解析耗时
    /// （百度结果页实测 1.19MB，全量下载+正则解析明显拖慢搜索）</summary>
    private const int MaxPageBytes = 400 * 1024;

    /// <summary>
    /// 调用 Exa 托管 AI 搜索（opencode 同款：MCP JSON-RPC tools/call，免 key）。
    /// livecrawl=fallback：缓存内容缺失时自动抓取页面 → 直接返回含正文的结果文本。
    /// </summary>
    private async Task<string?> SearchExaAsync(string query)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new
                {
                    name = "web_search_exa",
                    arguments = new
                    {
                        query = query,
                        type = "auto",
                        numResults = 8,
                        livecrawl = "fallback",
                        contextMaxCharacters = 10000
                    }
                }
            });
            var request = new HttpRequestMessage(HttpMethod.Post, "https://mcp.exa.ai/mcp");
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            return ParseMcpText(body);
        }
        catch (Exception ex)
        {
            Log.Debug("AgentTools", $"[WebSearch] Exa 搜索失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>解析 MCP JSON-RPC 响应（SSE 或纯 JSON）：取 result.content[].text</summary>
    private static string? ParseMcpText(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            if (!line.StartsWith("data: ")) continue;
            var text = TryExtractMcpText(line["data: ".Length..].Trim());
            if (text != null) return text;
        }
        // 无 SSE 前缀时整段可能是 JSON
        return TryExtractMcpText(body.Trim());
    }

    private static string? TryExtractMcpText(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.StartsWith("{")) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var result)
                && result.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in content.EnumerateArray())
                {
                    if (c.TryGetProperty("text", out var t))
                    {
                        var text = t.GetString();
                        if (!string.IsNullOrWhiteSpace(text)) return text;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>限流读取响应内容（最多 maxBytes 字节），并按 UTF-8/GBK 探测解码。</summary>
    private static async Task<string> ReadHtmlLimitedAsync(HttpResponseMessage response, int maxBytes)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int total = 0;
        while (total < maxBytes)
        {
            var read = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, maxBytes - total));
            if (read == 0) break;
            ms.Write(buffer, 0, read);
            total += read;
        }
        var bytes = ms.ToArray();
        // UTF-8 严格校验：有效用 UTF-8，否则回退 GBK（中文站点常见）
        try
        {
            return new System.Text.UTF8Encoding(false, true).GetString(bytes);
        }
        catch (System.Text.DecoderFallbackException)
        {
            try { return System.Text.Encoding.GetEncoding("GBK").GetString(bytes); }
            catch { return System.Text.Encoding.UTF8.GetString(bytes); }
        }
    }

    /// <summary>
    /// 通过必应中国版（cn.bing.com）搜索——国内可直连，结果结构稳定。
    /// ⚠ 勿用 www.bing.com：国内常被重定向到国际版，中文查询返回无关结果。
    /// </summary>
    private async Task<List<object>> SearchBingAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://cn.bing.com/search?q={Uri.EscapeDataString(query)}&mkt=zh-CN&setlang=zh-hans&count=10";
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var html = await ReadHtmlLimitedAsync(response, MaxPageBytes);
            return ParseBingResults(html);
        }
        catch (Exception ex)
        {
            Log.Debug("AgentTools", $"[WebSearch] Bing 搜索失败: {ex.Message}");
            return new List<object>();
        }
    }

    /// <summary>
    /// 解析 Bing 搜索结果页（li class="b_algo" 块，h2>a 为标题链接，p 为摘要）
    /// </summary>
    private List<object> ParseBingResults(string html)
    {
        var results = new List<object>();
        try
        {
            if (html.Length > MaxHtmlLength) html = html[..MaxHtmlLength];
            foreach (System.Text.RegularExpressions.Match block in RegexBingBlock.Matches(html))
            {
                var seg = block.Value;
                var aMatch = RegexBingAnchor.Match(seg);
                if (!aMatch.Success) continue;
                var url = aMatch.Groups[1].Value;
                var title = CleanHtmlText(aMatch.Groups[2].Value);
                if (string.IsNullOrEmpty(title)) continue;
                var pMatch = RegexBingSnippet.Match(seg);
                var snippet = pMatch.Success ? CleanHtmlText(pMatch.Groups[1].Value) : "";
                results.Add(new { title, url, snippet });
                if (results.Count >= 5) break;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("AgentTools", $"[WebSearch] 解析 Bing 结果失败: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// 通过百度搜索（国内可直连；摘要结构多变，仅提取标题与链接）
    /// </summary>
    private async Task<List<object>> SearchBaiduAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://www.baidu.com/s?wd={Uri.EscapeDataString(query)}&rn=10";
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var html = await ReadHtmlLimitedAsync(response, MaxPageBytes);
            return ParseBaiduResults(html);
        }
        catch (Exception ex)
        {
            Log.Debug("AgentTools", $"[WebSearch] 百度搜索失败: {ex.Message}");
            return new List<object>();
        }
    }

    /// <summary>
    /// 解析百度搜索结果页（h3 内 a 为标题链接；链接为百度跳转链接，可直接点击）
    /// </summary>
    private List<object> ParseBaiduResults(string html)
    {
        var results = new List<object>();
        try
        {
            if (html.Length > MaxHtmlLength) html = html[..MaxHtmlLength];
            foreach (System.Text.RegularExpressions.Match m in RegexBaiduBlock.Matches(html))
            {
                var url = m.Groups[1].Value;
                var title = CleanHtmlText(m.Groups[2].Value);
                if (string.IsNullOrEmpty(title)) continue;
                // 百度可能返回相对路径（如 /sf/vsearch?...），补全为绝对地址
                if (url.StartsWith("/")) url = "https://www.baidu.com" + url;
                results.Add(new { title, url, snippet = "" });
                if (results.Count >= 5) break;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("AgentTools", $"[WebSearch] 解析百度结果失败: {ex.Message}");
        }
        return results;
    }
}
