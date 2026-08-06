using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 插件管理页 ViewModel：展示已安装插件列表、启用/禁用开关、状态标签，并支持两种方式添加插件：
/// 本地安装（FilePicker 选 .dll/.ccp）与网络安装（GitHub 仓库地址自动拉取最新 Release 附件，或直接输入直链）。
/// （在线插件商店已于 2026-08-06 移除：多市场源/联网拉取在国内网络不可靠，改为按需网络安装。）
/// </summary>
public partial class PluginManagementViewModel : ObservableObject
{
    private readonly IPluginManager _pluginManager;

    private static readonly HttpClient s_http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        hc.DefaultRequestHeaders.UserAgent.ParseAdd("CatClawMusic/1.7");
        return hc;
    }

    /// <summary>插件项展示列表（已安装）</summary>
    public ObservableCollection<PluginItemView> Plugins { get; } = new();

    /// <summary>是否正在刷新插件列表</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>插件汇总文本（如"共 N 个插件，已启用 M 个"）</summary>
    [ObservableProperty]
    private string _summary = "加载中...";

    /// <summary>是否正在网络安装（解析地址/下载/安装中）</summary>
    [ObservableProperty]
    private bool _isInstalling;

    /// <summary>网络安装过程状态文本</summary>
    [ObservableProperty]
    private string _installStatusText = "";

    /// <summary>
    /// 初始化 <see cref="PluginManagementViewModel"/> 实例。
    /// </summary>
    /// <param name="pluginManager">插件管理器，用于读取与切换插件状态</param>
    public PluginManagementViewModel(IPluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    /// <summary>页面出现时刷新插件列表</summary>
    public async Task OnAppearingAsync()
    {
        await RefreshAsync();
    }

    /// <summary>刷新插件列表</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            Plugins.Clear();
            var list = _pluginManager.GetAllPlugins();
            foreach (var p in list)
                Plugins.Add(new PluginItemView(p, _pluginManager.IsPluginEnabled(p.PluginTypeId)));

            var enabled = Plugins.Count(x => x.IsEnabled);
            Summary = Plugins.Count > 0
                ? $"共 {Plugins.Count} 个插件，已启用 {enabled} 个"
                : "当前没有可用插件";
        }
        catch (Exception ex)
        {
            Summary = $"加载失败：{ex.Message}";
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] Refresh 失败: {ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>添加插件入口：弹出选择（本地安装 / 网络安装）</summary>
    [RelayCommand]
    public async Task AddPluginAsync()
    {
        try
        {
            var page = CurrentPage();
            if (page == null) return;
            var choice = await page.DisplayActionSheet("添加插件", "取消", null, "📁 本地安装", "🌐 网络安装");
            if (choice == "📁 本地安装")
                await InstallFromLocalAsync();
            else if (choice == "🌐 网络安装")
                await InstallFromNetworkAsync();
        }
        catch (Exception ex)
        {
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] 添加插件失败: {ex.Message}");
            await ShowAlertAsync("插件", $"添加失败：{ex.Message}");
        }
    }

    /// <summary>本地安装：FilePicker 选择 .dll / .ccp 后安装</summary>
    private async Task InstallFromLocalAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择插件文件",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = new[] { ".ccp" },
                    [DevicePlatform.Android] = new[] { "application/octet-stream", "application/x-msdownload", "*/*" },
                }),
            });
            if (result == null) return;
            await InstallFileAsync(result.FullPath);
        }
        catch (Exception ex)
        {
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] 本地安装失败: {ex.Message}");
            await ShowAlertAsync("插件", $"本地安装失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 网络安装：输入 GitHub 仓库地址（自动拉取最新 Release 中的 .dll/.ccp 附件）
    /// 或插件包直链，下载后安装。
    /// </summary>
    private async Task InstallFromNetworkAsync()
    {
        try
        {
            var page = CurrentPage();
            if (page == null) return;
            var input = await page.DisplayPromptAsync(
                "网络安装插件",
                "输入 GitHub 仓库地址，自动拉取最新 Release 中的插件包（.ccp）；\n也可直接粘贴插件包下载直链：\n\n仓库示例：https://github.com/owner/repo\n直链示例：https://.../xxx.ccp",
                "下一步", "取消",
                placeholder: "https://github.com/owner/repo",
                keyboard: Keyboard.Url);
            if (string.IsNullOrWhiteSpace(input)) return;
            input = input.Trim();

            IsInstalling = true;
            try
            {
                InstallStatusText = "正在解析下载地址...";
                var urls = await ResolveDownloadUrlsAsync(input);
                if (urls.Count == 0)
                {
                    await ShowAlertAsync("网络安装", "该仓库的最新 Release 中没有 .ccp 插件包");
                    return;
                }

                InstallStatusText = "正在下载插件...";
                var (fileName, localPath) = await DownloadFirstAsync(urls);

                InstallStatusText = "正在安装...";
                await InstallFileAsync(localPath);

                try { File.Delete(localPath); } catch { }
            }
            finally
            {
                IsInstalling = false;
                InstallStatusText = "";
            }
        }
        catch (Exception ex)
        {
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] 网络安装失败: {ex.Message}");
            await ShowAlertAsync("网络安装", $"安装失败：{ex.Message}");
        }
    }

    /// <summary>解析用户输入为候选下载地址列表（仓库地址 → 查 Release API 找附件；否则视为直链）</summary>
    private static async Task<List<string>> ResolveDownloadUrlsAsync(string input)
    {
        var m = Regex.Match(input, @"github\.com/([^/]+)/([^/?#]+?)(?:/releases/tag/([^/?#]+))?",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            return new List<string> { input.Trim() };

        var owner = m.Groups[1].Value;
        var repo = m.Groups[2].Value.TrimEnd('/');
        var tag = m.Groups[3].Success ? m.Groups[3].Value : null;
        var api = tag != null
            ? $"https://api.github.com/repos/{owner}/{repo}/releases/tags/{Uri.EscapeDataString(tag)}"
            : $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

        var json = await GetJsonAsync(new[] { api, $"https://gh-proxy.com/{api}" });
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return new List<string>();

        var urls = new List<string>();
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameEl)) continue;
            var name = nameEl.GetString() ?? "";
            if (!name.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase)) continue;
            if (asset.TryGetProperty("browser_download_url", out var urlEl))
            {
                var url = urlEl.GetString();
                if (!string.IsNullOrWhiteSpace(url)) urls.Add(url.Trim());
            }
        }
        return urls;
    }

    /// <summary>依次尝试候选下载地址，保存插件文件到临时目录；全部失败抛异常（含逐候选诊断）</summary>
    private static async Task<(string fileName, string localPath)> DownloadFirstAsync(List<string> urls)
    {
        var failures = new List<string>();
        foreach (var u in urls)
        {
            foreach (var mirror in BuildDownloadMirrors(u))
            {
                try
                {
                    var fileName = PickFileName(u);
                    var dir = Path.Combine(FileSystem.CacheDirectory, "plugin-downloads");
                    Directory.CreateDirectory(dir);
                    var localPath = Path.Combine(dir, fileName);

                    using var resp = await s_http.GetAsync(mirror, HttpCompletionOption.ResponseHeadersRead);
                    resp.EnsureSuccessStatusCode();
                    await using (var fs = File.Create(localPath))
                    await using (var src = await resp.Content.ReadAsStreamAsync())
                        await src.CopyToAsync(fs);

                    // 轻量校验：.ccp 内容应为 PE 程序集（MZ 头）
                    if (fileName.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase))
                    {
                        using var fs = File.OpenRead(localPath);
                        Span<byte> head = stackalloc byte[2];
                        if (fs.Read(head) != 2 || head[0] != 0x4D || head[1] != 0x5A)
                        {
                            failures.Add($"{ShortUrl(mirror)}=非PE程序集");
                            try { File.Delete(localPath); } catch { }
                            continue;
                        }
                    }
                    return (fileName, localPath);
                }
                catch (Exception ex)
                {
                    failures.Add($"{ShortUrl(mirror)}={ex.GetType().Name}");
                }
            }
        }
        throw new Exception($"所有下载地址均不可用：\n{string.Join("\n→ ", failures)}");
    }

    /// <summary>GET 请求候选列表，返回第一个成功响应的文本；全部失败抛异常（含逐候选诊断）</summary>
    private static async Task<string> GetJsonAsync(IEnumerable<string> candidates)
    {
        var failures = new List<string>();
        foreach (var c in candidates)
        {
            try
            {
                using var resp = await s_http.GetAsync(c);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                failures.Add($"{ShortUrl(c)}={ex.GetType().Name}");
            }
        }
        throw new Exception($"无法访问发布信息：\n{string.Join("\n→ ", failures)}");
    }

    /// <summary>为下载地址构造镜像候选（GitHub 地址追加 gh-proxy.com 反代）</summary>
    private static IEnumerable<string> BuildDownloadMirrors(string url)
    {
        yield return url;
        if (url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase))
            yield return "https://gh-proxy.com/" + url;
    }

    /// <summary>从下载 URL 提取稳定文件名（无扩展名时回退 plugin_{guid}.ccp）</summary>
    private static string PickFileName(string url)
    {
        var name = "";
        try { name = Path.GetFileName(new Uri(url).AbsolutePath); } catch { }
        if (string.IsNullOrWhiteSpace(name) || name is "/" or "\\")
            name = $"plugin_{Guid.NewGuid():N}.ccp";
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c.ToString(), "_");
        if (name.Length > 80) name = name[^80..];
        if (!name.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase))
            name += ".ccp";
        return name;
    }

    private static string ShortUrl(string url)
    {
        var u = url.Replace("https://", "").Replace("http://", "");
        return u.Length <= 70 ? u : u[..70] + "...";
    }

    /// <summary>安装本地插件文件（本地选择或网络下载后共用），成功则刷新列表</summary>
    private async Task InstallFileAsync(string path)
    {
        try
        {
            // 记录文件诊断信息：路径、文件名、大小、前两字节（判断是否 PE 程序集 MZ 头）
            long size = -1;
            string headHex = "N/A";
            try
            {
                var fi = new FileInfo(path);
                size = fi.Exists ? fi.Length : -1;
                if (fi.Exists && fi.Length >= 2)
                {
                    using var fs = File.OpenRead(path);
                    var b0 = fs.ReadByte();
                    var b1 = fs.ReadByte();
                    headHex = $"0x{b0:X2} 0x{b1:X2} (MZ={(b0 == 0x4D && b1 == 0x5A ? "是" : "否")})";
                }
            }
            catch { }
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] 开始安装: 路径={path} 大小={size} 头={headHex}");

            var progress = new Progress<(string, int)>(_ => { /* 无进度 UI，忽略 */ });
            var info = await _pluginManager.InstallFromLocalFileAsync(path, progress);
            if (info != null)
            {
                await RefreshAsync();
                await ShowAlertAsync("插件", $"已安装「{info.DisplayName}」v{info.Version}");
            }
            else
            {
                await ShowAlertAsync("插件", "安装失败：插件文件无效或格式不受支持（仅支持 .ccp 格式插件包）");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] 安装失败: {ex.GetType().Name}: {ex.Message}\n{ex}");
            await ShowAlertAsync("插件", $"安装失败：{ex.Message}");
        }
    }

    /// <summary>切换插件启用状态</summary>
    [RelayCommand]
    public void ToggleEnabled(PluginItemView? item)
    {
        if (item == null) return;
        try
        {
            var newState = !item.IsEnabled;
            _pluginManager.SetPluginEnabled(item.PluginTypeId, newState);
            item.IsEnabled = newState;
            UpdateSummary();
        }
        catch (Exception ex)
        {
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] Toggle 失败: {ex.Message}");
        }
    }

    /// <summary>卸载（删除）已安装插件：删文件、清启用偏好与索引，刷新列表</summary>
    [RelayCommand]
    public async Task UninstallPluginAsync(PluginItemView? item)
    {
        if (item == null || !item.CanUninstall) return;
        var ok = await ShowConfirmAsync("卸载插件",
            $"确定要卸载「{item.DisplayName}」吗？\n插件文件将被删除，相关功能立即停用。");
        if (!ok) return;
        try
        {
            var success = await _pluginManager.UninstallPluginAsync(item.PluginTypeId);
            if (success)
            {
                await RefreshAsync();
                await ShowAlertAsync("卸载成功", $"已卸载「{item.DisplayName}」");
            }
            else
            {
                await ShowAlertAsync("卸载失败", "插件卸载失败（可能正在使用）");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] 卸载失败: {ex.Message}");
            await ShowAlertAsync("卸载失败", ex.Message);
        }
    }

    /// <summary>配置插件（当前仅网易云：粘贴网页版 Cookie 增强推荐个性化）</summary>
    [RelayCommand]
    public async Task ConfigurePluginAsync(PluginItemView? item)
    {
        if (item == null || !item.CanConfigure) return;
        try
        {
            var page = CurrentPage();
            if (page == null) return;
            var cookie = await page.DisplayPromptAsync(
                "配置网易云 Cookie",
                "在浏览器登录 music.163.com，按 F12 → 控制台输入 document.cookie，复制结果粘贴到此处。\n可提升推荐个性化与播放完整度（可留空清除）。",
                "保存", "取消",
                maxLength: 2000);
            if (cookie == null) return;

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatClawMusic.Maui");
            Directory.CreateDirectory(dir);
            var cfg = Path.Combine(dir, "netease_cookie.txt");
            var text = cookie.Trim();
            if (text.Length == 0) { try { File.Delete(cfg); } catch { } }
            else File.WriteAllText(cfg, text);
            await ShowAlertAsync("配置完成", text.Length == 0 ? "已清除 Cookie" : "Cookie 已保存，重启插件后生效");
        }
        catch (Exception ex)
        {
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] 配置失败: {ex.Message}");
            await ShowAlertAsync("配置失败", ex.Message);
        }
    }

    private void UpdateSummary()
    {
        var enabled = Plugins.Count(x => x.IsEnabled);
        Summary = Plugins.Count > 0
            ? $"共 {Plugins.Count} 个插件，已启用 {enabled} 个"
            : "当前没有可用插件";
    }

    private static Page? CurrentPage()
    {
        try { return Application.Current?.Windows.FirstOrDefault()?.Page; } catch { return null; }
    }

    private async Task ShowAlertAsync(string title, string message)
    {
        try
        {
            var page = CurrentPage();
            if (page != null) await page.DisplayAlertAsync(title, message, "确定");
        }
        catch { }
    }

    private async Task<bool> ShowConfirmAsync(string title, string message, string accept = "卸载")
    {
        try
        {
            var page = CurrentPage();
            if (page == null) return false;
            return await page.DisplayAlertAsync(title, message, accept, "取消");
        }
        catch { return false; }
    }
}

/// <summary>插件展示项（已安装列表）</summary>
public partial class PluginItemView : ObservableObject
{
    /// <summary>插件元数据信息</summary>
    public PluginInfo Info { get; }

    /// <summary>插件类型 ID</summary>
    public string PluginTypeId => Info.PluginTypeId;
    /// <summary>展示名称</summary>
    public string DisplayName => Info.DisplayName;
    /// <summary>插件版本</summary>
    public string Version => Info.Version;
    /// <summary>插件作者</summary>
    public string Author => Info.Author;
    /// <summary>插件描述（优先使用元数据描述，回退到插件实例描述）</summary>
    public string Description => string.IsNullOrWhiteSpace(Info.Description) ? Info.Plugin.Description : Info.Description;
    /// <summary>插件分类展示文本</summary>
    public string CategoryText => Info.Category switch
    {
        PluginCategory.LyricsProvider => "歌词",
        PluginCategory.ProtocolProvider => "协议",
        PluginCategory.CoverProvider => "封面",
        PluginCategory.AudioEnhancer => "音效",
        PluginCategory.MenuContributor => "菜单",
        PluginCategory.OnlineMusic => "在线音源",
        _ => "其他"
    };
    /// <summary>插件图标 Emoji（缺省 🧩）</summary>
    public string IconEmoji => string.IsNullOrWhiteSpace(Info.IconEmoji) ? "🧩" : Info.IconEmoji;
    /// <summary>插件来源展示文本（内置 / 已安装）</summary>
    public string SourceText => Info.Source == PluginSource.BuiltIn ? "内置" : "已安装";

    /// <summary>是否可卸载（仅动态安装的插件可卸载，内置插件不可）</summary>
    public bool CanUninstall => Info.Source == PluginSource.Installed;

    /// <summary>是否可配置（当前仅网易云插件支持 Cookie 配置）</summary>
    public bool CanConfigure => CanUninstall && Info.PluginTypeId.Contains("netEase", StringComparison.OrdinalIgnoreCase);

    /// <summary>该插件是否已启用</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    private bool _isEnabled;

    /// <summary>状态展示文本（已启用 / 已禁用）</summary>
    public string StatusText => IsEnabled ? "已启用" : "已禁用";
    /// <summary>状态展示颜色（已启用绿色 / 已禁用灰色）</summary>
    public string StatusColor => IsEnabled ? "#4CAF50" : "#9E9E9E";

    public PluginItemView(PluginInfo info, bool isEnabled)
    {
        Info = info;
        IsEnabled = isEnabled;
    }
}
