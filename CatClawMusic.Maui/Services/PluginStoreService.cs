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
/// </summary>
public class PluginStoreService
{
    /// <summary>默认商店清单地址（CatClawMusic 仓库 plugins/plugins.json 的 raw 直链）</summary>
    public const string DefaultStoreUrl =
        "https://raw.githubusercontent.com/kankejiang/CatClawMusic/master/plugins/plugins.json";

    private readonly HttpClient _http;

    public PluginStoreService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("User-Agent", "CatClawMusic/1.0");
    }

    /// <summary>拉取商店清单，返回可用插件列表（失败抛异常由调用方处理）</summary>
    public async Task<List<PluginStoreItem>> FetchAsync(string? storeUrl = null)
    {
        var url = string.IsNullOrWhiteSpace(storeUrl) ? DefaultStoreUrl : storeUrl;
        var json = await _http.GetStringAsync(url).ConfigureAwait(false);
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
        using var resp = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1;
        var tmpPath = Path.Combine(Path.GetTempPath(),
            $"catclaw_plugin_{DateTime.Now:yyyyMMddHHmmss}.dll");

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
        return tmpPath;
    }
}
