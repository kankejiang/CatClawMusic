using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CatClawMusic.Maui.Services;

/// <summary>插件商店条目（来自商店清单，支持 v1 数组与 v2 字典两种格式）</summary>
public class PluginStoreItem
{
    /// <summary>插件类型 ID（与 PluginManager 的 PluginTypeId 对应，如 OnlineMusic.netEaseMusic）</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>插件名称</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>插件版本（如 1.1.0）</summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>插件作者</summary>
    public string Author { get; set; } = string.Empty;
    /// <summary>插件完整描述</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>短描述（卡片一句话，AstrBot short_desc 对齐；缺省回退 Description）</summary>
    public string ShortDescription { get; set; } = string.Empty;
    /// <summary>插件分类（OnlineMusic / Lyrics / ...）</summary>
    public string Category { get; set; } = string.Empty;
    /// <summary>图标 Emoji（缺省 🧩；有 LogoUrl 时优先显示图片）</summary>
    public string Icon { get; set; } = "🧩";
    /// <summary>图片 Logo 地址（256x256，可选）</summary>
    public string LogoUrl { get; set; } = string.Empty;
    /// <summary>标签（用于搜索与分类筛选）</summary>
    public List<string> Tags { get; set; } = new();
    /// <summary>兼容的应用最低版本（PEP440 子集，如 "&gt;=1.7.10"；空 = 不限）</summary>
    public string MinAppVersion { get; set; } = string.Empty;
    /// <summary>插件主页</summary>
    public string Homepage { get; set; } = string.Empty;
    /// <summary>更新时间（ISO 字符串）</summary>
    public string UpdatedAt { get; set; } = string.Empty;
    /// <summary>安装包下载地址（.dll / .ccp 直链或 GitHub Release asset）</summary>
    public string InstallUrl { get; set; } = string.Empty;
    /// <summary>安装包 sha256（十六进制，可选；提供则安装前校验）</summary>
    public string FileHash { get; set; } = string.Empty;
    /// <summary>来源市场名称（内置 / 自定义源地址），由服务端填充</summary>
    public string SourceName { get; set; } = "内置市场";

    /// <summary>由安装地址派生的稳定文件名（如 CatClawMusic.Plugins.OnlineMusic.dll），用于覆盖安装避免堆积</summary>
    public string PackageFileName =>
        string.IsNullOrWhiteSpace(InstallUrl) ? string.Empty : InstallUrl.TrimEnd('/').Split('/').Last();
}

/// <summary>
/// 插件商店服务 —— 从多个市场源拉取清单（GitHub 仓库 JSON），合并去重后提供安装。
/// 参考 AstrBot 插件市场：索引即 JSON 文件、仓库即分发渠道、多源订阅。
/// 安装由 PluginManager.InstallFromLocalFileAsync 完成（先下载 .dll 到本地）。
/// 国内网络对 raw.githubusercontent.com 不稳定，所有请求均带多源回退：
/// raw 直链 → jsDelivr CDN 镜像 → GitHub API（清单 base64 / dll 下载跳过 API）。
/// </summary>
public class PluginStoreService
{
    /// <summary>默认商店清单地址（CatClawMusic 仓库 plugins/plugins.json 的 raw 直链）</summary>
    public const string DefaultStoreUrl =
        "https://raw.githubusercontent.com/kankejiang/CatClawMusic/master/plugins/plugins.json";

    private const string RawPrefix = "https://raw.githubusercontent.com/";
    private static readonly string CustomSourcesPath =
        Path.Combine(FileSystem.AppDataDirectory, "plugin_sources.json");

    private readonly HttpClient _http;

    public PluginStoreService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Add("User-Agent", "CatClawMusic/1.0");
    }

    /// <summary>根据 raw 直链生成候选源列表：raw → jsDelivr CDN → gh-proxy.com 反代（国内友好）→ GitHub API（dll 下载跳过 API，因其返回 base64 文本）</summary>
    private static List<string> BuildCandidateUrls(string rawUrl, bool excludeApi)
    {
        var urls = new List<string> { rawUrl };
        if (rawUrl.StartsWith(RawPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var parts = rawUrl.Substring(RawPrefix.Length).Split('/');
            if (parts.Length >= 3)
            {
                var owner = parts[0];
                var repo = parts[1];
                var branch = parts[2];
                var path = string.Join("/", parts.Skip(3));
                urls.Add($"https://cdn.jsdelivr.net/gh/{owner}/{repo}@{branch}/{path}");
                // 国内友好反代镜像：把原始 URL 挂在 gh-proxy.com 后面作为路径
                urls.Add($"https://gh-proxy.com/{rawUrl}");
                if (!excludeApi)
                    urls.Add($"https://api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}");
            }
        }
        return urls;
    }

    // ═══════════════════════════════════════
    // 自定义市场源（持久化到文件，规避未打包 Windows Preferences 失效）
    // ═══════════════════════════════════════

    public List<string> GetCustomSources()
    {
        try
        {
            if (!File.Exists(CustomSourcesPath)) return new List<string>();
            var json = File.ReadAllText(CustomSourcesPath);
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch { return new List<string>(); }
    }

    public void SaveCustomSources(IEnumerable<string> sources)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CustomSourcesPath)!);
            File.WriteAllText(CustomSourcesPath, JsonSerializer.Serialize(sources.Distinct().ToList()));
        }
        catch (Exception ex)
        {
            Log.Debug("PluginStoreService", $"[PluginStore] 保存自定义源失败: {ex.Message}");
        }
    }

    public string[] GetAllSourceUrls()
        => new[] { DefaultStoreUrl }.Concat(GetCustomSources()).ToArray();

    // ═══════════════════════════════════════
    // 清单拉取与合并
    // ═══════════════════════════════════════

    /// <summary>拉取全部市场源并合并：按 id 去重，同 id 保留最高版本；所有源均失败时抛异常。</summary>
    public async Task<List<PluginStoreItem>> FetchAllAsync()
    {
        var merged = new Dictionary<string, PluginStoreItem>(StringComparer.OrdinalIgnoreCase);
        Exception? last = null;
        var urls = GetAllSourceUrls();
        foreach (var url in urls)
        {
            try
            {
                var items = await FetchSourceAsync(url);
                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.Id)) continue;
                    if (!merged.TryGetValue(item.Id, out var exist)
                        || CompareVersions(item.Version, exist.Version) > 0)
                    {
                        item.SourceName = url == DefaultStoreUrl ? "内置市场" : url;
                        merged[item.Id] = item;
                    }
                }
            }
            catch (Exception ex) { last = ex; }
        }
        if (merged.Count == 0 && last != null) throw last;
        return merged.Values.OrderBy(x => x.Name).ToList();
    }

    /// <summary>拉取单个市场源清单并解析（v1 数组 / v2 字典均支持）。
    /// 失败时把每个候选的成败写入异常消息，便于线上定位"哪个镜像、什么错"。</summary>
    public async Task<List<PluginStoreItem>> FetchSourceAsync(string storeUrl)
    {
        var attempts = new List<string>();
        foreach (var candidate in BuildCandidateUrls(storeUrl, excludeApi: false))
        {
            try
            {
                var json = await GetJsonAsync(candidate).ConfigureAwait(false);
                var items = Parse(json);
                if (items.Count > 0) return items;
                attempts.Add($"{ShortUrl(candidate)}=空清单");
            }
            catch (Exception ex)
            {
                attempts.Add($"{ShortUrl(candidate)}={ex.GetType().Name}");
            }
        }
        var detail = string.Join(" → ", attempts);
        throw new Exception($"无法连接市场源[{storeUrl}]：{detail}");
    }

    /// <summary>把 URL 截成"host/..."的紧凑形式，便于在错误消息里展示</summary>
    private static string ShortUrl(string url)
    {
        try
        {
            var u = new Uri(url);
            var path = u.AbsolutePath;
            return path.Length > 40 ? $"{u.Host}{path[..40]}…" : $"{u.Host}{path}";
        }
        catch { return url; }
    }

    /// <summary>获取 JSON 文本：GitHub API 的 contents 端点返回 base64 编码，需解码</summary>
    private async Task<string> GetJsonAsync(string url)
    {
        if (url.StartsWith("https://api.github.com/", StringComparison.OrdinalIgnoreCase)
            && url.Contains("/contents/", StringComparison.OrdinalIgnoreCase))
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url).ConfigureAwait(false));
            if (doc.RootElement.TryGetProperty("content", out var c))
            {
                var b64 = (c.GetString() ?? string.Empty).Replace("\n", "").Replace("\r", "");
                if (!string.IsNullOrEmpty(b64))
                    return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            }
            throw new Exception("GitHub API 响应缺少 content");
        }
        return await _http.GetStringAsync(url).ConfigureAwait(false);
    }

    /// <summary>解析商店清单 JSON：兼容 v1 数组（["plugins"] 数组）与 v2 字典（{"pluginId": {...}}）</summary>
    private static List<PluginStoreItem> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var items = new List<PluginStoreItem>();
        var root = doc.RootElement;

        // v2 字典：{ "plugin_id": { ... } }
        if (root.ValueKind == JsonValueKind.Object && !root.TryGetProperty("plugins", out _))
        {
            foreach (var prop in root.EnumerateObject())
            {
                try
                {
                    var item = JsonSerializer.Deserialize<PluginStoreItem>(prop.Value.GetRawText());
                    if (item != null)
                    {
                        if (string.IsNullOrWhiteSpace(item.Id)) item.Id = prop.Name;
                        if (!string.IsNullOrWhiteSpace(item.InstallUrl)) items.Add(item);
                    }
                }
                catch { }
            }
            return items;
        }

        // v1 数组 / 带 plugins 包裹的数组
        if (root.TryGetProperty("plugins", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in arr.EnumerateArray())
            {
                try
                {
                    var item = JsonSerializer.Deserialize<PluginStoreItem>(p.GetRawText());
                    if (item != null && !string.IsNullOrWhiteSpace(item.InstallUrl))
                        items.Add(item);
                }
                catch { }
            }
        }
        return items;
    }

    // ═══════════════════════════════════════
    // 下载与校验
    // ═══════════════════════════════════════

    /// <summary>下载插件包到本地临时文件，返回文件路径。
    /// <paramref name="fileName"/> 指定稳定文件名（默认从 URL 派生），便于覆盖安装避免旧副本堆积。</summary>
    public async Task<string> DownloadPluginAsync(string downloadUrl, IProgress<(string, int)>? progress = null, string? fileName = null)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "plugin.dll" : SanitizeFileName(fileName);
        var tmpPath = Path.Combine(Path.GetTempPath(), name);

        Exception? last = null;
        foreach (var candidate in BuildCandidateUrls(downloadUrl, excludeApi: true))
        {
            try
            {
                await DownloadFromUrlAsync(candidate, tmpPath, progress).ConfigureAwait(false);
                return tmpPath;
            }
            catch (Exception ex) { last = ex; }
        }

        try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
        throw last ?? new Exception("插件下载失败");
    }

    /// <summary>校验文件 sha256 是否匹配（hex 不区分大小写）。期望值为空时跳过校验返回 true。</summary>
    public bool VerifyHash(string filePath, string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256)) return true;
        try
        {
            var expected = expectedSha256.Trim();
            if (expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                expected = expected["sha256:".Length..].Trim();
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(filePath);
            var actual = Convert.ToHexString(sha.ComputeHash(fs));
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>当前应用版本是否满足插件兼容范围。</summary>
    public (bool Ok, string Message) CheckCompatibility(PluginStoreItem item)
    {
        var host = AppInfo.Current.VersionString;
        var ok = PluginVersionRange.IsSatisfied(item.MinAppVersion, host);
        var msg = string.IsNullOrWhiteSpace(item.MinAppVersion)
            ? string.Empty
            : $"该插件要求应用版本 {item.MinAppVersion}，当前 {host}，可能无法正常工作";
        return (ok, msg);
    }

    private static int CompareVersions(string? a, string? b)
    {
        if (Version.TryParse(a?.TrimStart('v', 'V'), out var va)
            && Version.TryParse(b?.TrimStart('v', 'V'), out var vb))
            return va.CompareTo(vb);
        return string.CompareOrdinal(a ?? string.Empty, b ?? string.Empty);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        return sb.ToString();
    }

    /// <summary>从单个 URL 流式下载到目标文件</summary>
    private async Task DownloadFromUrlAsync(string url, string tmpPath, IProgress<(string, int)>? progress)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1;
        await using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write);
        var buffer = new byte[8192];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer, 0, n).ConfigureAwait(false);
            read += n;
            if (total > 0) progress?.Report(("正在下载插件...", (int)(read * 100 / total)));
        }
        progress?.Report(("下载完成，正在安装...", 100));
    }
}
