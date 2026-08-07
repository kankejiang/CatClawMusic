using System.Reflection;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 插件管理器 —— CatClawMusic 插件系统的核心控制器。
/// <para>
/// 职责概述：
/// <list type="bullet">
///   <item>管理插件的完整生命周期：发现、加载、启用/禁用、初始化、关闭、卸载</item>
///   <item>支持两种安装方式：从本地文件（.ccp）安装 和 从 GitHub Release 下载安装</item>
///   <item>实现「反射适配器模式」：当插件 DLL 引用了不同版本的宿主程序集时，
///         通过 FullName 匹配接口并使用反射代理调用，避免类型转换失败</item>
///   <item>实现「两级匹配策略」：优先使用宿主端 IPlugin 接口直接匹配（isAssignableFrom），
///         若失败则退化为按接口全限定名（FullName）反射匹配并包装为适配器</item>
///   <item>持久化已安装插件索引（installed.json），确保应用重启后能自动恢复已安装插件</item>
///   <item>注册全局 AssemblyResolve 事件，解决插件加载时对宿主程序集的依赖解析问题</item>
/// </list>
/// </para>
/// <para>
/// 适配器体系（内部类）：
/// <list type="bullet">
///   <item><see cref="BasicPluginAdapter"/> —— 基础适配器，代理 IPlugin 核心属性和方法</item>
///   <item><see cref="CoverProviderAdapter"/> —— 封面提供者适配器</item>
///   <item><see cref="LyricsProviderAdapter"/> —— 歌词提供者适配器</item>
///   <item><see cref="MenuContributorAdapter"/> —— 菜单贡献者适配器</item>
///   <item><see cref="ProtocolProviderAdapter"/> —— 协议提供者适配器</item>
///   <item><see cref="AudioEnhancerAdapter"/> —— 音频增强器适配器</item>
/// </list>
/// </para>
/// </summary>
public class PluginManager : IPluginManager
{
    /// <summary>
    /// 所有已注册插件的列表，包括内置插件和动态安装的插件
    /// </summary>
    private readonly List<PluginInfo> _plugins = new();

    /// <summary>
    /// 读取插件启用状态的委托，键格式为 "plugin_enabled_{PluginTypeId}"
    /// </summary>
    private readonly Func<string, bool> _getPrefFunc;

    /// <summary>
    /// 持久化插件启用状态的委托，键格式为 "plugin_enabled_{PluginTypeId}"
    /// </summary>
    private readonly Action<string, bool> _setPrefFunc;

    /// <summary>
    /// 插件文件存放目录，用于存储动态安装的 .ccp 插件包和 installed.json 索引
    /// </summary>
    private readonly string _pluginsDir;

    /// <summary>
    /// HTTP 客户端，用于从 GitHub Release 下载插件文件，超时时间为 30 秒
    /// </summary>
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// 已安装插件的 PluginTypeId 集合，用于快速判断某个插件是否为动态安装的
    /// </summary>
    private readonly HashSet<string> _installedPluginIds = new();

    /// <summary>
    /// IPlugin 接口的全限定名，用于反射适配器模式中的第二级匹配（按名称匹配）
    /// </summary>
    private static readonly string IPluginFullName = typeof(IPlugin).FullName!;

    /// <summary>
    /// ICoverProviderPlugin 接口的全限定名，用于反射匹配封面提供者插件
    /// </summary>
    private static readonly string ICoverProviderFullName = typeof(ICoverProviderPlugin).FullName!;

    /// <summary>
    /// ILyricsProviderPlugin 接口的全限定名，用于反射匹配歌词提供者插件
    /// </summary>
    private static readonly string ILyricsProviderFullName = typeof(ILyricsProviderPlugin).FullName!;

    /// <summary>
    /// IMenuContributorPlugin 接口的全限定名，用于反射匹配菜单贡献者插件
    /// </summary>
    private static readonly string IMenuContributorFullName = typeof(IMenuContributorPlugin).FullName!;

    /// <summary>
    /// IProtocolProviderPlugin 接口的全限定名，用于反射匹配协议提供者插件
    /// </summary>
    private static readonly string IProtocolProviderFullName = typeof(IProtocolProviderPlugin).FullName!;

    /// <summary>
    /// IAudioEnhancerPlugin 接口的全限定名，用于反射匹配音频增强器插件
    /// </summary>
    private static readonly string IAudioEnhancerFullName = typeof(IAudioEnhancerPlugin).FullName!;

    /// <summary>
    /// IOnlineMusicPlugin 接口的全限定名，用于反射匹配在线音乐音源插件
    /// </summary>
    private static readonly string IOnlineMusicFullName = typeof(IOnlineMusicPlugin).FullName!;

    /// <summary>
    /// IViewContributorPlugin 接口的全限定名，用于反射匹配视图贡献者插件（提供完整页面入口）
    /// </summary>
    private static readonly string IViewContributorFullName = typeof(IViewContributorPlugin).FullName!;

    /// <summary>
    /// IThemeProviderPlugin 接口的全限定名，用于反射匹配主题提供者插件
    /// </summary>
    private static readonly string IThemeProviderFullName = typeof(IThemeProviderPlugin).FullName!;

    /// <summary>
    /// IPlayerPagePlugin 接口的全限定名，用于反射匹配播放页提供者插件
    /// </summary>
    private static readonly string IPlayerPageFullName = typeof(IPlayerPagePlugin).FullName!;

    /// <summary>
    /// IAudioVisualizerPlugin 接口的全限定名，用于反射匹配音频可视化插件
    /// </summary>
    private static readonly string IAudioVisualizerFullName = typeof(IAudioVisualizerPlugin).FullName!;

    /// <summary>
    /// IPluginConfigurable 接口的全限定名，用于反射匹配可配置插件
    /// </summary>
    private static readonly string IConfigurableFullName = typeof(IPluginConfigurable).FullName!;

    /// <summary>
    /// 插件管理器构造函数。完成初始化流程：
    /// <list type="number">
    ///   <item>验证并保存偏好读写委托和插件目录</item>
    ///   <item>确保插件目录存在</item>
    ///   <item>注册全局程序集解析事件处理器</item>
    ///   <item>加载已安装插件索引</item>
    ///   <item>注册内置插件（从依赖注入传入），并恢复其启用状态</item>
    ///   <item>从索引文件恢复动态安装的插件</item>
    /// </list>
    /// </summary>
    /// <param name="plugins">由依赖注入提供的内置插件实例集合</param>
    /// <param name="getPrefFunc">读取偏好设置的委托，用于获取插件启用状态</param>
    /// <param name="setPrefFunc">写入偏好设置的委托，用于持久化插件启用状态</param>
    /// <param name="pluginsDir">插件文件存储目录的绝对路径</param>
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

        // 注册全局程序集解析事件：当插件 DLL 加载时请求宿主程序集，返回当前 AppDomain 已加载的版本
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

        // 加载已安装插件索引到内存
        LoadInstalledIndex();

        // 注册内置插件并恢复启用状态
        foreach (var plugin in plugins)
        {
            var info = CreatePluginInfo(plugin);
            info.IsEnabled = _getPrefFunc($"plugin_enabled_{info.PluginTypeId}");
            _plugins.Add(info);
        }

        // 从索引文件恢复动态安装的插件
        LoadInstalledPlugins();
    }

    /// <summary>
    /// 全局程序集解析事件处理器。
    /// <para>
    /// 当插件 DLL 在运行时通过反射引用了宿主程序集（CatClawMusic.Core/Data/UI），
    /// 但由于版本号不同导致默认解析失败时，此处理器会返回当前 AppDomain 中
    /// 已加载的对应程序集，从而解决版本冲突问题。
    /// </para>
    /// <para>
    /// 这是因为插件编译时可能引用了不同版本的宿主 DLL，
    /// 而运行时宿主已经加载了自己的版本，CLR 默认不会自动匹配。
    /// </para>
    /// </summary>
    /// <param name="sender">事件发送者</param>
    /// <param name="args">解析事件参数，包含请求的程序集名称</param>
    /// <returns>已加载的宿主程序集，或不属于宿主程序集时返回 null</returns>
    private Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        try
        {
            var name = new AssemblyName(args.Name).Name;
            if (string.IsNullOrWhiteSpace(name)) return null;

            // 泛化解析：返回当前 AppDomain 已加载的同名程序集。
            // 覆盖宿主程序集（CatClawMusic.Core 等）以及插件引用的框架程序集
            // （Microsoft.Maui.Controls、CommunityToolkit.Mvvm、
            //  Microsoft.Extensions.DependencyInjection 等），确保插件加载时
            // 所有依赖都能解析到宿主进程内已加载的版本。
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
            if (loaded != null)
            {
                Log.Debug("PluginManager", $"[PluginManager] AssemblyResolve: {args.Name} -> {loaded.FullName}");
                return loaded;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("PluginManager", $"[PluginManager] AssemblyResolve 异常: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 获取所有已注册插件的列表（包括内置和动态安装的）
    /// </summary>
    /// <returns>插件信息列表的副本</returns>
    public List<PluginInfo> GetAllPlugins()
    {
        return _plugins.ToList();
    }

    /// <summary>
    /// 获取所有已启用的、实现了指定接口类型的插件实例。
    /// <para>
    /// 搜索范围包括主插件和子插件（SubPlugins）。
    /// 例如调用 GetEnabledPlugins&lt;ILyricsProviderPlugin&gt;() 可获取所有已启用的歌词提供者。
    /// </para>
    /// </summary>
    /// <typeparam name="T">目标插件接口类型（必须继承自 IPlugin）</typeparam>
    /// <returns>实现了指定接口且已启用的插件实例列表</returns>
    public List<T> GetEnabledPlugins<T>() where T : IPlugin
    {
        var result = new List<T>();
        foreach (var p in _plugins)
        {
            if (!p.IsEnabled) continue;
            // 检查主插件是否匹配目标接口
            if (p.Plugin is T t)
                result.Add(t);
            // 检查子插件是否匹配目标接口
            foreach (var sub in p.SubPlugins)
            {
                if (sub is T st)
                    result.Add(st);
            }
        }
        return result;
    }

    /// <summary>
    /// 判断指定插件是否处于启用状态
    /// </summary>
    /// <param name="pluginTypeId">插件的类型标识，格式为 "{Category}.{PluginId}"</param>
    /// <returns>插件已启用返回 true，未找到或已禁用返回 false</returns>
    public bool IsPluginEnabled(string pluginTypeId)
    {
        return _plugins.FirstOrDefault(p => p.PluginTypeId == pluginTypeId)?.IsEnabled ?? false;
    }

    /// <summary>
    /// 设置插件的启用/禁用状态，并持久化到偏好设置
    /// </summary>
    /// <param name="pluginTypeId">插件的类型标识</param>
    /// <param name="enabled">true 为启用，false 为禁用</param>
    public void SetPluginEnabled(string pluginTypeId, bool enabled)
    {
        var plugin = _plugins.FirstOrDefault(p => p.PluginTypeId == pluginTypeId);
        if (plugin == null) return;

        plugin.IsEnabled = enabled;
        _setPrefFunc($"plugin_enabled_{pluginTypeId}", enabled);
    }

    /// <summary>
    /// 异步初始化所有已启用的插件。
    /// <para>
    /// 依次调用每个已启用主插件和子插件的 InitializeAsync 方法。
    /// 若某个插件初始化失败，将自动将其设为禁用状态（主插件）或静默忽略（子插件）。
    /// </para>
    /// </summary>
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
                // 主插件初始化失败则自动禁用
                info.IsEnabled = false;
            }
            foreach (var sub in info.SubPlugins)
            {
                // 子插件初始化失败则静默忽略
                try { await sub.InitializeAsync(); } catch (Exception ex) { Log.Debug("PluginManager", $"子插件初始化失败: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// 异步关闭所有已启用的插件。
    /// <para>
    /// 依次调用每个已启用主插件和子插件的 ShutdownAsync 方法。
    /// 关闭过程中的异常将被静默捕获，确保不会影响其他插件的关闭。
    /// </para>
    /// </summary>
    public async Task ShutdownAllAsync()
    {
        foreach (var info in _plugins.Where(p => p.IsEnabled))
        {
            try { await info.Plugin.ShutdownAsync(); } catch (Exception ex) { Log.Debug("PluginManager", $"主插件关闭失败: {ex.Message}"); }
            foreach (var sub in info.SubPlugins)
            {
                try { await sub.ShutdownAsync(); } catch (Exception ex) { Log.Debug("PluginManager", $"子插件关闭失败: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// 从本地文件安装插件。
    /// <para>
    /// 支持的文件格式：.ccp（CatClawMusic 插件包，内容为插件程序集）。
    /// 安装流程：验证文件 → 复制到插件目录 → 加载并注册插件。
    /// 若目标目录已存在同名文件，会自动添加时间戳后缀避免覆盖。
    /// </para>
    /// </summary>
    /// <param name="filePath">插件文件的本地路径</param>
    /// <param name="progress">进度报告器，报告 (描述文本, 百分比) 元组</param>
    /// <returns>安装成功返回 PluginInfo，失败返回 null</returns>
    public async Task<PluginInfo?> InstallFromLocalFileAsync(string filePath, IProgress<(string, int)>? progress = null)
    {
        try
        {
            progress?.Report(("正在读取插件文件...", 10));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("未找到插件文件", filePath);

            var fileName = Path.GetFileName(filePath);
            if (!fileName.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase))
            {
                // Android 文件选择器把所选文件复制到缓存目录时，文件名可能丢失扩展名
                // 或变成随机名；此时校验文件内容（PE 程序集 MZ 头）而非文件名。
                if (!IsAssemblyFile(filePath))
                    throw new InvalidOperationException("仅支持 .ccp 格式的插件文件");
                // 内容合法但扩展名缺失：复制时规范化补上 .ccp，保证后续按插件包处理
                fileName = Path.GetFileNameWithoutExtension(fileName) + ".ccp";
            }

            var destPath = Path.Combine(_pluginsDir, fileName);
            // 若目标路径已存在同名文件，添加时间戳后缀避免覆盖
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
            // 记录完整异常信息（含类型与堆栈），便于诊断 Android 端安装失败的真实原因
            Log.Debug("PluginManager", $"[PluginManager] InstallFromLocalFileAsync 失败: 文件={filePath} 异常={ex.GetType().Name}: {ex.Message}\n{ex}");
            progress?.Report(($"安装失败: {ex.Message}", 100));
            // 重新抛出，让调用方能向用户展示具体失败原因（而非笼统的"格式不受支持"）
            throw;
        }
    }

    /// <summary>
    /// 检查文件是否为有效的 PE 程序集（.ccp 插件包本质就是托管程序集）。
    /// 用于 Android 文件选择器缓存导致文件名丢失扩展名时的内容校验。
    /// </summary>
    private static bool IsAssemblyFile(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            if (fs.Length < 2) return false;
            // 判断 DOS 头 MZ 标记（0x4D 0x5A）
            return fs.ReadByte() == 0x4D && fs.ReadByte() == 0x5A;
        }
        catch { return false; }
    }

    /// <summary>
    /// 从 GitHub 仓库的最新 Release 安装插件。
    /// <para>
    /// 安装流程：
    /// <list type="number">
    ///   <item>解析 GitHub 仓库 URL，提取 owner 和 repo 名称</item>
    ///   <item>调用 GitHub API 获取最新 Release 信息</item>
    ///   <item>在 Release Assets 中查找 .ccp 文件</item>
    ///   <item>下载插件文件到本地插件目录（支持进度回调）</item>
    ///   <item>加载并注册插件</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="repoUrl">GitHub 仓库地址，格式为 https://github.com/用户名/仓库名</param>
    /// <param name="progress">进度报告器，报告 (描述文本, 百分比) 元组</param>
    /// <returns>安装成功返回 PluginInfo，失败返回 null</returns>
    public async Task<PluginInfo?> InstallFromGitHubAsync(string repoUrl, IProgress<(string, int)>? progress = null)
    {
        try
        {
            progress?.Report(("正在解析仓库地址...", 5));

            // 解析 GitHub URL，提取 owner 和 repo
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

            // 调用 GitHub API 获取最新 Release
            var releasesUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

            using var request = new HttpRequestMessage(HttpMethod.Get, releasesUrl);
            request.Headers.UserAgent.ParseAdd("CatClawMusic/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github.v3+json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var releasesJson = await response.Content.ReadAsStringAsync();

            using var doc = System.Text.Json.JsonDocument.Parse(releasesJson);
            var root = doc.RootElement;

            // 检查 Release 是否包含附件
            if (!root.TryGetProperty("assets", out var assets) || assets.GetArrayLength() == 0)
            {
                throw new InvalidOperationException(
                    $"仓库 {owner}/{repo} 的最新 Release 没有包含附件。\n" +
                    "请先在 GitHub 上创建 Release 并上传编译好的 .ccp 插件包。\n" +
                    "或使用「从本地安装」导入已编译的插件。");
            }

            // 在 Release Assets 中查找 .ccp 文件（客户端统一只认 .ccp 格式）
            string? dllUrl = null;
            string? dllName = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase))
                {
                    dllUrl = asset.GetProperty("browser_download_url").GetString();
                    dllName = name;
                    break;
                }
            }

            if (dllUrl == null)
            {
                throw new InvalidOperationException(
                    $"仓库 {owner}/{repo} 的 Release 中没有找到 .ccp 插件文件。\n" +
                    "请上传编译好的插件（.ccp 格式）到 Release Assets。");
            }

            progress?.Report(("正在下载插件...", 30));

            // 下载插件文件，支持进度回调
            var destPath = Path.Combine(_pluginsDir, dllName ?? "plugin.ccp");

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
                    // 下载进度映射到 30%~70% 区间
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

    /// <summary>
    /// 加载并注册插件的内部方法。
    /// <para>
    /// 这是本地安装和 GitHub 安装的共享逻辑入口，执行以下步骤：
    /// <list type="number">
    ///   <item>读取 DLL 字节并加载程序集</item>
    ///   <item>提取程序集中的所有类型（处理 ReflectionTypeLoadException）</item>
    ///   <item>使用两级匹配策略创建插件实例</item>
    ///   <item>若未找到有效插件则删除文件并抛出异常</item>
    ///   <item>构建 PluginInfo 并注册到插件列表</item>
    ///   <item>异步初始化插件</item>
    ///   <item>更新已安装插件索引</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="localPath">插件 DLL 的本地路径</param>
    /// <param name="sourceUrl">插件来源 URL（本地安装时为文件路径，GitHub 安装时为仓库 URL）</param>
    /// <param name="progress">进度报告器</param>
    /// <returns>注册成功返回 PluginInfo，失败返回 null</returns>
    private async Task<PluginInfo?> LoadAndRegisterPluginAsync(string localPath, string sourceUrl, IProgress<(string, int)>? progress)
    {
        // 以字节方式加载程序集，避免锁定文件
        var fileBytes = File.ReadAllBytes(localPath);
        var assembly = Assembly.Load(fileBytes);

        Log.Debug("PluginManager", $"[PluginManager] Loaded assembly: {assembly.FullName}");

        // 提取程序集中的所有类型，处理部分类型加载失败的情况
        Type[] allTypes;
        try
        {
            allTypes = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException rtle)
        {
            // 当某些类型无法加载时，只取成功加载的类型
            allTypes = rtle.Types.Where(t => t != null).ToArray()!;
            var details = string.Join(" | ", rtle.LoaderExceptions
                .Where(e => e != null)
                .Select(e => e!.Message)
                .Take(5));
            Log.Debug("PluginManager", $"[PluginManager] ReflectionTypeLoadException: {rtle.LoaderExceptions.Length} type(s) failed to load: {details}");
        }

        // 使用两级匹配策略创建插件实例
        var instances = CreatePluginInstances(allTypes);

        if (instances.Count == 0)
        {
            // 未找到有效插件，清理已复制的文件
            File.Delete(localPath);
            throw new InvalidOperationException(
                "插件程序集中未找到有效的IPlugin实现。\n" +
                "可能原因：插件编译时引用了不同版本的 CatClawMusic.Core.dll，\n" +
                "或宿主 Release 构建裁剪导致插件引用的类型被移除（需关闭裁剪）。\n" +
                "请确保插件项目引用宿主的 CatClawMusic.Core.dll 而非独立副本。\n" +
                "详细原因请查看诊断日志（debug.log）中的 ReflectionTypeLoadException 记录。");
        }

        progress?.Report(("正在初始化插件...", 85));

        // 第一个实例作为主插件，其余作为子插件
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

        // 异步初始化主插件和子插件
        if (info.IsEnabled)
        {
            await primary.InitializeAsync();
            foreach (var sub in info.SubPlugins)
            {
                try { await sub.InitializeAsync(); } catch (Exception ex) { Log.Debug("PluginManager", $"子插件初始化失败: {ex.Message}"); }
            }
        }

        // 更新已安装插件索引
        _installedPluginIds.Add(info.PluginTypeId);
        SaveInstalledIndex();

        progress?.Report(("安装完成", 100));
        return info;
    }

    /// <summary>
    /// 使用两级匹配策略从程序集类型中创建插件实例。
    /// <para>
    /// 两级匹配策略：
    /// <list type="number">
    ///   <item>
    ///     <b>第一级：直接类型匹配</b> —— 使用 typeof(IPlugin).IsAssignableFrom 检查，
    ///     适用于插件编译时引用了与宿主相同版本的 CatClawMusic.Core.dll 的情况。
    ///     此级匹配成功时，可直接将实例强制转换为 IPlugin 接口。
    ///   </item>
    ///   <item>
    ///     <b>第二级：全限定名匹配 + 反射适配器</b> —— 当第一级匹配失败时，
    ///     通过比较接口的 FullName（全限定名）来识别实现了特定接口的类型。
    ///     匹配成功后，根据接口类型选择对应的反射适配器进行包装，
    ///     适配器内部通过反射调用目标方法，绕过类型系统的不兼容问题。
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// 第二级匹配的必要性：当插件 DLL 引用了不同版本的宿主程序集时，
    /// 即使接口定义完全相同，CLR 也会将它们视为不同类型，导致 isAssignableFrom 返回 false。
    /// 但接口的 FullName 在不同版本间保持一致，因此可以通过名称匹配来识别。
    /// </para>
    /// </summary>
    /// <param name="allTypes">程序集中提取的所有类型数组</param>
    /// <returns>成功创建的插件实例列表</returns>
    private List<IPlugin> CreatePluginInstances(Type[] allTypes)
    {
        // 第一级匹配：直接类型匹配（isAssignableFrom）
        var directTypes = allTypes
            .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .ToList();

        if (directTypes.Count > 0)
        {
            List<IPlugin> instances = new();
            foreach (var type in directTypes)
            {
                try
                {
                    if (Activator.CreateInstance(type) is IPlugin pluginInstance)
                        instances.Add(pluginInstance);
                }
                catch (Exception ex)
                {
                    // 单个类型创建失败（如实现的接口在宿主程序集中无法解析）不中断整个安装，
                    // 记录诊断日志便于定位问题。
                    Log.Debug("PluginManager", $"[PluginManager] 创建插件实例失败 {type.FullName}: {ex.GetType().Name}: {ex.Message}");
                }
            }
            return instances;
        }

        // 第二级匹配：全限定名匹配 + 反射适配器
        var nameMatchedTypes = allTypes
            .Where(t => !t.IsAbstract && !t.IsInterface
                && t.GetInterfaces().Any(i => i.FullName == IPluginFullName))
            .ToList();

        if (nameMatchedTypes.Count == 0)
            return new List<IPlugin>();

        Log.Debug("PluginManager", $"[PluginManager] Using reflection adapter for {nameMatchedTypes.Count} plugin type(s) with embedded types");

        // 根据接口类型选择对应的反射适配器进行包装
        List<IPlugin> instances2 = new();
        foreach (var type in nameMatchedTypes)
        {
            try
            {
                var rawInstance = Activator.CreateInstance(type);
                if (rawInstance == null) continue;

                // 获取该类型实现的所有接口的全限定名
                var interfaceNames = type.GetInterfaces().Select(i => i.FullName).ToHashSet();

                // 一个插件类型可同时实现多个宿主接口（如 IOnlineMusicPlugin + IViewContributorPlugin），
                // 为每个已实现的接口创建对应适配器：第一个作为主插件，其余作为子插件。
                // 这样 GetEnabledPlugins<每个接口>() 都能找到对应能力，避免接口链丢失。
                var wrappers = new List<IPlugin>();
                if (interfaceNames.Contains(ICoverProviderFullName))
                    wrappers.Add(new CoverProviderAdapter(rawInstance));
                if (interfaceNames.Contains(ILyricsProviderFullName))
                    wrappers.Add(new LyricsProviderAdapter(rawInstance));
                if (interfaceNames.Contains(IMenuContributorFullName))
                    wrappers.Add(new MenuContributorAdapter(rawInstance));
                if (interfaceNames.Contains(IProtocolProviderFullName))
                    wrappers.Add(new ProtocolProviderAdapter(rawInstance));
                if (interfaceNames.Contains(IAudioEnhancerFullName))
                    wrappers.Add(new AudioEnhancerAdapter(rawInstance));
                if (interfaceNames.Contains(IOnlineMusicFullName))
                    wrappers.Add(new OnlineMusicAdapter(rawInstance));
                if (interfaceNames.Contains(IViewContributorFullName))
                    wrappers.Add(new ViewContributorAdapter(rawInstance));
                if (interfaceNames.Contains(IThemeProviderFullName))
                    wrappers.Add(new ThemeProviderAdapter(rawInstance));
                if (interfaceNames.Contains(IPlayerPageFullName))
                    wrappers.Add(new PlayerPageAdapter(rawInstance));
                if (interfaceNames.Contains(IAudioVisualizerFullName))
                    wrappers.Add(new AudioVisualizerAdapter(rawInstance));
                if (interfaceNames.Contains(IConfigurableFullName))
                    wrappers.Add(new ConfigurableAdapter(rawInstance));
                if (wrappers.Count == 0)
                    wrappers.Add(new BasicPluginAdapter(rawInstance));

                instances2.AddRange(wrappers);
            }
            catch (Exception ex)
            {
                Log.Debug("PluginManager", $"[PluginManager] Failed to create wrapper for {type.FullName}: {ex.Message}");
            }
        }
        return instances2;
    }

    /// <summary>
    /// 卸载指定插件。
    /// <para>
    /// 卸载流程：
    /// <list type="number">
    ///   <item>验证插件存在且可卸载（CanUninstall 为 true）</item>
    ///   <item>若插件已启用，先调用 ShutdownAsync 关闭插件</item>
    ///   <item>删除插件的 DLL 文件</item>
    ///   <item>从插件列表和已安装索引中移除</item>
    ///   <item>持久化更新后的索引</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="pluginTypeId">要卸载的插件类型标识</param>
    /// <returns>卸载成功返回 true，插件不存在或不可卸载返回 false</returns>
    public async Task<bool> UninstallPluginAsync(string pluginTypeId)
    {
        var info = _plugins.FirstOrDefault(p => p.PluginTypeId == pluginTypeId && p.CanUninstall);
        if (info == null) return false;

        // 关闭已启用的插件
        try
        {
            if (info.IsEnabled)
            {
                await info.Plugin.ShutdownAsync();
                foreach (var sub in info.SubPlugins)
                {
                    try { await sub.ShutdownAsync(); } catch (Exception ex) { Log.Debug("PluginManager", $"子插件关闭失败: {ex.Message}"); }
                }
            }
        }
        catch (Exception ex) { Log.Debug("PluginManager", $"卸载时关闭插件失败: {ex.Message}"); }

        // 删除插件 DLL 文件
        if (info.AssemblyPath != null)
        {
            try
            {
                File.Delete(info.AssemblyPath);
            }
            catch (Exception ex) { Log.Debug("PluginManager", $"删除插件 DLL 失败: {ex.Message}"); }
        }

        // 从内存和索引中移除
        _plugins.Remove(info);
        _installedPluginIds.Remove(pluginTypeId);
        SaveInstalledIndex();
        return true;
    }

    /// <summary>
    /// 将已安装插件索引持久化到 installed.json 文件。
    /// <para>
    /// 仅保存可卸载的插件（CanUninstall 为 true），即动态安装的插件。
    /// 内置插件不需要保存，因为它们由依赖注入自动注册。
    /// </para>
    /// </summary>
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
        catch (Exception ex) { Log.Debug("PluginManager", $"保存插件索引失败: {ex.Message}"); }
    }

    /// <summary>
    /// 从 installed.json 文件加载已安装插件索引到内存。
    /// <para>
    /// 仅将索引中的 PluginTypeId 加载到 _installedPluginIds 集合，
    /// 用于后续判断插件是否为动态安装的。不在此处加载插件实例。
    /// </para>
    /// </summary>
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
                    // 仅当 DLL 文件仍然存在时才视为有效索引条目
                    if (entry.AssemblyPath != null && File.Exists(entry.AssemblyPath))
                    {
                        _installedPluginIds.Add(entry.PluginTypeId);
                    }
                }
            }
        }
        catch (Exception ex) { Log.Debug("PluginManager", $"加载插件索引失败: {ex.Message}"); }
    }

    /// <summary>
    /// 从 installed.json 索引文件恢复动态安装的插件。
    /// <para>
    /// 遍历索引中的每个条目，加载对应的 DLL 程序集，
    /// 使用两级匹配策略创建插件实例，并恢复其启用状态。
    /// 使用 loadedAssemblies 集合避免重复加载同一程序集。
    /// </para>
    /// </summary>
    private void LoadInstalledPlugins()
    {
        var indexPath = Path.Combine(_pluginsDir, "installed.json");
        if (!File.Exists(indexPath)) return;

        // 防止同一程序集被重复加载
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
                    // 以字节方式加载，避免文件锁定
                    var fileBytes = File.ReadAllBytes(entry.AssemblyPath);
                    var assembly = Assembly.Load(fileBytes);

                    // 提取类型，处理部分加载失败
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

                    // 构建插件信息并恢复启用状态
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
                catch (Exception ex) { Log.Debug("PluginManager", $"恢复插件失败: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Log.Debug("PluginManager", $"恢复已安装插件失败: {ex.Message}"); }
    }

    /// <summary>
    /// 根据插件实例的具体接口类型，创建对应的 PluginInfo 对象。
    /// <para>
    /// 此方法负责：
    /// <list type="bullet">
    ///   <item>根据插件的接口类型生成 PluginTypeId（格式为 "{Category}.{PluginId}"）</item>
    ///   <item>确定插件的分类（PluginCategory）</item>
    ///   <item>为每种分类分配默认的图标 Emoji</item>
    /// </list>
    /// 匹配优先级：歌词提供者 → 协议提供者 → 封面提供者 → 音频增强器 → 菜单贡献者 → 其他
    /// </para>
    /// </summary>
    /// <param name="plugin">插件实例</param>
    /// <returns>包含分类信息和默认图标的 PluginInfo 对象</returns>
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
        else if (plugin is IOnlineMusicPlugin)
        {
            pluginTypeId = $"OnlineMusic.{plugin.PluginId}";
            category = PluginCategory.OnlineMusic;
            iconEmoji = "🌐";
        }
        else if (plugin is IThemeProviderPlugin)
        {
            pluginTypeId = $"ThemeProvider.{plugin.PluginId}";
            category = PluginCategory.ThemeProvider;
            iconEmoji = "🎨";
        }
        else if (plugin is IPlayerPagePlugin)
        {
            pluginTypeId = $"PlayerPage.{plugin.PluginId}";
            category = PluginCategory.PlayerPage;
            iconEmoji = "🎬";
        }
        else if (plugin is IAudioVisualizerPlugin)
        {
            pluginTypeId = $"AudioVisualizer.{plugin.PluginId}";
            category = PluginCategory.AudioVisualizer;
            iconEmoji = "📊";
        }
        else if (plugin is IViewContributorPlugin)
        {
            pluginTypeId = $"ViewContributor.{plugin.PluginId}";
            category = PluginCategory.ViewContributor;
            iconEmoji = "📱";
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

    /// <summary>
    /// 反射适配器区域 —— 包含所有用于代理不同版本插件接口的适配器类。
    /// <para>
    /// 当插件 DLL 引用了不同版本的宿主程序集时，直接类型转换会失败。
    /// 适配器通过反射调用目标对象的方法和属性，绕过 CLR 类型系统的版本兼容性检查。
    /// 所有适配器继承自 BasicPluginAdapter，并按需扩展特定接口的方法代理。
    /// </para>
    /// </summary>

    /// <summary>
    /// 类型转换辅助方法。尝试将对象转换为目标类型。
    /// <para>
    /// 转换策略：
    /// <list type="number">
    ///   <item>若值已为目标类型，直接返回</item>
    ///   <item>否则通过 JSON 序列化/反序列化进行转换（处理同名不同版本类型的映射）</item>
    ///   <item>若 JSON 转换失败，返回原始值</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="value">要转换的值</param>
    /// <param name="targetType">目标类型</param>
    /// <returns>转换后的值，或转换失败时的原始值</returns>
    private static object? ConvertType(object? value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsAssignableFrom(value.GetType())) return value;
        try
        {
            // 通过 JSON 中转实现跨版本类型映射
            var json = JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize(json, targetType);
        }
        catch
        {
            return value;
        }
    }

    /// <summary>
    /// 类型转换辅助方法的泛型版本。尝试将对象转换为指定的泛型类型。
    /// <para>
    /// 与非泛型版本不同，此方法在 JSON 转换失败时返回 null 而非原始值。
    /// </para>
    /// </summary>
    /// <typeparam name="T">目标类型（必须为引用类型）</typeparam>
    /// <param name="value">要转换的值</param>
    /// <returns>转换后的值，或转换失败时返回 null</returns>
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

    /// <summary>
    /// 通过反射异步调用目标对象的方法。
    /// <para>
    /// 此方法处理以下情况：
    /// <list type="bullet">
    ///   <item>按方法名和参数类型查找方法（精确匹配优先，退化为仅名称匹配）</item>
    ///   <item>若方法返回 Task，则 await 该 Task 并提取 Result 属性值</item>
    ///   <item>若方法返回普通值，直接返回</item>
    ///   <item>若方法未找到，返回 null</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="target">目标对象实例</param>
    /// <param name="methodName">要调用的方法名</param>
    /// <param name="args">方法参数</param>
    /// <returns>方法返回值，异步方法返回 Task&lt;T&gt; 的 T 值，未找到方法返回 null</returns>
    private static async Task<object?> InvokeAsyncMethod(object target, string methodName, params object?[]? args)
    {
        // 首先尝试按方法名 + 参数类型精确匹配
        var method = target.GetType().GetMethod(methodName,
            BindingFlags.Public | BindingFlags.Instance,
            null,
            args?.Select(a => a?.GetType() ?? typeof(object)).ToArray() ?? Type.EmptyTypes,
            null);

        // 精确匹配失败时，退化为仅按方法名查找
        if (method == null)
        {
            method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        }

        if (method == null) return null;

        var result = method.Invoke(target, args);
        // 处理异步方法：await Task 并提取 Result
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

    /// <summary>
    /// 基础插件适配器 —— 代理 IPlugin 接口的核心属性和方法。
    /// <para>
    /// 通过反射读取目标对象的属性值和调用方法，使不同版本编译的插件
    /// 能够在宿主端正常工作。所有其他适配器均继承自此类。
    /// </para>
    /// <para>
    /// 代理的 IPlugin 成员：
    /// <list type="bullet">
    ///   <item>PluginId —— 插件唯一标识</item>
    ///   <item>Name —— 插件显示名称</item>
    ///   <item>Version —— 插件版本号</item>
    ///   <item>Author —— 插件作者</item>
    ///   <item>Description —— 插件描述</item>
    ///   <item>Capabilities —— 插件能力列表</item>
    ///   <item>InitializeAsync() —— 异步初始化</item>
    ///   <item>ShutdownAsync() —— 异步关闭</item>
    /// </list>
    /// </para>
    /// </summary>
    private class BasicPluginAdapter : IPlugin
    {
        /// <summary>
        /// 反射调用的目标对象实例（来自不同版本程序集的插件实例）
        /// </summary>
        protected readonly object _target;

        /// <summary>
        /// 目标对象的运行时类型，用于反射获取属性和方法
        /// </summary>
        protected readonly Type _targetType;

        /// <summary>
        /// 初始化基础插件适配器
        /// </summary>
        /// <param name="target">要代理的目标插件对象实例</param>
        public BasicPluginAdapter(object target)
        {
            _target = target;
            _targetType = target.GetType();
        }

        /// <summary>
        /// 插件唯一标识，通过反射读取目标对象的 PluginId 属性
        /// </summary>
        public string PluginId => (string?)_targetType.GetProperty("PluginId")?.GetValue(_target) ?? "";

        /// <summary>
        /// 插件显示名称，通过反射读取目标对象的 Name 属性
        /// </summary>
        public string Name => (string?)_targetType.GetProperty("Name")?.GetValue(_target) ?? "";

        /// <summary>
        /// 插件版本号，通过反射读取目标对象的 Version 属性
        /// </summary>
        public string Version => (string?)_targetType.GetProperty("Version")?.GetValue(_target) ?? "";

        /// <summary>
        /// 插件作者，通过反射读取目标对象的 Author 属性
        /// </summary>
        public string Author => (string?)_targetType.GetProperty("Author")?.GetValue(_target) ?? "";

        /// <summary>
        /// 插件描述，通过反射读取目标对象的 Description 属性
        /// </summary>
        public string Description => (string?)_targetType.GetProperty("Description")?.GetValue(_target) ?? "";

        /// <summary>
        /// 插件能力列表，通过反射读取目标对象的 Capabilities 属性
        /// </summary>
        public List<string> Capabilities => (List<string>?)_targetType.GetProperty("Capabilities")?.GetValue(_target) ?? new();

        /// <summary>
        /// 异步初始化插件，通过反射调用目标对象的 InitializeAsync 方法
        /// </summary>
        public Task InitializeAsync() => (Task)_targetType.GetMethod("InitializeAsync")!.Invoke(_target, null)!;

        /// <summary>
        /// 异步关闭插件，通过反射调用目标对象的 ShutdownAsync 方法
        /// </summary>
        public Task ShutdownAsync() => (Task)_targetType.GetMethod("ShutdownAsync")!.Invoke(_target, null)!;
    }

    /// <summary>
    /// 封面提供者适配器 —— 代理 ICoverProviderPlugin 接口。
    /// <para>
    /// 在 BasicPluginAdapter 基础上，额外代理以下成员：
    /// <list type="bullet">
    ///   <item>IsAvailable —— 封面提供者是否可用</item>
    ///   <item>GetCoverAsync(Song) —— 根据歌曲信息获取封面图片字节数据</item>
    /// </list>
    /// GetCoverAsync 方法会检查目标方法的参数类型，若 Song 类型来自不同版本程序集，
    /// 则通过 JSON 序列化进行类型转换。
    /// </para>
    /// </summary>
    private class CoverProviderAdapter : BasicPluginAdapter, ICoverProviderPlugin
    {
        /// <summary>
        /// 初始化封面提供者适配器
        /// </summary>
        /// <param name="target">要代理的目标封面提供者对象实例</param>
        public CoverProviderAdapter(object target) : base(target) { }

        /// <summary>
        /// 封面提供者是否可用，通过反射读取目标对象的 IsAvailable 属性
        /// </summary>
        public bool IsAvailable => (bool?)_targetType.GetProperty("IsAvailable")?.GetValue(_target) ?? false;

        /// <summary>
        /// 根据歌曲信息异步获取封面图片。
        /// <para>
        /// 处理 Song 参数的跨版本类型转换：若目标方法期望的 Song 类型与宿主端不同，
        /// 则通过 JSON 序列化将宿主端的 Song 转换为目标版本的 Song。
        /// </para>
        /// </summary>
        /// <param name="song">歌曲信息</param>
        /// <returns>封面图片字节数组，获取失败返回 null</returns>
        public async Task<byte[]?> GetCoverAsync(Song song)
        {
            var method = _targetType.GetMethod("GetCoverAsync");
            if (method == null) return null;

            // 检查目标方法的参数类型，必要时进行跨版本类型转换
            var paramType = method.GetParameters().FirstOrDefault()?.ParameterType;
            object?[]? invokeArgs;
            if (paramType != null && paramType.FullName == typeof(Song).FullName)
            {
                // FullName 匹配但类型不同（不同版本），通过 JSON 转换
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

    /// <summary>
    /// 歌词提供者适配器 —— 代理 ILyricsProviderPlugin 接口。
    /// <para>
    /// 在 BasicPluginAdapter 基础上，额外代理以下成员：
    /// <list type="bullet">
    ///   <item>IsAvailable —— 歌词提供者是否可用</item>
    ///   <item>GetLyricsAsync(Song) —— 根据歌曲信息获取 LRC 歌词</item>
    /// </list>
    /// GetLyricsAsync 方法对返回值使用 ConvertType&lt;LrcLyrics&gt; 进行跨版本类型转换。
    /// </para>
    /// </summary>
    private class LyricsProviderAdapter : BasicPluginAdapter, ILyricsProviderPlugin
    {
        /// <summary>
        /// 初始化歌词提供者适配器
        /// </summary>
        /// <param name="target">要代理的目标歌词提供者对象实例</param>
        public LyricsProviderAdapter(object target) : base(target) { }

        /// <summary>
        /// 歌词提供者是否可用，通过反射读取目标对象的 IsAvailable 属性
        /// </summary>
        public bool IsAvailable => (bool?)_targetType.GetProperty("IsAvailable")?.GetValue(_target) ?? false;

        /// <summary>
        /// 根据歌曲信息异步获取 LRC 格式歌词。
        /// <para>
        /// 处理 Song 参数和 LrcLyrics 返回值的跨版本类型转换。
        /// </para>
        /// </summary>
        /// <param name="song">歌曲信息</param>
        /// <returns>LRC 歌词对象，获取失败返回 null</returns>
        public async Task<LrcLyrics?> GetLyricsAsync(Song song)
        {
            var method = _targetType.GetMethod("GetLyricsAsync");
            if (method == null) return null;

            // 检查目标方法的参数类型，必要时进行跨版本类型转换
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
            // 返回值通过 JSON 转换映射到宿主端的 LrcLyrics 类型
            return ConvertType<LrcLyrics>(result);
        }
    }

    /// <summary>
    /// 菜单贡献者适配器 —— 代理 IMenuContributorPlugin 接口。
    /// <para>
    /// 在 BasicPluginAdapter 基础上，额外代理以下成员：
    /// <list type="bullet">
    ///   <item>GetMenuItems(Song) —— 获取歌曲上下文菜单项列表</item>
    ///   <item>OnMenuItemClicked(int, Song, object) —— 处理菜单项点击事件</item>
    /// </list>
    /// GetMenuItems 方法对返回的列表进行逐项类型转换，处理 IList 到 List&lt;MenuItemEntry&gt; 的映射。
    /// </para>
    /// </summary>
    private class MenuContributorAdapter : BasicPluginAdapter, IMenuContributorPlugin
    {
        /// <summary>
        /// 初始化菜单贡献者适配器
        /// </summary>
        /// <param name="target">要代理的目标菜单贡献者对象实例</param>
        public MenuContributorAdapter(object target) : base(target) { }

        /// <summary>
        /// 获取指定歌曲的上下文菜单项列表。
        /// <para>
        /// 处理 Song 参数的跨版本类型转换。
        /// 返回值处理：若直接类型转换失败，则遍历 IList 逐项通过 JSON 转换为 MenuItemEntry。
        /// </para>
        /// </summary>
        /// <param name="song">歌曲信息</param>
        /// <returns>菜单项列表</returns>
        public List<MenuItemEntry> GetMenuItems(Song song)
        {
            var method = _targetType.GetMethod("GetMenuItems");
            if (method == null) return new();

            // 检查目标方法的参数类型，必要时进行跨版本类型转换
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
            // 尝试直接类型转换
            if (result is List<MenuItemEntry> typed) return typed;

            // 直接转换失败，遍历 IList 逐项转换
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

        /// <summary>
        /// 处理菜单项点击事件。
        /// <para>
        /// 处理 Song 参数的跨版本类型转换，以及 fragment 参数的透传。
        /// 若目标方法返回 Task，则 await 该 Task。
        /// </para>
        /// </summary>
        /// <param name="itemId">被点击的菜单项 ID</param>
        /// <param name="song">当前歌曲信息</param>
        /// <param name="fragment">Android Fragment 对象（平台特定）</param>
        public async Task OnMenuItemClicked(int itemId, Song song, object fragment)
        {
            var method = _targetType.GetMethod("OnMenuItemClicked");
            if (method == null) return;

            var parameters = method.GetParameters();
            var args = new object?[3];
            args[0] = itemId;

            // 检查第二个参数类型，必要时进行 Song 的跨版本转换
            if (parameters.Length > 1 && parameters[1].ParameterType.FullName == typeof(Song).FullName)
                args[1] = ConvertType(song, parameters[1].ParameterType);
            else
                args[1] = song;

            args[2] = fragment;

            var result = method.Invoke(_target, args);
            if (result is Task task) await task;
        }
    }

    /// <summary>
    /// 协议提供者适配器 —— 代理 IProtocolProviderPlugin 接口。
    /// <para>
    /// 在 BasicPluginAdapter 基础上，额外代理以下成员：
    /// <list type="bullet">
    ///   <item>ProtocolName —— 协议名称</item>
    ///   <item>ListFilesAsync(string) —— 列出远程文件列表</item>
    ///   <item>OpenReadAsync(string) —— 打开远程文件读取流</item>
    ///   <item>TestConnectionAsync(ConnectionProfile) —— 测试连接是否可用</item>
    /// </list>
    /// ListFilesAsync 和 TestConnectionAsync 方法处理了跨版本类型转换。
    /// </para>
    /// </summary>
    private class ProtocolProviderAdapter : BasicPluginAdapter, IProtocolProviderPlugin
    {
        /// <summary>
        /// 初始化协议提供者适配器
        /// </summary>
        /// <param name="target">要代理的目标协议提供者对象实例</param>
        public ProtocolProviderAdapter(object target) : base(target) { }

        /// <summary>
        /// 协议名称，通过反射读取目标对象的 ProtocolName 属性
        /// </summary>
        public string ProtocolName => (string?)_targetType.GetProperty("ProtocolName")?.GetValue(_target) ?? "";

        /// <summary>
        /// 异步列出指定路径下的远程文件列表。
        /// <para>
        /// 返回值处理：若直接类型转换失败，则遍历 IList 逐项通过 JSON 转换为 RemoteFile。
        /// </para>
        /// </summary>
        /// <param name="path">远程路径</param>
        /// <returns>远程文件列表</returns>
        public async Task<List<RemoteFile>> ListFilesAsync(string path)
        {
            var result = await InvokeAsyncMethod(_target, "ListFilesAsync", path);
            if (result is List<RemoteFile> typed) return typed;

            // 直接转换失败，遍历 IList 逐项转换
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

        /// <summary>
        /// 异步打开远程文件的读取流
        /// </summary>
        /// <param name="filePath">远程文件路径</param>
        /// <returns>文件读取流</returns>
        public async Task<Stream> OpenReadAsync(string filePath)
        {
            var result = await InvokeAsyncMethod(_target, "OpenReadAsync", filePath);
            return (Stream)result!;
        }

        /// <summary>
        /// 异步测试连接配置是否可用。
        /// <para>
        /// 处理 ConnectionProfile 参数的跨版本类型转换。
        /// </para>
        /// </summary>
        /// <param name="profile">连接配置信息</param>
        /// <returns>连接成功返回 true，否则返回 false</returns>
        public async Task<bool> TestConnectionAsync(ConnectionProfile profile)
        {
            var method = _targetType.GetMethod("TestConnectionAsync");
            if (method == null) return false;

            // 检查目标方法的参数类型，必要时进行跨版本类型转换
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

    /// <summary>
    /// 音频增强器适配器 —— 代理 IAudioEnhancerPlugin 接口。
    /// <para>
    /// 在 BasicPluginAdapter 基础上，额外代理以下成员：
    /// <list type="bullet">
    ///   <item>IsEnabled —— 音频增强器是否启用（支持读写）</item>
    ///   <item>ProcessSamples(float[], int, int) —— 处理音频采样数据</item>
    ///   <item>Reset() —— 重置音频增强器状态</item>
    /// </list>
    /// 与其他适配器不同，ProcessSamples 和 Reset 是同步方法，无需处理异步调用。
    /// </para>
    /// </summary>
    private class AudioEnhancerAdapter : BasicPluginAdapter, IAudioEnhancerPlugin
    {
        /// <summary>
        /// 初始化音频增强器适配器
        /// </summary>
        /// <param name="target">要代理的目标音频增强器对象实例</param>
        public AudioEnhancerAdapter(object target) : base(target) { }

        /// <summary>
        /// 音频增强器是否启用。
        /// <para>
        /// 通过反射读写目标对象的 IsEnabled 属性。
        /// getter 直接读取，setter 检查属性是否可写后再设置。
        /// </para>
        /// </summary>
        public bool IsEnabled
        {
            get => (bool?)_targetType.GetProperty("IsEnabled")?.GetValue(_target) ?? false;
            set
            {
                var prop = _targetType.GetProperty("IsEnabled");
                if (prop?.CanWrite == true) prop.SetValue(_target, value);
            }
        }

        /// <summary>
        /// 处理音频采样数据。
        /// <para>
        /// 通过反射调用目标对象的 ProcessSamples 方法。
        /// 若方法不存在或返回值类型不匹配，返回原始采样数据（不做处理）。
        /// </para>
        /// </summary>
        /// <param name="samples">PCM 浮点采样数据</param>
        /// <param name="sampleRate">采样率（Hz）</param>
        /// <param name="channels">声道数</param>
        /// <returns>处理后的采样数据，或原始数据（处理失败时）</returns>
        public float[] ProcessSamples(float[] samples, int sampleRate, int channels)
        {
            var method = _targetType.GetMethod("ProcessSamples");
            if (method == null) return samples;
            var result = method.Invoke(_target, new object[] { samples, sampleRate, channels });
            return result as float[] ?? samples;
        }

        /// <summary>
        /// 重置音频增强器状态，通过反射调用目标对象的 Reset 方法
        /// </summary>
        public void Reset()
        {
            var method = _targetType.GetMethod("Reset");
            method?.Invoke(_target, null);
        }
    }

    /// <summary>
    /// 在线音乐音源适配器 —— 代理 IOnlineMusicPlugin 接口。
    /// <para>
    /// 在 BasicPluginAdapter 基础上，额外代理以下成员：
    /// <list type="bullet">
    ///   <item>PlatformName —— 来源平台标识</item>
    ///   <item>SearchAsync(string, int, int) —— 搜索歌曲</item>
    ///   <item>GetPlayUrlAsync(OnlineSong, int) —— 取播放直链</item>
    ///   <item>GetLyricsAsync(OnlineSong) —— 取歌词（含翻译）</item>
    ///   <item>GetPlaylistsAsync(string?) —— 取歌单列表</item>
    /// </list>
    /// OnlineSong / OnlinePlaylist 参数与返回值通过 JSON 跨版本转换（同现有适配器模式）；
    /// GetLyricsAsync 的返回元组按 ValueTuple 字段 Item1/Item2 反射读取。
    /// </para>
    /// </summary>
    private class OnlineMusicAdapter : BasicPluginAdapter, IOnlineMusicPlugin
    {
        /// <summary>初始化在线音乐音源适配器</summary>
        /// <param name="target">要代理的目标在线音乐音源对象实例</param>
        public OnlineMusicAdapter(object target) : base(target) { }

        /// <summary>来源平台标识，通过反射读取目标对象的 PlatformName 属性</summary>
        public string PlatformName => (string?)_targetType.GetProperty("PlatformName")?.GetValue(_target) ?? "";

        /// <summary>异步搜索歌曲</summary>
        public async Task<List<OnlineSong>?> SearchAsync(string keyword, int page = 1, int pageSize = 8)
        {
            var result = await InvokeAsyncMethod(_target, "SearchAsync", keyword, page, pageSize);
            if (result is List<OnlineSong> typed) return typed;
            if (result is System.Collections.IList list)
            {
                var songs = new List<OnlineSong>();
                foreach (var item in list)
                {
                    var converted = ConvertType<OnlineSong>(item);
                    if (converted != null) songs.Add(converted);
                }
                return songs;
            }
            return null;
        }

        /// <summary>异步获取播放直链</summary>
        public async Task<string?> GetPlayUrlAsync(OnlineSong song, int quality = 0)
        {
            var method = _targetType.GetMethod("GetPlayUrlAsync");
            if (method == null) return null;

            var paramType = method.GetParameters().FirstOrDefault()?.ParameterType;
            object?[]? invokeArgs;
            if (paramType != null && paramType.FullName == typeof(OnlineSong).FullName)
                invokeArgs = new[] { ConvertType(song, paramType), quality };
            else
                invokeArgs = new object?[] { song, quality };

            var result = await InvokeAsyncMethod(_target, "GetPlayUrlAsync", invokeArgs);
            return result as string;
        }

        /// <summary>异步获取歌词（LRC 原文 + 翻译）</summary>
        public async Task<(string? Lrc, string? TLrc)?> GetLyricsAsync(OnlineSong song)
        {
            var method = _targetType.GetMethod("GetLyricsAsync");
            if (method == null) return null;

            var paramType = method.GetParameters().FirstOrDefault()?.ParameterType;
            object?[]? invokeArgs;
            if (paramType != null && paramType.FullName == typeof(OnlineSong).FullName)
                invokeArgs = new[] { ConvertType(song, paramType) };
            else
                invokeArgs = new object?[] { song };

            var result = await InvokeAsyncMethod(_target, "GetLyricsAsync", invokeArgs);
            if (result == null) return null;

            string? lrc = null, tlrrc = null;
            var type = result.GetType();
            if (type.IsValueType && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                // Nullable<ValueTuple<string,string>>：读取 Value 属性
                var hasValue = type.GetProperty("HasValue")?.GetValue(result) as bool? ?? true;
                if (!hasValue) return null;
                var valueObj = type.GetProperty("Value")?.GetValue(result);
                if (valueObj != null)
                {
                    lrc = valueObj.GetType().GetField("Item1")?.GetValue(valueObj) as string;
                    tlrrc = valueObj.GetType().GetField("Item2")?.GetValue(valueObj) as string;
                }
            }
            else
            {
                // 直接是 ValueTuple<string,string>
                lrc = type.GetField("Item1")?.GetValue(result) as string;
                tlrrc = type.GetField("Item2")?.GetValue(result) as string;
            }
            return (lrc, tlrrc);
        }

        /// <summary>异步获取歌单列表</summary>
        public async Task<List<OnlinePlaylist>> GetPlaylistsAsync(string? category = null)
        {
            var result = await InvokeAsyncMethod(_target, "GetPlaylistsAsync", category);
            if (result is List<OnlinePlaylist> typed) return typed;
            if (result is System.Collections.IList list)
            {
                var items = new List<OnlinePlaylist>();
                foreach (var item in list)
                {
                    var converted = ConvertType<OnlinePlaylist>(item);
                    if (converted != null) items.Add(converted);
                }
                return items;
            }
            return new();
        }

        /// <summary>异步获取歌单内歌曲</summary>
        public async Task<List<OnlineSong>?> GetPlaylistSongsAsync(OnlinePlaylist playlist, int page = 1, int pageSize = 50)
        {
            var method = _targetType.GetMethod("GetPlaylistSongsAsync");
            if (method == null) return null;

            var paramType = method.GetParameters().FirstOrDefault()?.ParameterType;
            object?[]? invokeArgs;
            if (paramType != null && paramType.FullName == typeof(OnlinePlaylist).FullName)
                invokeArgs = new[] { ConvertType(playlist, paramType), page, pageSize };
            else
                invokeArgs = new object?[] { playlist, page, pageSize };

            var result = await InvokeAsyncMethod(_target, "GetPlaylistSongsAsync", invokeArgs);
            if (result is List<OnlineSong> typed) return typed;
            if (result is System.Collections.IList list)
            {
                var songs = new List<OnlineSong>();
                foreach (var item in list)
                {
                    var converted = ConvertType<OnlineSong>(item);
                    if (converted != null) songs.Add(converted);
                }
                return songs;
            }
            return null;
        }

        /// <summary>异步获取私人漫游（随机推荐）</summary>
        public async Task<List<OnlineSong>?> GetPrivateFmAsync(int num = 10)
        {
            var result = await InvokeAsyncMethod(_target, "GetPrivateFmAsync", num);
            if (result is List<OnlineSong> typed) return typed;
            if (result is System.Collections.IList list)
            {
                var songs = new List<OnlineSong>();
                foreach (var item in list)
                {
                    var converted = ConvertType<OnlineSong>(item);
                    if (converted != null) songs.Add(converted);
                }
                return songs;
            }
            return null;
        }

        /// <summary>异步获取每日推荐歌曲</summary>
        public async Task<List<OnlineSong>?> GetDailyRecommendAsync(int num = 20)
        {
            var result = await InvokeAsyncMethod(_target, "GetDailyRecommendAsync", num);
            if (result is List<OnlineSong> typed) return typed;
            if (result is System.Collections.IList list)
            {
                var songs = new List<OnlineSong>();
                foreach (var item in list)
                {
                    var converted = ConvertType<OnlineSong>(item);
                    if (converted != null) songs.Add(converted);
                }
                return songs;
            }
            return null;
        }

        /// <summary>异步获取排行榜列表</summary>
        public async Task<List<OnlinePlaylist>> GetToplistsAsync()
        {
            var result = await InvokeAsyncMethod(_target, "GetToplistsAsync");
            if (result is List<OnlinePlaylist> typed) return typed;
            if (result is System.Collections.IList list)
            {
                var items = new List<OnlinePlaylist>();
                foreach (var item in list)
                {
                    var converted = ConvertType<OnlinePlaylist>(item);
                    if (converted != null) items.Add(converted);
                }
                return items;
            }
            return new();
        }

        /// <summary>获取浏览器登录配置（反射调用插件方法；未实现返回 null）</summary>
        public async Task<BrowserLoginInfo?> GetBrowserLoginInfoAsync()
        {
            var result = await InvokeAsyncMethod(_target, "GetBrowserLoginInfoAsync");
            return ConvertType<BrowserLoginInfo>(result);
        }

        /// <summary>接收宿主从 WebView 提取的 Cookie，回传插件完成登录</summary>
        public async Task SetLoginCookieAsync(string cookie)
            => await InvokeAsyncMethod(_target, "SetLoginCookieAsync", cookie);

        /// <summary>当前是否已登录</summary>
        public async Task<bool> IsLoggedInAsync()
        {
            var result = await InvokeAsyncMethod(_target, "IsLoggedInAsync");
            return result is bool b && b;
        }

        /// <summary>已登录账号昵称</summary>
        public async Task<string?> GetAccountNameAsync()
        {
            var result = await InvokeAsyncMethod(_target, "GetAccountNameAsync");
            return result as string;
        }

        /// <summary>退出登录</summary>
        public async Task LogoutAsync() => await InvokeAsyncMethod(_target, "LogoutAsync");
    }

    /// <summary>
    /// 视图贡献者适配器：通过反射代理不同版本的 IViewContributorPlugin 实现。
    /// <para>
    /// EntryTitle/EntryIcon 属性通过反射读取；CreateEntryPage 通过反射调用目标方法，
    /// 直接返回目标对象创建的页面实例（object 形式），由宿主强制转换为 Page。
    /// </para>
    /// </summary>
    private class ViewContributorAdapter : BasicPluginAdapter, IViewContributorPlugin
    {
        /// <summary>初始化视图贡献者适配器</summary>
        /// <param name="target">要代理的目标视图贡献者对象实例</param>
        public ViewContributorAdapter(object target) : base(target) { }

        /// <summary>入口显示标题，通过反射读取目标对象的 EntryTitle 属性</summary>
        public string EntryTitle => (string?)_targetType.GetProperty("EntryTitle")?.GetValue(_target) ?? "插件";

        /// <summary>入口图标，通过反射读取目标对象的 EntryIcon 属性</summary>
        public string EntryIcon => (string?)_targetType.GetProperty("EntryIcon")?.GetValue(_target) ?? "📦";

        /// <summary>
        /// 创建并返回入口页面实例。
        /// 通过反射调用目标对象的 CreateEntryPage(IServiceProvider) 方法，
        /// 直接返回目标对象创建的页面实例（object 形式）。
        /// </summary>
        /// <param name="services">宿主的服务提供者</param>
        /// <returns>MAUI ContentPage 实例（以 object 形式返回）</returns>
        public object CreateEntryPage(IServiceProvider services)
        {
            var method = _targetType.GetMethod("CreateEntryPage");
            if (method == null)
                throw new InvalidOperationException("插件未实现 CreateEntryPage 方法");

            // 反射调用 CreateEntryPage(IServiceProvider)
            return method.Invoke(_target, new object[] { services })
                ?? throw new InvalidOperationException("CreateEntryPage 返回 null");
        }
    }

    /// <summary>
    /// 主题提供者适配器：通过反射代理不同版本的 IThemeProviderPlugin 实现。
    /// <para>
    /// ThemeId/ThemeName/ThemeOrder 属性通过反射读取；GetThemeColorsAsync/GetThemeBackgroundAsync
    /// 通过 InvokeAsyncMethod 反射调用。色板为 BCL 类型（Dictionary&lt;string,string&gt;），
    /// 不涉自定义类型跨版本问题，可直接使用。
    /// </para>
    /// </summary>
    private class ThemeProviderAdapter : BasicPluginAdapter, IThemeProviderPlugin
    {
        /// <summary>初始化主题提供者适配器</summary>
        /// <param name="target">要代理的目标主题提供者对象实例</param>
        public ThemeProviderAdapter(object target) : base(target) { }

        /// <summary>主题唯一标识，通过反射读取</summary>
        public string ThemeId => (string?)_targetType.GetProperty("ThemeId")?.GetValue(_target) ?? "";

        /// <summary>主题显示名称，通过反射读取</summary>
        public string ThemeName => (string?)_targetType.GetProperty("ThemeName")?.GetValue(_target) ?? "插件主题";

        /// <summary>排序权重，通过反射读取</summary>
        public int ThemeOrder => (int?)_targetType.GetProperty("ThemeOrder")?.GetValue(_target) ?? 100;

        /// <summary>获取主题色板（资源键 → 色值），通过反射调用目标方法</summary>
        public async Task<Dictionary<string, string>?> GetThemeColorsAsync()
        {
            if (_targetType.GetMethod("GetThemeColorsAsync") == null) return null;
            var result = await InvokeAsyncMethod(_target, "GetThemeColorsAsync", null);
            return result as Dictionary<string, string>;
        }

        /// <summary>获取主题背景图片源，通过反射调用目标方法</summary>
        public async Task<string?> GetThemeBackgroundAsync()
        {
            if (_targetType.GetMethod("GetThemeBackgroundAsync") == null) return null;
            var result = await InvokeAsyncMethod(_target, "GetThemeBackgroundAsync", null);
            return result as string;
        }
    }

    /// <summary>
    /// 播放页提供者适配器：通过反射代理不同版本的 IPlayerPagePlugin 实现。
    /// <para>
    /// PlayerPageName/Priority 属性通过反射读取；CreatePlayerView 通过反射调用目标方法，
    /// 直接返回目标对象创建的视图实例（object 形式），由宿主强制转换为 View。
    /// </para>
    /// </summary>
    private class PlayerPageAdapter : BasicPluginAdapter, IPlayerPagePlugin
    {
        /// <summary>初始化播放页提供者适配器</summary>
        /// <param name="target">要代理的目标播放页提供者对象实例</param>
        public PlayerPageAdapter(object target) : base(target) { }

        /// <summary>播放页显示名称，通过反射读取</summary>
        public string PlayerPageName => (string?)_targetType.GetProperty("PlayerPageName")?.GetValue(_target) ?? "插件播放页";

        /// <summary>优先级，通过反射读取</summary>
        public int Priority => (int?)_targetType.GetProperty("Priority")?.GetValue(_target) ?? 0;

        /// <summary>创建并返回播放页内容视图，通过反射调用目标方法</summary>
        public object CreatePlayerView(IServiceProvider services)
        {
            var method = _targetType.GetMethod("CreatePlayerView");
            if (method == null)
                throw new InvalidOperationException("插件未实现 CreatePlayerView 方法");
            return method.Invoke(_target, new object[] { services })
                ?? throw new InvalidOperationException("CreatePlayerView 返回 null");
        }
    }

    /// <summary>
    /// 音频可视化适配器：通过反射代理不同版本的 IAudioVisualizerPlugin 实现。
    /// <para>
    /// VisualizerName/IsEnabled 属性通过反射读写；CreateVisualizerView 通过反射调用目标方法，
    /// 直接返回目标对象创建的视图实例（object 形式），由宿主强制转换为 View。
    /// </para>
    /// </summary>
    private class AudioVisualizerAdapter : BasicPluginAdapter, IAudioVisualizerPlugin
    {
        /// <summary>初始化音频可视化适配器</summary>
        /// <param name="target">要代理的目标音频可视化对象实例</param>
        public AudioVisualizerAdapter(object target) : base(target) { }

        /// <summary>可视化名称，通过反射读取</summary>
        public string VisualizerName => (string?)_targetType.GetProperty("VisualizerName")?.GetValue(_target) ?? "可视化";

        /// <summary>是否启用可视化，通过反射读写</summary>
        public bool IsEnabled
        {
            get => (bool?)_targetType.GetProperty("IsEnabled")?.GetValue(_target) ?? false;
            set => _targetType.GetProperty("IsEnabled")?.SetValue(_target, value);
        }

        /// <summary>创建并返回可视化视图，通过反射调用目标方法</summary>
        public object CreateVisualizerView(IServiceProvider services)
        {
            var method = _targetType.GetMethod("CreateVisualizerView");
            if (method == null)
                throw new InvalidOperationException("插件未实现 CreateVisualizerView 方法");
            return method.Invoke(_target, new object[] { services })
                ?? throw new InvalidOperationException("CreateVisualizerView 返回 null");
        }
    }

    /// <summary>
    /// 可配置插件适配器：通过反射代理不同版本的 IPluginConfigurable 实现。
    /// <para>
    /// CanConfigure 属性通过反射读取；CreateConfigView 通过反射调用目标方法，
    /// 直接返回目标对象创建的配置视图实例（object 形式），由宿主强制转换为 View。
    /// </para>
    /// </summary>
    private class ConfigurableAdapter : BasicPluginAdapter, IPluginConfigurable
    {
        /// <summary>初始化可配置插件适配器</summary>
        /// <param name="target">要代理的目标可配置插件对象实例</param>
        public ConfigurableAdapter(object target) : base(target) { }

        /// <summary>是否可配置，通过反射读取</summary>
        public bool CanConfigure => (bool?)_targetType.GetProperty("CanConfigure")?.GetValue(_target) ?? false;

        /// <summary>创建并返回配置视图，通过反射调用目标方法</summary>
        public object CreateConfigView(IServiceProvider services)
        {
            var method = _targetType.GetMethod("CreateConfigView");
            if (method == null)
                throw new InvalidOperationException("插件未实现 CreateConfigView 方法");
            return method.Invoke(_target, new object[] { services })
                ?? throw new InvalidOperationException("CreateConfigView 返回 null");
        }
    }

    #endregion

    /// <summary>
    /// 已安装插件索引条目 —— 用于 installed.json 文件的序列化/反序列化。
    /// <para>
    /// 记录每个动态安装插件的关键信息，确保应用重启后能够恢复已安装插件。
    /// </para>
    /// </summary>
    private class InstalledPluginEntry
    {
        /// <summary>
        /// 插件类型标识，格式为 "{Category}.{PluginId}"，如 "LyricsProvider.NetEaseLyrics"
        /// </summary>
        public string PluginTypeId { get; set; } = string.Empty;

        /// <summary>
        /// 插件 DLL 文件的本地绝对路径
        /// </summary>
        public string? AssemblyPath { get; set; }

        /// <summary>
        /// 插件安装来源 URL（GitHub 安装时为仓库 URL，本地安装时为文件路径）
        /// </summary>
        public string? InstallUrl { get; set; }

        /// <summary>
        /// 插件显示名称，用于索引记录
        /// </summary>
        public string? PluginName { get; set; }
    }
}
