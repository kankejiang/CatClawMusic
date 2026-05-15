using System.Reflection;
using System.Runtime.Loader;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

public class PluginManager : IPluginManager
{
    private readonly List<PluginInfo> _plugins = new();
    private readonly Func<string, bool> _getPrefFunc;
    private readonly Action<string, bool> _setPrefFunc;
    private readonly string _pluginsDir;
    private readonly Dictionary<string, AssemblyLoadContext> _loadContexts = new();
    private readonly HttpClient _httpClient = new();
    private readonly HashSet<string> _installedPluginIds = new();

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

    public List<PluginInfo> GetAllPlugins()
    {
        return _plugins.ToList();
    }

    public List<T> GetEnabledPlugins<T>() where T : IPlugin
    {
        return _plugins
            .Where(p => p.IsEnabled && p.Plugin is T)
            .Select(p => (T)p.Plugin)
            .ToList();
    }

    public bool IsPluginEnabled(string pluginTypeId)
    {
        return _plugins.FirstOrDefault(p => p.PluginTypeId == pluginTypeId)?.IsEnabled ?? false;
    }

    public void SetPluginEnabled(string pluginTypeId, bool enabled)
    {
        var plugin = _plugins.FirstOrDefault(p => p.PluginTypeId == pluginTypeId);
        if (plugin == null) return;

        plugin.IsEnabled = enabled;
        _setPrefFunc($"plugin_enabled_{pluginTypeId}", enabled);
    }

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
        }
    }

    public async Task ShutdownAllAsync()
    {
        foreach (var info in _plugins.Where(p => p.IsEnabled))
        {
            try
            {
                await info.Plugin.ShutdownAsync();
            }
            catch { }
        }
    }

    public async Task<PluginInfo?> InstallFromLocalFileAsync(string filePath, IProgress<(string, int)>? progress = null)
    {
        try
        {
            progress?.Report(("正在读取插件文件...", 10));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("未找到插件文件", filePath);

            var fileName = Path.GetFileName(filePath);
            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("仅支持 .dll 格式的插件文件");

            var destPath = Path.Combine(_pluginsDir, fileName);
            if (File.Exists(destPath))
            {
                destPath = Path.Combine(_pluginsDir,
                    $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:yyyyMMddHHmmss}.dll");
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
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
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

    private async Task<PluginInfo?> LoadAndRegisterPluginAsync(string localPath, string sourceUrl, IProgress<(string, int)>? progress)
    {
        var fileName = Path.GetFileName(localPath);

        var loadContext = new PluginLoadContext(localPath);
        var assembly = loadContext.LoadFromAssemblyPath(localPath);
        _loadContexts[fileName] = loadContext;

        var pluginTypes = assembly.GetTypes()
            .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .ToList();

        if (pluginTypes.Count == 0)
        {
            loadContext.Unload();
            _loadContexts.Remove(fileName);
            File.Delete(localPath);
            throw new InvalidOperationException("插件程序集中未找到有效的IPlugin实现");
        }

        progress?.Report(("正在初始化插件...", 85));

        PluginInfo? addedInfo = null;
        foreach (var type in pluginTypes)
        {
            if (Activator.CreateInstance(type) is not IPlugin pluginInstance) continue;

            var info = CreatePluginInfo(pluginInstance);
            info.Source = PluginSource.Installed;
            info.AssemblyPath = localPath;
            info.InstallUrl = sourceUrl;
            info.IsEnabled = true;
            _plugins.Add(info);
            addedInfo = info;

            _setPrefFunc($"plugin_enabled_{info.PluginTypeId}", true);

            if (info.IsEnabled)
            {
                await pluginInstance.InitializeAsync();
            }
        }

        if (addedInfo != null)
        {
            _installedPluginIds.Add(addedInfo.PluginTypeId);
            SaveInstalledIndex();
        }

        progress?.Report(("安装完成", 100));
        return addedInfo;
    }

    public async Task<bool> UninstallPluginAsync(string pluginTypeId)
    {
        var info = _plugins.FirstOrDefault(p => p.PluginTypeId == pluginTypeId && p.CanUninstall);
        if (info == null) return false;

        try
        {
            if (info.IsEnabled)
            {
                await info.Plugin.ShutdownAsync();
            }
        }
        catch { }

        if (info.AssemblyPath != null)
        {
            var fileName = Path.GetFileName(info.AssemblyPath);

            if (_loadContexts.TryGetValue(fileName, out var ctx))
            {
                ctx.Unload();
                _loadContexts.Remove(fileName);
            }

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

    private void LoadInstalledPlugins()
    {
        var indexPath = Path.Combine(_pluginsDir, "installed.json");
        if (!File.Exists(indexPath)) return;

        try
        {
            var data = System.Text.Json.JsonSerializer.Deserialize<List<InstalledPluginEntry>>(
                File.ReadAllText(indexPath));
            if (data == null) return;

            foreach (var entry in data)
            {
                if (entry.AssemblyPath == null || !File.Exists(entry.AssemblyPath)) continue;

                var fileName = Path.GetFileName(entry.AssemblyPath);
                if (_loadContexts.ContainsKey(fileName)) continue;

                try
                {
                    var loadContext = new PluginLoadContext(entry.AssemblyPath);
                    var assembly = loadContext.LoadFromAssemblyPath(entry.AssemblyPath);
                    _loadContexts[fileName] = loadContext;

                    var pluginTypes = assembly.GetTypes()
                        .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

                    foreach (var type in pluginTypes)
                    {
                        if (Activator.CreateInstance(type) is not IPlugin pluginInstance) continue;

                        var info = CreatePluginInfo(pluginInstance);
                        info.Source = PluginSource.Installed;
                        info.AssemblyPath = entry.AssemblyPath;
                        info.InstallUrl = entry.InstallUrl;
                        info.IsEnabled = _getPrefFunc($"plugin_enabled_{info.PluginTypeId}");
                        _plugins.Add(info);
                    }
                }
                catch
                {
                    // 插件加载失败，跳过
                }
            }
        }
        catch { }
    }

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

    private class InstalledPluginEntry
    {
        public string PluginTypeId { get; set; } = string.Empty;
        public string? AssemblyPath { get; set; }
        public string? InstallUrl { get; set; }
        public string? PluginName { get; set; }
    }

    private class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath)
            : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }
    }
}
