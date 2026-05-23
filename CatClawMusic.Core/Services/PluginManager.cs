using System.Reflection;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

public class PluginManager : IPluginManager
{
    private readonly List<PluginInfo> _plugins = new();
    private readonly Func<string, bool> _getPrefFunc;
    private readonly Action<string, bool> _setPrefFunc;
    private readonly string _pluginsDir;
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    private readonly HashSet<string> _installedPluginIds = new();
    private static readonly HashSet<string> _hostAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CatClawMusic.Core",
        "CatClawMusic.Data",
        "CatClawMusic.UI"
    };

    private static readonly string IPluginFullName = typeof(IPlugin).FullName!;
    private static readonly string ICoverProviderFullName = typeof(ICoverProviderPlugin).FullName!;
    private static readonly string ILyricsProviderFullName = typeof(ILyricsProviderPlugin).FullName!;
    private static readonly string IMenuContributorFullName = typeof(IMenuContributorPlugin).FullName!;
    private static readonly string IProtocolProviderFullName = typeof(IProtocolProviderPlugin).FullName!;
    private static readonly string IAudioEnhancerFullName = typeof(IAudioEnhancerPlugin).FullName!;

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

        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

        LoadInstalledIndex();

        foreach (var plugin in plugins)
        {
            var info = CreatePluginInfo(plugin);
            info.IsEnabled = _getPrefFunc($"plugin_enabled_{info.PluginTypeId}");
            _plugins.Add(info);
        }

        LoadInstalledPlugins();
    }

    private Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        if (name != null && _hostAssemblyNames.Contains(name))
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == name);
            if (loaded != null)
            {
                System.Diagnostics.Debug.WriteLine($"[PluginManager] AssemblyResolve: {args.Name} -> {loaded.FullName}");
                return loaded;
            }
        }
        return null;
    }

    public List<PluginInfo> GetAllPlugins()
    {
        return _plugins.ToList();
    }

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
            foreach (var sub in info.SubPlugins)
            {
                try { await sub.InitializeAsync(); } catch { }
            }
        }
    }

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

            var releasesUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

            using var request = new HttpRequestMessage(HttpMethod.Get, releasesUrl);
            request.Headers.UserAgent.ParseAdd("CatClawMusic/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github.v3+json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var releasesJson = await response.Content.ReadAsStringAsync();

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
                    $"仓库 {owner}/{repo} 的 Release 中没有找到 .dll 或 .ccp 文件。\n" +
                    "请上传编译好的插件到 Release Assets。");
            }

            progress?.Report(("正在下载插件...", 30));

            var destPath = Path.Combine(_pluginsDir, dllName ?? "plugin.dll");

            using var downloadResponse = await _httpClient.GetAsync(dllUrl);
            downloadResponse.EnsureSuccessStatusCode();
            var totalBytes = downloadResponse.Content.Headers.ContentLength ?? -1;

            using var remoteStream = await downloadResponse.Content.ReadAsStreamAsync();
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
        var fileBytes = File.ReadAllBytes(localPath);
        var assembly = Assembly.Load(fileBytes);

        System.Diagnostics.Debug.WriteLine($"[PluginManager] Loaded assembly: {assembly.FullName}");

        Type[] allTypes;
        try
        {
            allTypes = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException rtle)
        {
            allTypes = rtle.Types.Where(t => t != null).ToArray()!;
            System.Diagnostics.Debug.WriteLine($"[PluginManager] ReflectionTypeLoadException: {rtle.LoaderExceptions.Length} type(s) failed to load");
        }

        var instances = CreatePluginInstances(allTypes);

        if (instances.Count == 0)
        {
            File.Delete(localPath);
            throw new InvalidOperationException(
                "插件程序集中未找到有效的IPlugin实现。\n" +
                "可能原因：插件编译时引用了不同版本的 CatClawMusic.Core.dll。\n" +
                "请确保插件项目引用宿主的 CatClawMusic.Core.dll 而非独立副本。");
        }

        progress?.Report(("正在初始化插件...", 85));

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

    private List<IPlugin> CreatePluginInstances(Type[] allTypes)
    {
        var directTypes = allTypes
            .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .ToList();

        if (directTypes.Count > 0)
        {
            List<IPlugin> instances = new();
            foreach (var type in directTypes)
            {
                if (Activator.CreateInstance(type) is IPlugin pluginInstance)
                    instances.Add(pluginInstance);
            }
            return instances;
        }

        var nameMatchedTypes = allTypes
            .Where(t => !t.IsAbstract && !t.IsInterface
                && t.GetInterfaces().Any(i => i.FullName == IPluginFullName))
            .ToList();

        if (nameMatchedTypes.Count == 0)
            return new List<IPlugin>();

        System.Diagnostics.Debug.WriteLine($"[PluginManager] Using reflection adapter for {nameMatchedTypes.Count} plugin type(s) with embedded types");

        List<IPlugin> instances2 = new();
        foreach (var type in nameMatchedTypes)
        {
            try
            {
                var rawInstance = Activator.CreateInstance(type);
                if (rawInstance == null) continue;

                var interfaceNames = type.GetInterfaces().Select(i => i.FullName).ToHashSet();
                IPlugin wrapped;

                if (interfaceNames.Contains(ICoverProviderFullName))
                    wrapped = new CoverProviderAdapter(rawInstance);
                else if (interfaceNames.Contains(ILyricsProviderFullName))
                    wrapped = new LyricsProviderAdapter(rawInstance);
                else if (interfaceNames.Contains(IMenuContributorFullName))
                    wrapped = new MenuContributorAdapter(rawInstance);
                else if (interfaceNames.Contains(IProtocolProviderFullName))
                    wrapped = new ProtocolProviderAdapter(rawInstance);
                else if (interfaceNames.Contains(IAudioEnhancerFullName))
                    wrapped = new AudioEnhancerAdapter(rawInstance);
                else
                    wrapped = new BasicPluginAdapter(rawInstance);

                instances2.Add(wrapped);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PluginManager] Failed to create wrapper for {type.FullName}: {ex.Message}");
            }
        }
        return instances2;
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

                    Type[] allTypes;
                    try
                    {
                        allTypes = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException rtle)
                    {
                        allTypes = rtle.Types.Where(t => t != null).ToArray()!;
                    }

                    var instances = CreatePluginInstances(allTypes);
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

    #region Reflection Adapters

    private static object? ConvertType(object? value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsAssignableFrom(value.GetType())) return value;
        try
        {
            var json = JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize(json, targetType);
        }
        catch
        {
            return value;
        }
    }

    private static T? ConvertType<T>(object? value) where T : class
    {
        if (value == null) return null;
        if (value is T t) return t;
        try
        {
            var json = JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<object?> InvokeAsyncMethod(object target, string methodName, params object?[]? args)
    {
        var method = target.GetType().GetMethod(methodName,
            BindingFlags.Public | BindingFlags.Instance,
            null,
            args?.Select(a => a?.GetType() ?? typeof(object)).ToArray() ?? Type.EmptyTypes,
            null);

        if (method == null)
        {
            method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        }

        if (method == null) return null;

        var result = method.Invoke(target, args);
        if (result is Task task)
        {
            await task;
            if (task.GetType().IsGenericType)
            {
                var resultProp = task.GetType().GetProperty("Result");
                return resultProp?.GetValue(task);
            }
            return null;
        }
        return result;
    }

    private class BasicPluginAdapter : IPlugin
    {
        protected readonly object _target;
        protected readonly Type _targetType;

        public BasicPluginAdapter(object target)
        {
            _target = target;
            _targetType = target.GetType();
        }

        public string PluginId => (string?)_targetType.GetProperty("PluginId")?.GetValue(_target) ?? "";
        public string Name => (string?)_targetType.GetProperty("Name")?.GetValue(_target) ?? "";
        public string Version => (string?)_targetType.GetProperty("Version")?.GetValue(_target) ?? "";
        public string Author => (string?)_targetType.GetProperty("Author")?.GetValue(_target) ?? "";
        public string Description => (string?)_targetType.GetProperty("Description")?.GetValue(_target) ?? "";
        public List<string> Capabilities => (List<string>?)_targetType.GetProperty("Capabilities")?.GetValue(_target) ?? new();

        public Task InitializeAsync() => (Task)_targetType.GetMethod("InitializeAsync")!.Invoke(_target, null)!;
        public Task ShutdownAsync() => (Task)_targetType.GetMethod("ShutdownAsync")!.Invoke(_target, null)!;
    }

    private class CoverProviderAdapter : BasicPluginAdapter, ICoverProviderPlugin
    {
        public CoverProviderAdapter(object target) : base(target) { }

        public bool IsAvailable => (bool?)_targetType.GetProperty("IsAvailable")?.GetValue(_target) ?? false;

        public async Task<byte[]?> GetCoverAsync(Song song)
        {
            var method = _targetType.GetMethod("GetCoverAsync");
            if (method == null) return null;

            var paramType = method.GetParameters().FirstOrDefault()?.ParameterType;
            object?[]? invokeArgs;
            if (paramType != null && paramType.FullName == typeof(Song).FullName)
            {
                var converted = ConvertType(song, paramType);
                invokeArgs = new[] { converted };
            }
            else
            {
                invokeArgs = new object?[] { song };
            }

            var result = await InvokeAsyncMethod(_target, "GetCoverAsync", invokeArgs);
            return result as byte[];
        }
    }

    private class LyricsProviderAdapter : BasicPluginAdapter, ILyricsProviderPlugin
    {
        public LyricsProviderAdapter(object target) : base(target) { }

        public bool IsAvailable => (bool?)_targetType.GetProperty("IsAvailable")?.GetValue(_target) ?? false;

        public async Task<LrcLyrics?> GetLyricsAsync(Song song)
        {
            var method = _targetType.GetMethod("GetLyricsAsync");
            if (method == null) return null;

            var paramType = method.GetParameters().FirstOrDefault()?.ParameterType;
            object?[]? invokeArgs;
            if (paramType != null && paramType.FullName == typeof(Song).FullName)
            {
                var converted = ConvertType(song, paramType);
                invokeArgs = new[] { converted };
            }
            else
            {
                invokeArgs = new object?[] { song };
            }

            var result = await InvokeAsyncMethod(_target, "GetLyricsAsync", invokeArgs);
            return ConvertType<LrcLyrics>(result);
        }
    }

    private class MenuContributorAdapter : BasicPluginAdapter, IMenuContributorPlugin
    {
        public MenuContributorAdapter(object target) : base(target) { }

        public List<MenuItemEntry> GetMenuItems(Song song)
        {
            var method = _targetType.GetMethod("GetMenuItems");
            if (method == null) return new();

            var paramType = method.GetParameters().FirstOrDefault()?.ParameterType;
            object?[]? invokeArgs;
            if (paramType != null && paramType.FullName == typeof(Song).FullName)
            {
                var converted = ConvertType(song, paramType);
                invokeArgs = new[] { converted };
            }
            else
            {
                invokeArgs = new object?[] { song };
            }

            var result = method.Invoke(_target, invokeArgs);
            if (result is List<MenuItemEntry> typed) return typed;

            if (result is System.Collections.IList list)
            {
                var entries = new List<MenuItemEntry>();
                foreach (var item in list)
                {
                    var converted = ConvertType<MenuItemEntry>(item);
                    if (converted != null) entries.Add(converted);
                }
                return entries;
            }

            return new();
        }

        public async Task OnMenuItemClicked(int itemId, Song song, object fragment)
        {
            var method = _targetType.GetMethod("OnMenuItemClicked");
            if (method == null) return;

            var parameters = method.GetParameters();
            var args = new object?[3];
            args[0] = itemId;

            if (parameters.Length > 1 && parameters[1].ParameterType.FullName == typeof(Song).FullName)
                args[1] = ConvertType(song, parameters[1].ParameterType);
            else
                args[1] = song;

            args[2] = fragment;

            var result = method.Invoke(_target, args);
            if (result is Task task) await task;
        }
    }

    private class ProtocolProviderAdapter : BasicPluginAdapter, IProtocolProviderPlugin
    {
        public ProtocolProviderAdapter(object target) : base(target) { }

        public string ProtocolName => (string?)_targetType.GetProperty("ProtocolName")?.GetValue(_target) ?? "";

        public async Task<List<RemoteFile>> ListFilesAsync(string path)
        {
            var result = await InvokeAsyncMethod(_target, "ListFilesAsync", path);
            if (result is List<RemoteFile> typed) return typed;

            if (result is System.Collections.IList list)
            {
                var files = new List<RemoteFile>();
                foreach (var item in list)
                {
                    var converted = ConvertType<RemoteFile>(item);
                    if (converted != null) files.Add(converted);
                }
                return files;
            }

            return new();
        }

        public async Task<Stream> OpenReadAsync(string filePath)
        {
            var result = await InvokeAsyncMethod(_target, "OpenReadAsync", filePath);
            return (Stream)result!;
        }

        public async Task<bool> TestConnectionAsync(ConnectionProfile profile)
        {
            var method = _targetType.GetMethod("TestConnectionAsync");
            if (method == null) return false;

            var paramType = method.GetParameters().FirstOrDefault()?.ParameterType;
            object?[]? invokeArgs;
            if (paramType != null && paramType.FullName == typeof(ConnectionProfile).FullName)
            {
                var converted = ConvertType(profile, paramType);
                invokeArgs = new[] { converted };
            }
            else
            {
                invokeArgs = new object?[] { profile };
            }

            var result = await InvokeAsyncMethod(_target, "TestConnectionAsync", invokeArgs);
            return result is bool b && b;
        }
    }

    private class AudioEnhancerAdapter : BasicPluginAdapter, IAudioEnhancerPlugin
    {
        public AudioEnhancerAdapter(object target) : base(target) { }

        public bool IsEnabled
        {
            get => (bool?)_targetType.GetProperty("IsEnabled")?.GetValue(_target) ?? false;
            set
            {
                var prop = _targetType.GetProperty("IsEnabled");
                if (prop?.CanWrite == true) prop.SetValue(_target, value);
            }
        }

        public float[] ProcessSamples(float[] samples, int sampleRate, int channels)
        {
            var method = _targetType.GetMethod("ProcessSamples");
            if (method == null) return samples;
            var result = method.Invoke(_target, new object[] { samples, sampleRate, channels });
            return result as float[] ?? samples;
        }

        public void Reset()
        {
            var method = _targetType.GetMethod("Reset");
            method?.Invoke(_target, null);
        }
    }

    #endregion

    private class InstalledPluginEntry
    {
        public string PluginTypeId { get; set; } = string.Empty;
        public string? AssemblyPath { get; set; }
        public string? InstallUrl { get; set; }
        public string? PluginName { get; set; }
    }
}
