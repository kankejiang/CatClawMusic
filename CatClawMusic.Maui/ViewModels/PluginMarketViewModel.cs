using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 插件市场页 ViewModel：拉取 GitHub 托管的市场清单（CatClawMusic.PluginMarket 仓库 index.json），
/// 展示可安装插件并支持一键安装/更新。
/// <para>
/// 数据链路（零服务器）：jsDelivr CDN 优先（国内可达），raw.githubusercontent.com 兜底；
/// 下载走清单 download_url（CI 已预填 sha256，下载后校验哈希再安装），无直链时回退
/// PluginManager.InstallFromGitHubAsync（按 repo 查 releases/latest）。
/// </para>
/// </summary>
public partial class PluginMarketViewModel : ObservableObject
{
    private readonly IPluginManager _pluginManager;

    private static readonly HttpClient s_http = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip
                                | System.Net.DecompressionMethods.Deflate,
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>市场清单候选地址：jsDelivr CDN（国内可达）→ GitHub raw 兜底</summary>
    private static readonly string[] s_indexUrls =
    {
        "https://cdn.jsdelivr.net/gh/kankejiang/CatClawMusic.PluginMarket@main/index.json",
        "https://raw.githubusercontent.com/kankejiang/CatClawMusic.PluginMarket/main/index.json",
    };

    /// <summary>市场插件条目列表</summary>
    public ObservableCollection<MarketPluginItem> Items { get; } = new();

    /// <summary>是否正在加载市场清单</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>清单拉取失败时的提示文本（空=正常）</summary>
    [ObservableProperty]
    private string _errorText = "";

    /// <summary>列表为空时的占位提示（加载成功但无条目）</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>市场源展示名（来自清单 $meta.name，拉取成功后填充）</summary>
    [ObservableProperty]
    private string _marketName = "插件市场";

    public PluginMarketViewModel(IPluginManager pluginManager)
    {
        _pluginManager = pluginManager;
        s_http.DefaultRequestHeaders.UserAgent.ParseAdd("CatClawMusic/1.8");
    }

    /// <summary>页面出现时加载市场清单（首次或出错时重试）</summary>
    public async Task OnAppearingAsync()
    {
        if (Items.Count == 0 || ErrorText.Length > 0)
            await LoadAsync();
        else
            await MarkInstalledStatesAsync();
    }

    /// <summary>拉取并解析市场清单，构建条目列表</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorText = "";
        try
        {
            // 候选源：SHA 钉定直链（最新，绕过 jsDelivr 分支解析 TTL 缓存）→ @main CDN（可能滞后）→ raw 兜底
            var urls = new List<string>(s_indexUrls);
            var sha = await TryResolveHeadShaAsync();
            if (sha.Length > 0)
                urls.Insert(0, $"https://cdn.jsdelivr.net/gh/kankejiang/CatClawMusic.PluginMarket@{sha}/index.json");

            string json = "";
            var failures = new List<string>();
            foreach (var url in urls)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    using var resp = await s_http.GetAsync(url, cts.Token);
                    resp.EnsureSuccessStatusCode();
                    json = await resp.Content.ReadAsStringAsync(cts.Token);
                    break;
                }
                catch (Exception ex)
                {
                    failures.Add($"{url.Replace("https://", "")[..Math.Min(50, url.Length - 8)]}={ex.GetType().Name}");
                }
            }
            if (json.Length == 0)
                throw new Exception(string.Join("；", failures));

            var items = ParseIndex(json, out var marketName);
            if (marketName.Length > 0)
                MarketName = marketName;
            Items.Clear();
            foreach (var it in items)
            {
                it.InstallCommand = InstallCommand;
                Items.Add(it);
            }
            IsEmpty = Items.Count == 0;
            await MarkInstalledStatesAsync();
        }
        catch (Exception ex)
        {
            Log.Debug("PluginMarketViewModel", $"[PluginMarket] 加载清单失败: {ex.Message}");
            ErrorText = $"市场加载失败：{ex.Message}\n请检查网络后下拉重试";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>从 GitHub API 解析市场仓库 main 分支 HEAD SHA（失败返回空，静默降级到 @main 直链）</summary>
    private static async Task<string> TryResolveHeadShaAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var resp = await s_http.GetAsync(
                "https://api.github.com/repos/kankejiang/CatClawMusic.PluginMarket/commits/main", cts.Token);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("sha", out var shaEl)
                && shaEl.ValueKind == JsonValueKind.String)
                return shaEl.GetString() ?? "";
        }
        catch (Exception ex)
        {
            Log.Debug("PluginMarketViewModel", $"[PluginMarket] HEAD SHA 解析失败（降级 @main）: {ex.Message}");
        }
        return "";
    }

    /// <summary>解析市场清单 JSON（$meta 提取市场名，其余每个根键是一个插件条目）</summary>
    private static List<MarketPluginItem> ParseIndex(string json, out string marketName)
    {
        var result = new List<MarketPluginItem>();
        marketName = "";
        using var doc = JsonDocument.Parse(json);
        foreach (var kv in doc.RootElement.EnumerateObject())
        {
            if (kv.Name == "$meta")
            {
                if (kv.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                    marketName = n.GetString() ?? "";
                continue;
            }
            var v = kv.Value;
            string Str(string prop)
            {
                return v.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String
                    ? el.GetString() ?? "" : "";
            }
            var item = new MarketPluginItem
            {
                PluginId = kv.Name,
                DisplayName = Str("display_name") is { Length: > 0 } dn ? dn : Str("name"),
                Name = Str("name"),
                Author = Str("author"),
                Version = Str("version"),
                Desc = Str("desc"),
                ShortDesc = Str("short_desc"),
                Repo = Str("repo"),
                DownloadUrl = Str("download_url"),
                Sha256 = Str("sha256"),
            };
            if (v.TryGetProperty("stars", out var stars) && stars.ValueKind == JsonValueKind.Number && stars.TryGetInt32(out var sv))
                item.Stars = sv;
            if (v.TryGetProperty("updated_at", out var upd) && upd.ValueKind == JsonValueKind.String)
                item.UpdatedAt = upd.GetString() ?? "";
            if (v.TryGetProperty("platforms", out var plats) && plats.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in plats.EnumerateArray())
                    if (p.ValueKind == JsonValueKind.String)
                        item.Platforms.Add(p.GetString() ?? "");
            }
            if (v.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tags.EnumerateArray())
                    if (t.ValueKind == JsonValueKind.String)
                        item.Tags.Add(t.GetString() ?? "");
            }
            if (item.DisplayName.Length > 0 && item.Version.Length > 0)
                result.Add(item);
        }
        return result;
    }

    /// <summary>比对已安装插件，更新各条目的安装/可更新状态</summary>
    private async Task MarkInstalledStatesAsync()
    {
        try
        {
            var all = await Task.Run(_pluginManager.GetAllPlugins);
            foreach (var it in Items)
            {
                if (it.PluginId.Length == 0) continue;
                var installed = all.FirstOrDefault(p =>
                    string.Equals(p.Plugin.Author, it.Author, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Plugin.PluginId, it.Name, StringComparison.OrdinalIgnoreCase));
                if (installed == null)
                {
                    it.IsInstalled = false;
                    it.StatusText = "未安装";
                }
                else
                {
                    it.IsInstalled = true;
                    it.InstalledVersion = installed.Version;
                    var newer = IsNewerVersion(it.Version, installed.Version);
                    it.StatusText = newer ? $"v{installed.Version} → 可更新" : $"已装 v{installed.Version}";
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("PluginMarketViewModel", $"[PluginMarket] 已安装比对失败: {ex.Message}");
        }
    }

    /// <summary>简版语义化版本比较：a 是否比 b 新（逐段数字比较）</summary>
    private static bool IsNewerVersion(string a, string b)
    {
        static int[] Parse(string s)
        {
            var parts = s.TrimStart('v', 'V').Split('.');
            var nums = new int[Math.Max(parts.Length, 1)];
            for (int i = 0; i < parts.Length && i < nums.Length; i++)
                int.TryParse(parts[i], out nums[i]);
            return nums;
        }
        var pa = Parse(a);
        var pb = Parse(b);
        var n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            var da = i < pa.Length ? pa[i] : 0;
            var db = i < pb.Length ? pb[i] : 0;
            if (da != db) return da > db;
        }
        return false;
    }

    /// <summary>安装/更新市场插件：下载 .ccp → sha256 校验 → 走本地安装链路</summary>
    [RelayCommand]
    public async Task InstallAsync(MarketPluginItem? item)
    {
        if (item == null || item.IsInstalling) return;
        item.IsInstalling = true;
        try
        {
            string localPath;
            if (item.DownloadUrl.Length > 0)
            {
                item.InstallStatus = "下载中...";
                localPath = await DownloadAsync(item);
            }
            else
            {
                // 清单无直链（CI 未同步到 release）：回退 GitHub 仓库安装链路
                item.InstallStatus = "从仓库获取...";
                var info = await _pluginManager.InstallFromGitHubAsync(item.Repo);
                if (info == null)
                    throw new Exception("仓库安装失败");
                item.InstallStatus = "";
                await MarkInstalledStatesAsync();
                await ShowAlertAsync("插件市场", $"已安装「{item.DisplayName}」v{info.Version}");
                return;
            }

            item.InstallStatus = "安装中...";
            var progress = new Progress<(string, int)>(_ => { });
            var result = await _pluginManager.InstallFromLocalFileAsync(localPath, progress);
            try { File.Delete(localPath); } catch { }
            if (result == null)
                throw new Exception("插件文件无效或格式不受支持");

            item.InstallStatus = "";
            await MarkInstalledStatesAsync();
            await ShowAlertAsync("插件市场", $"已安装「{item.DisplayName}」v{result.Version}");
        }
        catch (Exception ex)
        {
            Log.Debug("PluginMarketViewModel", $"[PluginMarket] 安装 {item.PluginId} 失败: {ex.Message}");
            item.InstallStatus = "";
            await ShowAlertAsync("插件市场", $"安装「{item.DisplayName}」失败：{ex.Message}");
        }
        finally
        {
            item.IsInstalling = false;
        }
    }

    /// <summary>下载插件包到缓存目录，校验 sha256（清单提供时）与 MZ 头</summary>
    private static async Task<string> DownloadAsync(MarketPluginItem item)
    {
        var dir = Path.Combine(FileSystem.CacheDirectory, "market-downloads");
        Directory.CreateDirectory(dir);
        var fileName = Path.GetFileName(new Uri(item.DownloadUrl).AbsolutePath);
        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c.ToString(), "_");
        var localPath = Path.Combine(dir, fileName);

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)))
        using (var resp = await s_http.GetAsync(item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token))
        {
            resp.EnsureSuccessStatusCode();
            await using (var fs = File.Create(localPath))
            await using (var src = await resp.Content.ReadAsStreamAsync(cts.Token))
                await src.CopyToAsync(fs, cts.Token);
        }

        // sha256 校验（清单 CI 预填；不匹配说明包被篡改或清单过期，拒绝安装）
        if (item.Sha256.Length == 64)
        {
            await using var fs = File.OpenRead(localPath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
            if (hash != item.Sha256.ToLowerInvariant())
            {
                try { File.Delete(localPath); } catch { }
                throw new Exception("校验失败：文件哈希与市场清单不一致");
            }
        }

        // PE 头校验（.ccp 必须是程序集）
        using (var fs = File.OpenRead(localPath))
        {
            Span<byte> head = stackalloc byte[2];
            if (fs.Read(head) != 2 || head[0] != 0x4D || head[1] != 0x5A)
            {
                try { File.Delete(localPath); } catch { }
                throw new Exception("文件不是有效的插件包");
            }
        }
        return localPath;
    }

    private static async Task ShowAlertAsync(string title, string message)
    {
        try
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null) await page.DisplayAlertAsync(title, message, "确定");
        }
        catch { }
    }
}

/// <summary>市场插件条目展示项</summary>
public partial class MarketPluginItem : ObservableObject
{
    /// <summary>plugin_id（author/name 形式的根键）</summary>
    public string PluginId { get; init; } = "";
    /// <summary>插件 ID（与 IPlugin.PluginId 一致）</summary>
    public string Name { get; init; } = "";
    /// <summary>作者（与 IPlugin.Author 一致）</summary>
    public string Author { get; init; } = "";
    /// <summary>显示名称（display_name 优先，回退 name）</summary>
    public string DisplayName { get; init; } = "";
    /// <summary>详细描述</summary>
    public string Desc { get; init; } = "";
    /// <summary>短描述（紧凑 UI）</summary>
    public string ShortDesc { get; init; } = "";
    /// <summary>源码仓库</summary>
    public string Repo { get; init; } = "";
    /// <summary>.ccp 下载直链（CI 同步填充，可能为空）</summary>
    public string DownloadUrl { get; init; } = "";
    /// <summary>包 SHA-256（CI 同步填充，可能为空）</summary>
    public string Sha256 { get; init; } = "";
    /// <summary>市场版本号</summary>
    public string Version { get; init; } = "";
    /// <summary>星标数（-1=未知）</summary>
    public int Stars { get; set; } = -1;
    /// <summary>最近更新时间（ISO 8601）</summary>
    public string UpdatedAt { get; set; } = "";
    /// <summary>支持平台</summary>
    public List<string> Platforms { get; } = new();
    /// <summary>标签</summary>
    public List<string> Tags { get; } = new();

    /// <summary>是否已安装</summary>
    [ObservableProperty]
    private bool _isInstalled;

    /// <summary>已安装版本（未安装为空）</summary>
    [ObservableProperty]
    private string _installedVersion = "";

    /// <summary>安装/更新状态文本（列表徽标）</summary>
    [ObservableProperty]
    private string _statusText = "未安装";

    /// <summary>是否正在下载/安装中</summary>
    [ObservableProperty]
    private bool _isInstalling;

    /// <summary>安装过程状态（按钮内联文本）</summary>
    [ObservableProperty]
    private string _installStatus = "";

    /// <summary>安装命令（由 VM 注入，绑定用）</summary>
    public System.Windows.Input.ICommand? InstallCommand { get; set; }

    /// <summary>徽标颜色（已装绿 / 可更新橙 / 未装主题色）</summary>
    public string StatusColor => StatusText.Contains("可更新") ? "#FF9800"
        : IsInstalled ? "#4CAF50"
        : "#B8C7FF";

    /// <summary>是否为 $meta 占位项</summary>
    public bool IsMeta => PluginId.Length == 0;
    /// <summary>按钮文本（安装 / 更新）</summary>
    public string ActionText => StatusText.Contains("可更新") ? "更新" : "安装";

    /// <summary>平台徽标文本（如 "Android · Windows"）</summary>
    public string PlatformsText => string.Join(" · ", Platforms.Select(p =>
        p.Equals("android", StringComparison.OrdinalIgnoreCase) ? "Android"
        : p.Equals("windows", StringComparison.OrdinalIgnoreCase) ? "Windows" : p));

    /// <summary>星标展示文本（★123，无数据为空）</summary>
    public string StarsText => Stars >= 0 ? $"★ {Stars}" : "";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(StatusText))
        {
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(ActionText));
        }
    }
}
