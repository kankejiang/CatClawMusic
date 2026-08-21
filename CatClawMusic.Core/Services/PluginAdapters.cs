using System.Reflection;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 反射适配器子系统 —— 用于代理不同版本插件接口的适配器类集合。
/// 当插件 DLL 引用了不同版本的宿主程序集时，直接类型转换会失败；
/// 适配器通过反射调用目标对象的方法和属性，绕过 CLR 类型系统的版本兼容性检查。
/// </summary>

/// <summary>反射适配器共享的类型转换 / 异步调用辅助</summary>
internal static class PluginAdapterReflection
{
    internal static object? ConvertType(object? value, Type targetType)
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
    internal static T? ConvertType<T>(object? value) where T : class
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
    internal static async Task<object?> InvokeAsyncMethod(object target, string methodName, params object?[]? args)
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
internal class BasicPluginAdapter : IPlugin
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
            // 反射元数据缓存：属性/方法对给定 target 固定不变，构造时查一次，
            // 避免每次访问都执行 GetProperty/GetMethod 反射查找（音频热路径每帧调用）。
            _pluginIdProp = _targetType.GetProperty("PluginId");
            _nameProp = _targetType.GetProperty("Name");
            _versionProp = _targetType.GetProperty("Version");
            _authorProp = _targetType.GetProperty("Author");
            _descriptionProp = _targetType.GetProperty("Description");
            _capabilitiesProp = _targetType.GetProperty("Capabilities");
            _initializeMethod = _targetType.GetMethod("InitializeAsync");
            _shutdownMethod = _targetType.GetMethod("ShutdownAsync");
        }

        private readonly System.Reflection.PropertyInfo? _pluginIdProp, _nameProp, _versionProp, _authorProp, _descriptionProp, _capabilitiesProp;
        private readonly System.Reflection.MethodInfo? _initializeMethod, _shutdownMethod;

        /// <summary>
        /// 插件唯一标识，通过反射读取目标对象的 PluginId 属性
        /// </summary>
        public string PluginId => (string?)_pluginIdProp?.GetValue(_target) ?? "";

        /// <summary>
        /// 插件显示名称，通过反射读取目标对象的 Name 属性
        /// </summary>
        public string Name => (string?)_nameProp?.GetValue(_target) ?? "";

        /// <summary>
        /// 插件版本号，通过反射读取目标对象的 Version 属性
        /// </summary>
        public string Version => (string?)_versionProp?.GetValue(_target) ?? "";

        /// <summary>
        /// 插件作者，通过反射读取目标对象的 Author 属性
        /// </summary>
        public string Author => (string?)_authorProp?.GetValue(_target) ?? "";

        /// <summary>
        /// 插件描述，通过反射读取目标对象的 Description 属性
        /// </summary>
        public string Description => (string?)_descriptionProp?.GetValue(_target) ?? "";

        /// <summary>
        /// 插件能力列表，通过反射读取目标对象的 Capabilities 属性
        /// </summary>
        public List<string> Capabilities => (List<string>?)_capabilitiesProp?.GetValue(_target) ?? new();

        /// <summary>
        /// 异步初始化插件，通过反射调用目标对象的 InitializeAsync 方法
        /// </summary>
        public Task InitializeAsync() => (Task)_initializeMethod!.Invoke(_target, null)!;

        /// <summary>
        /// 异步关闭插件，通过反射调用目标对象的 ShutdownAsync 方法
        /// </summary>
        public Task ShutdownAsync() => (Task)_shutdownMethod!.Invoke(_target, null)!;
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
internal class CoverProviderAdapter : BasicPluginAdapter, ICoverProviderPlugin
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
                var converted = PluginAdapterReflection.ConvertType(song, paramType);
                invokeArgs = new[] { converted };
            }
            else
            {
                invokeArgs = new object?[] { song };
            }

            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "GetCoverAsync", invokeArgs);
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
internal class LyricsProviderAdapter : BasicPluginAdapter, ILyricsProviderPlugin
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
                var converted = PluginAdapterReflection.ConvertType(song, paramType);
                invokeArgs = new[] { converted };
            }
            else
            {
                invokeArgs = new object?[] { song };
            }

            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "GetLyricsAsync", invokeArgs);
            // 返回值通过 JSON 转换映射到宿主端的 LrcLyrics 类型
            return PluginAdapterReflection.ConvertType<LrcLyrics>(result);
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
internal class MenuContributorAdapter : BasicPluginAdapter, IMenuContributorPlugin
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
                var converted = PluginAdapterReflection.ConvertType(song, paramType);
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
                    var converted = PluginAdapterReflection.ConvertType<MenuItemEntry>(item);
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
                args[1] = PluginAdapterReflection.ConvertType(song, parameters[1].ParameterType);
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
internal class ProtocolProviderAdapter : BasicPluginAdapter, IProtocolProviderPlugin
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
            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "ListFilesAsync", path);
            if (result is List<RemoteFile> typed) return typed;

            // 直接转换失败，遍历 IList 逐项转换
            if (result is System.Collections.IList list)
            {
                var files = new List<RemoteFile>();
                foreach (var item in list)
                {
                    var converted = PluginAdapterReflection.ConvertType<RemoteFile>(item);
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
            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "OpenReadAsync", filePath);
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
                var converted = PluginAdapterReflection.ConvertType(profile, paramType);
                invokeArgs = new[] { converted };
            }
            else
            {
                invokeArgs = new object?[] { profile };
            }

            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "TestConnectionAsync", invokeArgs);
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
internal class AudioEnhancerAdapter : BasicPluginAdapter, IAudioEnhancerPlugin
    {
        /// <summary>
        /// 初始化音频增强器适配器
        /// </summary>
        /// <param name="target">要代理的目标音频增强器对象实例</param>
        public AudioEnhancerAdapter(object target) : base(target)
        {
            // 反射元数据缓存：ProcessSamples 在音频渲染回调中每帧调用，
            // GetMethod 缓存后热路径零反射查找开销
            _isEnabledProp = _targetType.GetProperty("IsEnabled");
            _processSamplesMethod = _targetType.GetMethod("ProcessSamples");
            _resetMethod = _targetType.GetMethod("Reset");
        }

        private readonly System.Reflection.PropertyInfo? _isEnabledProp;
        private readonly System.Reflection.MethodInfo? _processSamplesMethod;
        private readonly System.Reflection.MethodInfo? _resetMethod;

        /// <summary>
        /// 音频增强器是否启用。
        /// <para>
        /// 通过反射读写目标对象的 IsEnabled 属性。
        /// getter 直接读取，setter 检查属性是否可写后再设置。
        /// </para>
        /// </summary>
        public bool IsEnabled
        {
            get => (bool?)_isEnabledProp?.GetValue(_target) ?? false;
            set
            {
                if (_isEnabledProp?.CanWrite == true) _isEnabledProp.SetValue(_target, value);
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
            if (_processSamplesMethod == null) return samples;
            var result = _processSamplesMethod.Invoke(_target, new object[] { samples, sampleRate, channels });
            return result as float[] ?? samples;
        }

        /// <summary>
        /// 重置音频增强器状态，通过反射调用目标对象的 Reset 方法
        /// </summary>
        public void Reset()
        {
            _resetMethod?.Invoke(_target, null);
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
internal class OnlineMusicAdapter : BasicPluginAdapter, IOnlineMusicPlugin
    {
        /// <summary>初始化在线音乐音源适配器</summary>
        /// <param name="target">要代理的目标在线音乐音源对象实例</param>
        public OnlineMusicAdapter(object target) : base(target) { }

        /// <summary>来源平台标识，通过反射读取目标对象的 PlatformName 属性</summary>
        public string PlatformName => (string?)_targetType.GetProperty("PlatformName")?.GetValue(_target) ?? "";

        /// <summary>异步搜索歌曲</summary>
        public async Task<List<OnlineSong>?> SearchAsync(string keyword, int page = 1, int pageSize = 8)
        {
            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "SearchAsync", keyword, page, pageSize);
            if (result is List<OnlineSong> typed) return typed;
            if (result is System.Collections.IList list)
            {
                var songs = new List<OnlineSong>();
                foreach (var item in list)
                {
                    var converted = PluginAdapterReflection.ConvertType<OnlineSong>(item);
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
                invokeArgs = new[] { PluginAdapterReflection.ConvertType(song, paramType), quality };
            else
                invokeArgs = new object?[] { song, quality };

            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "GetPlayUrlAsync", invokeArgs);
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
                invokeArgs = new[] { PluginAdapterReflection.ConvertType(song, paramType) };
            else
                invokeArgs = new object?[] { song };

            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "GetLyricsAsync", invokeArgs);
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

        /// <summary>异步获取歌词（含罗马音）。目标插件未实现 GetLyricsWithRomaAsync 时返回 null。</summary>
        public async Task<(string? Lrc, string? TLrc, string? RLrc)?> GetLyricsWithRomaAsync(OnlineSong song)
        {
            var method = _targetType.GetMethod("GetLyricsWithRomaAsync");
            if (method == null) return null; // 旧插件无此方法：返回 null，宿主回退 GetLyricsAsync

            var paramType = method.GetParameters().FirstOrDefault()?.ParameterType;
            object?[]? invokeArgs;
            if (paramType != null && paramType.FullName == typeof(OnlineSong).FullName)
                invokeArgs = new[] { PluginAdapterReflection.ConvertType(song, paramType) };
            else
                invokeArgs = new object?[] { song };

            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "GetLyricsWithRomaAsync", invokeArgs);
            if (result == null) return null;

            string? lrc = null, tlrc = null, rlrc = null;
            var type = result.GetType();
            object? valueObj = result;
            if (type.IsValueType && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                var hasValue = type.GetProperty("HasValue")?.GetValue(result) as bool? ?? true;
                if (!hasValue) return null;
                valueObj = type.GetProperty("Value")?.GetValue(result);
                if (valueObj == null) return null;
            }
            lrc = valueObj.GetType().GetField("Item1")?.GetValue(valueObj) as string;
            tlrc = valueObj.GetType().GetField("Item2")?.GetValue(valueObj) as string;
            rlrc = valueObj.GetType().GetField("Item3")?.GetValue(valueObj) as string;
            return (lrc, tlrc, rlrc);
        }

        /// <summary>异步获取歌单列表</summary>
        public async Task<List<OnlinePlaylist>> GetPlaylistsAsync(string? category = null)
        {
            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "GetPlaylistsAsync", category);
            if (result is List<OnlinePlaylist> typed) return typed;
            if (result is System.Collections.IList list)
            {
                var items = new List<OnlinePlaylist>();
                foreach (var item in list)
                {
                    var converted = PluginAdapterReflection.ConvertType<OnlinePlaylist>(item);
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
                invokeArgs = new[] { PluginAdapterReflection.ConvertType(playlist, paramType), page, pageSize };
            else
                invokeArgs = new object?[] { playlist, page, pageSize };

            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "GetPlaylistSongsAsync", invokeArgs);
            if (result is List<OnlineSong> typed) return typed;
            if (result is System.Collections.IList list)
            {
                var songs = new List<OnlineSong>();
                foreach (var item in list)
                {
                    var converted = PluginAdapterReflection.ConvertType<OnlineSong>(item);
                    if (converted != null) songs.Add(converted);
                }
                return songs;
            }
            return null;
        }

        /// <summary>异步获取私人漫游（随机推荐）</summary>
        public async Task<List<OnlineSong>?> GetPrivateFmAsync(int num = 10)
        {
            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "GetPrivateFmAsync", num);
            if (result is List<OnlineSong> typed) return typed;
            if (result is System.Collections.IList list)
            {
                var songs = new List<OnlineSong>();
                foreach (var item in list)
                {
                    var converted = PluginAdapterReflection.ConvertType<OnlineSong>(item);
                    if (converted != null) songs.Add(converted);
                }
                return songs;
            }
            return null;
        }

        /// <summary>异步获取每日推荐歌曲</summary>
        public async Task<List<OnlineSong>?> GetDailyRecommendAsync(int num = 20)
        {
            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "GetDailyRecommendAsync", num);
            if (result is List<OnlineSong> typed) return typed;
            if (result is System.Collections.IList list)
            {
                var songs = new List<OnlineSong>();
                foreach (var item in list)
                {
                    var converted = PluginAdapterReflection.ConvertType<OnlineSong>(item);
                    if (converted != null) songs.Add(converted);
                }
                return songs;
            }
            return null;
        }

        /// <summary>异步获取排行榜列表</summary>
        public async Task<List<OnlinePlaylist>> GetToplistsAsync()
        {
            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "GetToplistsAsync");
            if (result is List<OnlinePlaylist> typed) return typed;
            if (result is System.Collections.IList list)
            {
                var items = new List<OnlinePlaylist>();
                foreach (var item in list)
                {
                    var converted = PluginAdapterReflection.ConvertType<OnlinePlaylist>(item);
                    if (converted != null) items.Add(converted);
                }
                return items;
            }
            return new();
        }

        /// <summary>异步歌单搜索；未实现返回 null。</summary>
        public async Task<List<OnlinePlaylist>?> SearchPlaylistsAsync(string keyword, int page = 1, int pageSize = 20)
        {
            // 老插件无此方法：DIM null 兜底返回 null
            var method = _targetType.GetMethod("SearchPlaylistsAsync");
            object? result;
            if (method == null) return null;
            var paramType = method.GetParameters().FirstOrDefault()?.ParameterType;
            object?[] invokeArgs;
            if (paramType != null && paramType.FullName == typeof(string).FullName)
                invokeArgs = new object?[] { keyword, page, pageSize };
            else
                invokeArgs = new object?[] { keyword, page, pageSize };
            result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "SearchPlaylistsAsync", invokeArgs);
            if (result is List<OnlinePlaylist> typed) return typed;
            if (result is System.Collections.IList list)
            {
                var items = new List<OnlinePlaylist>();
                foreach (var item in list)
                {
                    var converted = PluginAdapterReflection.ConvertType<OnlinePlaylist>(item);
                    if (converted != null) items.Add(converted);
                }
                return items;
            }
            return null;
        }

        /// <summary>获取浏览器登录配置（反射调用插件方法；未实现返回 null）</summary>
        public async Task<BrowserLoginInfo?> GetBrowserLoginInfoAsync()
        {
            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "GetBrowserLoginInfoAsync");
            return PluginAdapterReflection.ConvertType<BrowserLoginInfo>(result);
        }

        /// <summary>接收宿主从 WebView 提取的 Cookie，回传插件完成登录</summary>
        public async Task SetLoginCookieAsync(string cookie)
            => await PluginAdapterReflection.InvokeAsyncMethod(_target, "SetLoginCookieAsync", cookie);

        /// <summary>当前是否已登录</summary>
        public async Task<bool> IsLoggedInAsync()
        {
            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "IsLoggedInAsync");
            return result is bool b && b;
        }

        /// <summary>已登录账号昵称</summary>
        public async Task<string?> GetAccountNameAsync()
        {
            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "GetAccountNameAsync");
            return result as string;
        }

        /// <summary>退出登录</summary>
        public async Task LogoutAsync() => await PluginAdapterReflection.InvokeAsyncMethod(_target, "LogoutAsync");
    }

    /// <summary>
    /// 视图贡献者适配器：通过反射代理不同版本的 IViewContributorPlugin 实现。
    /// <para>
    /// EntryTitle/EntryIcon 属性通过反射读取；CreateEntryPage 通过反射调用目标方法，
    /// 直接返回目标对象创建的页面实例（object 形式），由宿主强制转换为 Page。
    /// </para>
    /// </summary>
internal class ViewContributorAdapter : BasicPluginAdapter, IViewContributorPlugin
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
    /// 快捷入口适配器：通过反射代理不同版本的 IQuickEntryPlugin 实现。
    /// <para>
    /// QuickEntries 按属性名（Id/Title/Icon/Subtitle/Color1/Color2）反射读取，
    /// 兼容跨版本条目类型；ExecuteQuickEntry 反射调用目标方法（目标版本未实现时静默降级）。
    /// </para>
    /// </summary>
internal class QuickEntryAdapter : BasicPluginAdapter, IQuickEntryPlugin
    {
        private readonly System.Reflection.MethodInfo? _executeQuickEntryMethod;

        /// <summary>初始化快捷入口适配器</summary>
        /// <param name="target">要代理的目标插件对象实例</param>
        public QuickEntryAdapter(object target) : base(target)
        {
            _executeQuickEntryMethod = _targetType.GetMethod("ExecuteQuickEntry");
        }

        /// <summary>注册的快捷入口卡片列表（按属性名反射读取，跨版本安全）</summary>
        public IReadOnlyList<QuickEntryInfo> QuickEntries
        {
            get
            {
                var result = new List<QuickEntryInfo>();
                try
                {
                    var value = _targetType.GetProperty("QuickEntries")?.GetValue(_target);
                    if (value is not System.Collections.IEnumerable enumerable) return result;
                    foreach (var item in enumerable)
                    {
                        if (item == null) continue;
                        var t = item.GetType();
                        result.Add(new QuickEntryInfo
                        {
                            Id = t.GetProperty("Id")?.GetValue(item) as string ?? "",
                            Title = t.GetProperty("Title")?.GetValue(item) as string ?? "",
                            Icon = t.GetProperty("Icon")?.GetValue(item) as string ?? "",
                            Subtitle = t.GetProperty("Subtitle")?.GetValue(item) as string ?? "",
                            Color1 = t.GetProperty("Color1")?.GetValue(item) as string ?? "#667eea",
                            Color2 = t.GetProperty("Color2")?.GetValue(item) as string ?? "#764ba2",
                            SortOrder = t.GetProperty("SortOrder")?.GetValue(item) is int so ? so : 0,
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("PluginAdapters", $"[QuickEntry] 读取快捷入口失败: {ex.Message}");
                }
                return result;
            }
        }

        /// <summary>执行指定快捷入口动作（传入宿主服务提供者）</summary>
        /// <param name="entryId">被点击的入口 Id</param>
        /// <param name="services">宿主服务提供者</param>
        public void ExecuteQuickEntry(string entryId, IServiceProvider services)
            => _executeQuickEntryMethod?.Invoke(_target, new object[] { entryId, services });
    }

    /// <summary>
    /// 主题提供者适配器：通过反射代理不同版本的 IThemeProviderPlugin 实现。
    /// <para>
    /// ThemeId/ThemeName/ThemeOrder 属性通过反射读取；GetThemeColorsAsync/GetThemeBackgroundAsync
    /// 通过 InvokeAsyncMethod 反射调用。色板为 BCL 类型（Dictionary&lt;string,string&gt;），
    /// 不涉自定义类型跨版本问题，可直接使用。
    /// </para>
    /// </summary>
internal class ThemeProviderAdapter : BasicPluginAdapter, IThemeProviderPlugin
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
            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "GetThemeColorsAsync", null);
            return result as Dictionary<string, string>;
        }

        /// <summary>获取主题背景图片源，通过反射调用目标方法</summary>
        public async Task<string?> GetThemeBackgroundAsync()
        {
            if (_targetType.GetMethod("GetThemeBackgroundAsync") == null) return null;
            var result = await PluginAdapterReflection.InvokeAsyncMethod(_target, "GetThemeBackgroundAsync", null);
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
internal class PlayerPageAdapter : BasicPluginAdapter, IPlayerPagePlugin
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
internal class AudioVisualizerAdapter : BasicPluginAdapter, IAudioVisualizerPlugin
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
internal class ConfigurableAdapter : BasicPluginAdapter, IPluginConfigurable
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
