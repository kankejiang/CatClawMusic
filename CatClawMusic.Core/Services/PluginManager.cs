using System.Reflection;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 插件管理器实现，负责插件的注册、加载、安装、卸载和生命周期管理
/// </summary>
public class PluginManager : IPluginManager
{
    /// <summary>已注册的插件列表</summary>
    private readonly List<PluginInfo> _plugins = new();
    /// <summary>读取插件启用状态的委托</summary>
    private readonly Func<string, bool> _getPrefFunc;
    /// <summary>写入插件启用状态的委托</summary>
    private readonly Action<string, bool> _setPrefFunc;
    /// <summary>插件安装目录</summary>
    private readonly string _pluginsDir;
    /// <summary>HTTP 客户端</summary>
    private readonly HttpClient _httpClient = new();
    /// <summary>已安装插件 ID 集合</summary>
    private readonly HashSet<string> _installedPluginIds = new();

    /// <summary>
    /// 创建插件管理器实例
    /// </summary>
    /// <param name="plugins">内置插件列表</param>
    /// <param name="getPrefFunc">读取偏好设置的委托</param>
    /// <param name="setPrefFunc">写入偏好设置的委托</param>
    /// <param name="pluginsDir">插件安装目录</param>
    public PluginManager(
        IEnumerable<IPlugin> plugins,
        Func<string, bool> getPrefFunc,
        Action<string, bool> setPrefFunc,
        string pluginsDir)
    {
        _getPrefFunc = getPrefFunc ?? throw new ArgumentNullException(nameof(getPrefFunc));
        _setPrefFunc = setPrefFunc ?? throw new ArgumentNullException(nameof(setPrefFunc));
        _pluginsDir = pluginsDir ?? throw new ArgumentNullException(nameof(pluginsDir));

        Directory.CreateDirectory(_pluginsDir);

        LoadInstalledIndex();

        foreach (var plugin in plugins)
        {
            var info = CreatePluginInfo(plugin);
            info.IsEnabled = _getPrefFunc($"plugin_enabled_{info.PluginTypeId}");
            _plugins.Add(info);
        }

        LoadInstalledPlugins();
    }

    /// <summary>获取所有已注册的插件</summary>
    public List<PluginInfo> GetAllPlugins()
    {
        return _plugins.ToList();
    }

    /// <summary>获取指定类型的所有已启用插件实例</summary>
    public List<T> GetEnabledPlugins<T>() where T : IPlugin
    {
        var result = new List<T>();
        foreach (var p in _plugins)
        {
            if (!p.IsEnabled) continue;
            if (p.Plugin is T t)
                result.Add(t);
            foreach (var sub in p.SubPlugins)
            {
                if (sub is T st)
                    result.Add(st);
            }
        }
        return result;
    }

    /// <summary>判断插件是否已启用</summary>
    public bool IsPluginEnabled(string pluginTypeId)
    {
        return _plugins.FirstOrDefault(p => p.PluginTypeId == pluginTypeId)?.IsEnabled ?? false;
    }

    /// <summary>设置插件的启用状态并持久化</summary>
    public void SetPluginEnabled(string pluginTypeId, bool enabled)
    {
        var plugin = _plugins.FirstOrDefault(p => p.PluginTypeId == pluginTypeId);
        if (plugin == null) return;

        plugin.IsEnabled = enabled;
        _setPrefFunc($"plugin_enabled_{pluginTypeId}", enabled);
    }

    /// <summary>初始化所有已启用的插件</summary>
    public async Task InitializeAllAsync()
    {
        foreach (var info in _plugins.Where(p => p.IsEnabled))
        {
            try
            {
                await info.Plugin.InitializeAsync();
            }
            catch
            {
                info.IsEnabled = false;
            }
            foreach (var sub in info.SubPlugins)
            {
                try { await sub.InitializeAsync(); } catch { }
            }
        }
    }

    /// <summary>关闭所有已启用的插件</summary>
    public async Task ShutdownAllAsync()
    {
        foreach (var info in _plugins.Where(p => p.IsEnabled))
        {
            try { await info.Plugin.ShutdownAsync(); } catch { }
            foreach (var sub in info.SubPlugins)
            {
                try { await sub.ShutdownAsync(); } catch { }
            }
        }
    }

    /// <summary>从本地文件安装插件（.dll 或 .ccp）</summary>
    public async Task<PluginInfo?> InstallFromLocalFileAsync(string filePath, IProgress<(string, int)>? progress = null)
    {
        try
        {
            progress?.Report(("正在读取插件文件...", 10));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("未找到插件文件", filePath);

            var fileName = Path.GetFileName(filePath);
            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && !fileName.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("仅支持 .dll 或 .ccp 格式的插件文件");

            var destPath = Path.Combine(_pluginsDir, fileName);
            if (File.Exists(destPath))
            {
                var ext = Path.GetExtension(fileName);
                destPath = Path.Combine(_pluginsDir,
                    $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:yyyyMMddHHmmss}{ext}");
            }

            progress?.Report(("正在复制插件...", 30));
            File.Copy(filePath, destPath);

            progress?.Report(("正在加载插件...", 60));

            return await LoadAndRegisterPluginAsync(destPath, destPath, progress);
        }
        catch (Exception ex)
        {
            progress?.Report(($"安装失败: {ex.Message}", 100));
            return null;
        }
    }

    /// <summary>从 GitHub Release 安装插件</summary>
    public async Task<PluginInfo?> InstallFromGitHubAsync(string repoUrl, IProgress<(string, int)>? progress = null)
    {
        try
        {
            progress?.Report(("正在解析仓库地址...", 5));

            string owner, repo;
            try
            {
                var uri = new Uri(repoUrl);
                var segs = uri.AbsolutePath.Trim('/').Split('/');
                if (segs.Length < 2)
                    throw new Exception();
                owner = segs[0];
                repo = segs[1];
            }
            catch
            {
                throw new InvalidOperationException("无法解析 GitHub 仓库地址，请使用格式: https://github.com/用户名/仓库名");
            }

            progress?.Report(("正在获取 Release 信息...", 15));

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CatClawMusic/1.0");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");

            var releasesUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            var releasesJson = await _httpClient.GetStringAsync(releasesUrl);

            using var doc = System.Text.Json.JsonDocument.Parse(releasesJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("assets", out var assets) || assets.GetArrayLength() == 0)
            {
                throw new InvalidOperationException(
                    $"仓库 {owner}/{repo} 的最新 Release 没有包含附件。\n" +
                    "请先在 GitHub 上创建 Release 并上传编译好的 .dll 文件。\n" +
                    "或使用「从本地安装」导入已编译的 DLL。");
            }

            string? dllUrl = null;
            string? dllName = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase))
                {
                    dllUrl = asset.GetProperty("browser_download_url").GetString();
                    dllName = name;
                    break;
                }
            }

            if (dllUrl == null)
            {
                throw new InvalidOperationException(
                    $"仓库 {owner}/{repo} 的 Release 中没有找到 .dll 文件。\n" +
                    "请上传编译好的插件 DLL 到 Release Assets。");
            }

            progress?.Report(("正在下载插件...", 30));

            var destPath = Path.Combine(_pluginsDir, dllName ?? "plugin.dll");

            var response = await _httpClient.GetAsync(dllUrl);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1;

            using var remoteStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write);
            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await remoteStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;
                if (totalBytes > 0)
                {
                    var pct = (int)(30 + totalRead * 40 / totalBytes);
                    progress?.Report(("正在下载插件...", pct));
                }
            }

            progress?.Report(("正在加载插件...", 75));

            return await LoadAndRegisterPluginAsync(destPath, repoUrl, progress);
        }
        catch (Exception ex)
        {
            progress?.Report(($"安装失败: {ex.Message}", 100));
            return null;
        }
    }

    /// <summary>加载并注册插件程序集</summary>
    private async Task<PluginInfo?> LoadAndRegisterPluginAsync(string localPath, string sourceUrl, IProgress<(string, int)>? progress)
    {
        var fileBytes = File.ReadAllBytes(localPath);
        var assembly = Assembly.Load(fileBytes);

        var pluginTypes = assembly.GetTypes()
            .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .ToList();

        if (pluginTypes.Count == 0)
        {
            File.Delete(localPath);
            throw new InvalidOperationException("插件程序集中未找到有效的IPlugin实现");
        }

        progress?.Report(("正在初始化插件...", 85));

        List<IPlugin> instances = new();
        foreach (var type in pluginTypes)
        {
            if (Activator.CreateInstance(type) is IPlugin pluginInstance)
                instances.Add(pluginInstance);
        }

        if (instances.Count == 0)
        {
            File.Delete(localPath);
            throw new InvalidOperationException("无法创建插件实例");
        }

        var primary = instances[0];
        var info = CreatePluginInfo(primary);
        info.DisplayNameOverride = Path.GetFileNameWithoutExtension(localPath);
        info.Source = PluginSource.Installed;
        info.AssemblyPath = localPath;
        info.InstallUrl = sourceUrl;
        info.IsEnabled = true;

        for (int i = 1; i < instances.Count; i++)
        {
            info.SubPlugins.Add(instances[i]);
        }

        _plugins.Add(info);

        _setPrefFunc($"plugin_enabled_{info.PluginTypeId}", true);

        if (info.IsEnabled)
        {
            await primary.InitializeAsync();
            foreach (var sub in info.SubPlugins)
            {
                try { await sub.InitializeAsync(); } catch { }
            }
        }

        _installedPluginIds.Add(info.PluginTypeId);
        SaveInstalledIndex();

        progress?.Report(("安装完成", 100));
        return info;
    }

    /// <summary>卸载插件并删除程序集文件</summary>
    public async Task<bool> UninstallPluginAsync(string pluginTypeId)
    {
        var info = _plugins.FirstOrDefault(p => p.PluginTypeId == pluginTypeId && p.CanUninstall);
        if (info == null) return false;

        try
        {
            if (info.IsEnabled)
            {
                await info.Plugin.ShutdownAsync();
                foreach (var sub in info.SubPlugins)
                {
                    try { await sub.ShutdownAsync(); } catch { }
                }
            }
        }
        catch { }

        if (info.AssemblyPath != null)
        {
            try
            {
                File.Delete(info.AssemblyPath);
            }
            catch { }
        }

        _plugins.Remove(info);
        _installedPluginIds.Remove(pluginTypeId);
        SaveInstalledIndex();
        return true;
    }

    /// <summary>持久化已安装插件索引到 installed.json</summary>
    private void SaveInstalledIndex()
    {
        var indexPath = Path.Combine(_pluginsDir, "installed.json");
        try
        {
            var data = System.Text.Json.JsonSerializer.Serialize(
                _plugins.Where(p => p.CanUninstall).Select(p => new
                {
                    p.PluginTypeId,
                    p.AssemblyPath,
                    p.InstallUrl,
                    PluginName = p.Plugin.Name
                }));
            File.WriteAllText(indexPath, data);
        }
        catch { }
    }

    /// <summary>从 installed.json 加载已安装插件 ID 索引</summary>
    private void LoadInstalledIndex()
    {
        var indexPath = Path.Combine(_pluginsDir, "installed.json");
        if (!File.Exists(indexPath)) return;

        try
        {
            var data = System.Text.Json.JsonSerializer.Deserialize<List<InstalledPluginEntry>>(
                File.ReadAllText(indexPath));
            if (data != null)
            {
                foreach (var entry in data)
                {
                    if (entry.AssemblyPath != null && File.Exists(entry.AssemblyPath))
                    {
                        _installedPluginIds.Add(entry.PluginTypeId);
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>从 installed.json 加载已安装插件并注册</summary>
    private void LoadInstalledPlugins()
    {
        var indexPath = Path.Combine(_pluginsDir, "installed.json");
        if (!File.Exists(indexPath)) return;

        var loadedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var data = System.Text.Json.JsonSerializer.Deserialize<List<InstalledPluginEntry>>(
                File.ReadAllText(indexPath));
            if (data == null) return;

            foreach (var entry in data)
            {
                if (entry.AssemblyPath == null || !File.Exists(entry.AssemblyPath)) continue;
                if (loadedAssemblies.Contains(entry.AssemblyPath)) continue;
                loadedAssemblies.Add(entry.AssemblyPath);

                try
                {
                    var fileBytes = File.ReadAllBytes(entry.AssemblyPath);
                    var assembly = Assembly.Load(fileBytes);

                    var pluginTypes = assembly.GetTypes()
                        .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                        .ToList();

                    List<IPlugin> instances = new();
                    foreach (var type in pluginTypes)
                    {
                        if (Activator.CreateInstance(type) is IPlugin pluginInstance)
                            instances.Add(pluginInstance);
                    }

                    if (instances.Count == 0) continue;

                    var primary = instances[0];
                    var info = CreatePluginInfo(primary);
                    info.DisplayNameOverride = Path.GetFileNameWithoutExtension(entry.AssemblyPath);
                    info.Source = PluginSource.Installed;
                    info.AssemblyPath = entry.AssemblyPath;
                    info.InstallUrl = entry.InstallUrl;
                    info.IsEnabled = _getPrefFunc($"plugin_enabled_{info.PluginTypeId}");

                    for (int i = 1; i < instances.Count; i++)
                    {
                        info.SubPlugins.Add(instances[i]);
                    }

                    _plugins.Add(info);
                }
                catch
                {
                    // 插件加载失败，跳过
                }
            }
        }
        catch { }
    }

    /// <summary>根据插件类型创建 PluginInfo 元数据</summary>
    private static PluginInfo CreatePluginInfo(IPlugin plugin)
    {
        string pluginTypeId;
        PluginCategory category;
        string iconEmoji;

        if (plugin is ILyricsProviderPlugin)
        {
            pluginTypeId = $"LyricsProvider.{plugin.PluginId}";
            category = PluginCategory.LyricsProvider;
            iconEmoji = "🎵";
        }
        else if (plugin is IProtocolProviderPlugin)
        {
            pluginTypeId = $"ProtocolProvider.{plugin.PluginId}";
            category = PluginCategory.ProtocolProvider;
            iconEmoji = "🔌";
        }
        else if (plugin is ICoverProviderPlugin)
        {
            pluginTypeId = $"CoverProvider.{plugin.PluginId}";
            category = PluginCategory.CoverProvider;
            iconEmoji = "🖼️";
        }
        else if (plugin is IAudioEnhancerPlugin)
        {
            pluginTypeId = $"AudioEnhancer.{plugin.PluginId}";
            category = PluginCategory.AudioEnhancer;
            iconEmoji = "🎛️";
        }
        else if (plugin is IMenuContributorPlugin)
        {
            pluginTypeId = $"MenuContributor.{plugin.PluginId}";
            category = PluginCategory.MenuContributor;
            iconEmoji = "📋";
        }
        else
        {
            pluginTypeId = $"Other.{plugin.PluginId}";
            category = PluginCategory.Other;
            iconEmoji = "🧩";
        }

        return new PluginInfo
        {
            PluginTypeId = pluginTypeId,
            Plugin = plugin,
            IsEnabled = true,
            Description = plugin.Description,
            Category = category,
            IconEmoji = iconEmoji
        };
    }

    /// <summary>
    /// 已安装插件持久化条目
    /// </summary>
    private class InstalledPluginEntry
    {
        public string PluginTypeId { get; set; } = string.Empty;
        public string? AssemblyPath { get; set; }
        public string? InstallUrl { get; set; }
        public string? PluginName { get; set; }
    }
}
