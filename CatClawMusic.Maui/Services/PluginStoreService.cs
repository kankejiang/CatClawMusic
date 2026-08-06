using System.Text;
using System.Text.Json;

namespace CatClawMusic.Maui.Services;

/// <summary>插件商店条目（来自商店清单 plugins.json）</summary>
public class PluginStoreItem
{
    /// <summary>插件类型 ID（与 PluginManager 的 PluginTypeId 对应，如 OnlineMusic.netEaseMusic）</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>插件名称</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>插件版本</summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>插件作者</summary>
    public string Author { get; set; } = string.Empty;
    /// <summary>插件描述</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>插件分类</summary>
    public string Category { get; set; } = string.Empty;
    /// <summary>插件图标 Emoji</summary>
    public string Icon { get; set; } = "🧩";
    /// <summary>插件安装包下载地址（.dll 直链）</summary>
    public string InstallUrl { get; set; } = string.Empty;
}

/// <summary>
/// 插件商店服务 —— 从 GitHub 仓库的 plugins/plugins.json 清单拉取可用插件列表。
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

    private readonly HttpClient _http;

    public PluginStoreService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.Add("User-Agent", "CatClawMusic/1.0");
    }

    /// <summary>根据 raw 直链生成候选源列表：raw → jsDelivr CDN → GitHub API（dll 下载跳过 API，因其返回 base64 文本）</summary>
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
                if (!excludeApi)
                    urls.Add($"https://api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}");
            }
        }
        return urls;
    }

    /// <summary>拉取商店清单，返回可用插件列表（所有源均失败时抛异常由调用方处理）</summary>
    public async Task<List<PluginStoreItem>> FetchAsync(string? storeUrl = null)
    {
        var url = string.IsNullOrWhiteSpace(storeUrl) ? DefaultStoreUrl : storeUrl;
        Exception? last = null;
        foreach (var candidate in BuildCandidateUrls(url, excludeApi: false))
        {
            try
            {
                var json = await GetJsonAsync(candidate).ConfigureAwait(false);
                var items = Parse(json);
                if (items.Count > 0) return items;
            }
            catch (Exception ex) { last = ex; }
        }
        throw last ?? new Exception("无法连接插件商店");
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

    /// <summary>解析商店清单 JSON</summary>
    private static List<PluginStoreItem> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("plugins", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new List<PluginStoreItem>();

        var items = new List<PluginStoreItem>();
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
        return items;
    }

    /// <summary>下载插件 .dll 到本地临时文件，返回文件路径（供 InstallFromLocalFileAsync 安装）</summary>
    public async Task<string> DownloadPluginAsync(string downloadUrl, IProgress<(string, int)>? progress = null)
    {
        var tmpPath = Path.Combine(Path.GetTempPath(),
            $"catclaw_plugin_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N[..6]}.dll");

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
