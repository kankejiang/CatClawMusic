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

    public async Task<PluginInfo?> InstallPluginAsync(string url, IProgress<(string, int)>? progress = null)
    {
        try
        {
            progress?.Report(("正在下载插件...", 10));

            var fileName = Path.GetFileName(new Uri(url).LocalPath);
            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && !fileName.EndsWith(".catclaw-plugin", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "plugin.dll";
            }

            var localPath = Path.Combine(_pluginsDir, fileName);

            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1;

            using var remoteStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write);
            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await remoteStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;
                if (totalBytes > 0)
                {
                    var pct = (int)(50 + totalRead * 50 / totalBytes);
                    progress?.Report(("正在下载插件...", pct));
                }
            }

            progress?.Report(("正在加载插件...", 80));

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

            PluginInfo? addedInfo = null;
            foreach (var type in pluginTypes)
            {
                if (Activator.CreateInstance(type) is not IPlugin pluginInstance) continue;

                var info = CreatePluginInfo(pluginInstance);
                info.Source = PluginSource.Installed;
                info.AssemblyPath = localPath;
                info.InstallUrl = url;
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
        catch (Exception ex)
        {
            progress?.Report(($"安装失败: {ex.Message}", 100));
            return null;
        }
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
