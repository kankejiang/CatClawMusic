using System.Reflection;
using System.Runtime.CompilerServices;
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
    /// IQuickEntryPlugin 接口的全限定名，用于反射匹配快捷入口插件（发现页 HeroTrack 卡片）
    /// </summary>
    private static readonly string IQuickEntryFullName = typeof(IQuickEntryPlugin).FullName!;

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

    /// <summary>单个插件初始化超时：网络型插件（会话恢复/Cookie 校验）挂起时不再拖住全部插件就绪</summary>
    private static readonly TimeSpan PluginInitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>主插件初始化并发度：插件间通常无依赖，受控并行显著缩短总就绪时间；
    /// 旧版完全串行，总耗时 = Σ(每个插件初始化耗时)，一个慢插件会拖延其后的所有插件。</summary>
    private const int InitParallelism = 4;

    /// <summary>
    /// 异步初始化所有已启用的插件。
    /// <para>
    /// 主插件受控并发初始化（并发 4，单个 10s 超时），每个主插件就绪后其子插件再并行初始化。
    /// 若某个主插件初始化失败/超时，将自动将其设为禁用状态（子插件静默忽略）。
    /// </para>
    /// </summary>
    public async Task InitializeAllAsync()
    {
        var enabled = _plugins.Where(p => p.IsEnabled).ToList();
        using var gate = new SemaphoreSlim(InitParallelism, InitParallelism);
        var tasks = enabled.Select(async info =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                try
                {
                    await info.Plugin.InitializeAsync().WaitAsync(PluginInitTimeout).ConfigureAwait(false);
                }
                catch
                {
                    // 主插件初始化失败/超时则自动禁用
                    info.IsEnabled = false;
                    return;
                }
                foreach (var sub in info.SubPlugins)
                {
                    // 子插件初始化失败/超时则静默忽略
                    try { await sub.InitializeAsync().WaitAsync(PluginInitTimeout).ConfigureAwait(false); }
                    catch (Exception ex) { Log.Debug("PluginManager", $"子插件初始化失败: {ex.Message}"); }
                }
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
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
    /// <param name="repoUrl">仓库地址，格式为 https://github.com/用户名/仓库名 或 https://gitee.com/用户名/仓库名</param>
    /// <param name="progress">进度报告器，报告 (描述文本, 百分比) 元组</param>
    /// <returns>安装成功返回 PluginInfo，失败返回 null</returns>
    public async Task<PluginInfo?> InstallFromGitHubAsync(string repoUrl, IProgress<(string, int)>? progress = null)
    {
        try
        {
            // Gitee 仓库走 raw 文件约定（免认证）：仓库根目录放 plugin.ccp + plugin.json
            if (repoUrl.Contains("gitee.com", StringComparison.OrdinalIgnoreCase))
                return await InstallFromGiteeAsync(repoUrl, progress);

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

        // 强制运行插件程序集的模块初始化器（<Module>.cctor）。
        // Assembly.Load(byte[]) 不会立即执行 [ModuleInitializer]（lazy），而插件内部
        // 可能靠模块初始化器注册 AppDomain.AssemblyResolve（如 LxSource 插件从嵌入
        // 资源加载 Jint.dll/Acornima.dll）。若不先触发，紧随其后的 GetTypes() 扫描
        // 到字段/方法签名引用第三方程序集类型的插件类型（如 LxScriptHost 引用
        // Jint.Engine）时会抛 ReflectionTypeLoadException，导致安装失败。
        try { RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle); }
        catch (Exception ex) { Log.Debug("PluginManager", $"[PluginManager] 运行插件模块初始化器失败: {ex.Message}"); }

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

        // 异步初始化主插件和子插件（保持 await：安装 UI 返回时插件已就绪；
        // 加 10s 超时防单个慢插件卡死安装流程，失败不阻断索引保存）
        if (info.IsEnabled)
        {
            try { await primary.InitializeAsync().WaitAsync(PluginInitTimeout).ConfigureAwait(false); }
            catch (Exception ex) { Log.Debug("PluginManager", $"主插件安装初始化失败: {ex.Message}"); }
            foreach (var sub in info.SubPlugins)
            {
                try { await sub.InitializeAsync().WaitAsync(PluginInitTimeout).ConfigureAwait(false); }
                catch (Exception ex) { Log.Debug("PluginManager", $"子插件初始化失败: {ex.Message}"); }
            }
        }

        // 更新已安装插件索引
        _installedPluginIds.Add(info.PluginTypeId);
        SaveInstalledIndex();

        progress?.Report(("安装完成", 100));
        return info;
    }

    /// <summary>
    /// 从 Gitee 仓库安装插件（免认证 raw 文件约定）：
    /// 仓库根目录需包含 plugin.json（可选 manifest）+ plugin.ccp（插件文件），
    /// master/main 分支自动探测。
    /// </summary>
    public async Task<PluginInfo?> InstallFromGiteeAsync(string repoUrl, IProgress<(string, int)>? progress = null)
    {
        try
        {
            progress?.Report(("正在解析仓库地址...", 5));
            if (!TryParseRepo(repoUrl, out var owner, out var repo))
                throw new InvalidOperationException("无法解析 Gitee 仓库地址，请使用格式: https://gitee.com/用户名/仓库名");

            // 读 manifest 拿文件名（缺省 plugin.ccp）
            string fileName = "plugin.ccp";
            var manifestUrl = await ResolveGiteeRawAsync(owner, repo, "plugin.json");
            if (manifestUrl != null)
            {
                try
                {
                    var json = await _httpClient.GetStringAsync(manifestUrl);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("file", out var f))
                        fileName = f.GetString() ?? fileName;
                }
                catch { }
            }

            var fileUrl = await ResolveGiteeRawAsync(owner, repo, fileName);
            if (fileUrl == null)
                throw new InvalidOperationException(
                    $"仓库 {owner}/{repo} 中未找到 {fileName}（master/main 分支），\n" +
                    "请先将插件文件提交到仓库根目录。");

            progress?.Report(("正在下载插件...", 30));
            var destPath = Path.Combine(_pluginsDir, fileName);
            if (File.Exists(destPath)) File.Delete(destPath);
            await DownloadToFileAsync(fileUrl, destPath, progress);

            progress?.Report(("正在加载插件...", 75));
            return await LoadAndRegisterPluginAsync(destPath, repoUrl, progress);
        }
        catch (Exception ex)
        {
            Log.Debug("PluginManager", $"[PluginManager] Gitee 安装失败: {ex.Message}");
            throw;
        }
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
                if (interfaceNames.Contains(IQuickEntryFullName))
                    wrappers.Add(new QuickEntryAdapter(rawInstance));
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
    /// 检查插件是否有新版本。更新源优先级：
    /// 1) 插件实现 <see cref="IPlugin.UpdateUrl"/>（返回 manifest JSON：version/download_url/notes）
    /// 2) GitHub 约定：安装时记录的仓库地址 → releases/latest 的 tag_name 对比本地版本
    /// 检查失败（网络/无 Release/无更新）返回 null 或 HasUpdate=false，不抛异常。
    /// </summary>
    public async Task<PluginUpdateInfo?> CheckPluginUpdateAsync(PluginInfo plugin)
    {
        try
        {
            // 1. 插件自带更新源（manifest JSON）
            var updateUrl = plugin.Plugin.UpdateUrl;
            if (!string.IsNullOrWhiteSpace(updateUrl))
                return await ParseManifestAsync(updateUrl, plugin.InstallUrl, plugin.Version);

            if (string.IsNullOrWhiteSpace(plugin.InstallUrl))
                return null;

            // 2. Gitee 约定：仓库 raw 文件 plugin.json（免认证；master/main 分支自动探测）
            if (plugin.InstallUrl.Contains("gitee.com", StringComparison.OrdinalIgnoreCase))
            {
                string giteeOwner, giteeRepo;
                if (!TryParseRepo(plugin.InstallUrl, out giteeOwner, out giteeRepo)) return null;
                var manifestUrl = await ResolveGiteeRawAsync(giteeOwner, giteeRepo, "plugin.json");
                if (manifestUrl != null)
                    return await ParseManifestAsync(manifestUrl, plugin.InstallUrl, plugin.Version);
                return null;
            }

            // 3. GitHub Releases 约定
            if (!plugin.InstallUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                return null;

            string owner, repo;
            if (!TryParseRepo(plugin.InstallUrl, out owner, out repo)) return null;

            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{owner}/{repo}/releases/latest");
            request.Headers.UserAgent.ParseAdd("CatClawMusic/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github.v3+json");
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null; // 404 = 无 Release
            var releasesJson = await response.Content.ReadAsStringAsync();

            using var doc2 = System.Text.Json.JsonDocument.Parse(releasesJson);
            var root2 = doc2.RootElement;
            var tag = root2.TryGetProperty("tag_name", out var t) ? (t.GetString() ?? "").TrimStart('v', 'V') : "";
            if (string.IsNullOrEmpty(tag)) return null;

            // 找 .ccp 附件作为下载源
            string? downloadUrl = null;
            if (root2.TryGetProperty("assets", out var assets) && assets.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n2) ? n2.GetString() ?? "" : "";
                    if (name.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        break;
                    }
                }
            }

            return new PluginUpdateInfo
            {
                HasUpdate = IsNewerVersion(tag, plugin.Version),
                LatestVersion = tag,
                DownloadUrl = downloadUrl,
                ReleaseNotes = root2.TryGetProperty("body", out var b) ? (b.GetString() ?? "").Trim() : null,
                Homepage = plugin.InstallUrl
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>语义化版本对比（支持 v 前缀；解析失败回退字符串比较）</summary>
    private static bool IsNewerVersion(string latest, string current)
    {
        if (Version.TryParse(latest.TrimStart('v', 'V'), out var lv)
            && Version.TryParse(current.TrimStart('v', 'V'), out var cv))
            return lv > cv;
        return string.Compare(latest, current, StringComparison.OrdinalIgnoreCase) > 0;
    }

    /// <summary>解析 GitHub/Gitee 仓库地址 → (owner, repo)</summary>
    private static bool TryParseRepo(string repoUrl, out string owner, out string repo)
    {
        owner = repo = "";
        try
        {
            var uri = new Uri(repoUrl);
            var segs = uri.AbsolutePath.Trim('/').Split('/');
            if (segs.Length < 2) return false;
            owner = segs[0];
            repo = segs[1];
            return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>解析插件 manifest（version/download_url/notes）并对比本地版本</summary>
    private async Task<PluginUpdateInfo?> ParseManifestAsync(string manifestUrl, string? homepage, string currentVersion)
    {
        try
        {
            var json = await _httpClient.GetStringAsync(manifestUrl);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(version)) return null;
            return new PluginUpdateInfo
            {
                HasUpdate = IsNewerVersion(version, currentVersion),
                LatestVersion = version,
                DownloadUrl = root.TryGetProperty("download_url", out var d) ? d.GetString() : null,
                ReleaseNotes = root.TryGetProperty("notes", out var n) ? n.GetString() : null,
                Homepage = homepage
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>解析 Gitee 仓库 raw 文件 URL（master/main 分支自动探测；raw 免认证）</summary>
    private async Task<string?> ResolveGiteeRawAsync(string owner, string repo, string filePath)
    {
        foreach (var branch in new[] { "master", "main" })
        {
            var url = $"https://gitee.com/{owner}/{repo}/raw/{branch}/{filePath}";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                request.Headers.UserAgent.ParseAdd("CatClawMusic/1.0");
                using var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode) return url;
            }
            catch { }
        }
        return null;
    }

    /// <summary>下载文件到目标路径（带进度回调，0~100）</summary>
    private async Task<bool> DownloadToFileAsync(string url, string destPath, IProgress<(string, int)>? progress, int pctStart = 10, int pctEnd = 70)
    {
        using var downloadResponse = await _httpClient.GetAsync(url);
        downloadResponse.EnsureSuccessStatusCode();
        var totalBytes = downloadResponse.Content.Headers.ContentLength ?? -1;
        await using var remoteStream = await downloadResponse.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await remoteStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            totalRead += bytesRead;
            if (totalBytes > 0)
            {
                var pct = pctStart + (int)(totalRead * (pctEnd - pctStart) / totalBytes);
                progress?.Report(($"正在下载... ({totalRead * 100 / totalBytes}%)", Math.Min(pct, pctEnd)));
            }
        }
        return true;
    }

    /// <summary>
    /// 更新插件：下载新版本文件 → 卸载旧实例 → 安装新文件 → 重新加载注册。
    /// 返回更新后的 PluginInfo；失败返回 null（不破坏旧插件）。
    /// </summary>
    public async Task<PluginInfo?> UpdatePluginAsync(PluginInfo plugin, IProgress<(string, int)>? progress = null)
    {
        var update = await CheckPluginUpdateAsync(plugin);
        if (update?.HasUpdate != true || string.IsNullOrEmpty(update.DownloadUrl))
            return null;

        var tempPath = plugin.AssemblyPath + ".update.tmp";
        try
        {
            progress?.Report(("正在下载新版本...", 10));
            using var downloadResponse = await _httpClient.GetAsync(update.DownloadUrl);
            downloadResponse.EnsureSuccessStatusCode();
            var totalBytes = downloadResponse.Content.Headers.ContentLength ?? -1;
            await using var remoteStream = await downloadResponse.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write);
            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await remoteStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;
                if (totalBytes > 0)
                    progress?.Report(($"正在下载新版本... ({totalRead * 100 / totalBytes}%)", 10 + (int)(totalRead * 40 / totalBytes)));
            }

            // 下载成功后：卸载旧实例（Assembly.LoadFrom 不锁文件，删除安全）
            await UninstallPluginAsync(plugin.PluginTypeId);

            // 新文件名用 asset 名，避免与旧文件冲突；放入插件目录
            var newName = Path.GetFileName(new Uri(update.DownloadUrl).AbsolutePath);
            if (string.IsNullOrWhiteSpace(newName) || !newName.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase))
                newName = $"plugin-{update.LatestVersion}.ccp";
            var destPath = Path.Combine(_pluginsDir, newName);
            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tempPath, destPath);

            progress?.Report(("正在加载新版本...", 80));
            return await LoadAndRegisterPluginAsync(destPath, plugin.InstallUrl, progress);
        }
        catch (Exception ex)
        {
            Log.Debug("PluginManager", $"[PluginManager] 更新插件失败: {ex.Message}");
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return null;
        }
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

                    // 与 LoadAndRegisterPluginAsync 一致：强制运行模块初始化器，
                    // 确保插件内部 AssemblyResolve（嵌入资源依赖）先于 GetTypes() 生效
                    try { RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle); }
                    catch (Exception ex) { Log.Debug("PluginManager", $"[PluginManager] 运行插件模块初始化器失败: {ex.Message}"); }

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

}