using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 插件管理器实现
/// </summary>
public class PluginManager : IPluginManager
{
    private readonly List<PluginInfo> _plugins = new();
    private readonly Func<string, bool> _getPrefFunc;
    private readonly Action<string, bool> _setPrefFunc;

    /// <summary>
    /// 创建插件管理器
    /// </summary>
    /// <param name="plugins">所有注册的插件实例</param>
    /// <param name="getPrefFunc">从持久化读取插件启用状态的函数，键为 "plugin_enabled_{PluginTypeId}"</param>
    /// <param name="setPrefFunc">向持久化写入插件启用状态的函数</param>
    public PluginManager(
        IEnumerable<IPlugin> plugins,
        Func<string, bool> getPrefFunc,
        Action<string, bool> setPrefFunc)
    {
        _getPrefFunc = getPrefFunc ?? throw new ArgumentNullException(nameof(getPrefFunc));
        _setPrefFunc = setPrefFunc ?? throw new ArgumentNullException(nameof(setPrefFunc));

        foreach (var plugin in plugins)
        {
            var info = CreatePluginInfo(plugin);
            info.IsEnabled = _getPrefFunc($"plugin_enabled_{info.PluginTypeId}");
            _plugins.Add(info);
        }
    }

    /// <inheritdoc />
    public List<PluginInfo> GetAllPlugins()
    {
        return _plugins.ToList();
    }

    /// <inheritdoc />
    public List<T> GetEnabledPlugins<T>() where T : IPlugin
    {
        return _plugins
            .Where(p => p.IsEnabled && p.Plugin is T)
            .Select(p => (T)p.Plugin)
            .ToList();
    }

    /// <inheritdoc />
    public bool IsPluginEnabled(string pluginTypeId)
    {
        var plugin = _plugins.FirstOrDefault(p => p.PluginTypeId == pluginTypeId);
        return plugin?.IsEnabled ?? false;
    }

    /// <inheritdoc />
    public void SetPluginEnabled(string pluginTypeId, bool enabled)
    {
        var plugin = _plugins.FirstOrDefault(p => p.PluginTypeId == pluginTypeId);
        if (plugin == null) return;

        plugin.IsEnabled = enabled;
        _setPrefFunc($"plugin_enabled_{pluginTypeId}", enabled);
    }

    /// <inheritdoc />
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
                // 初始化失败的插件标记为禁用，不影响其他插件
                info.IsEnabled = false;
            }
        }
    }

    /// <inheritdoc />
    public async Task ShutdownAllAsync()
    {
        foreach (var info in _plugins.Where(p => p.IsEnabled))
        {
            try
            {
                await info.Plugin.ShutdownAsync();
            }
            catch
            {
                // 关闭失败的插件忽略异常
            }
        }
    }

    /// <summary>
    /// 根据 IPlugin 实例创建 PluginInfo，自动判断 Category 和 PluginTypeId
    /// </summary>
    private static PluginInfo CreatePluginInfo(IPlugin plugin)
    {
        string pluginTypeId;
        PluginCategory category;
        string iconEmoji;

        if (plugin is ILyricsProviderPlugin)
        {
            pluginTypeId = $"LyricsProvider.{plugin.Name}";
            category = PluginCategory.LyricsProvider;
            iconEmoji = "🎵";
        }
        else if (plugin is IProtocolProviderPlugin)
        {
            pluginTypeId = $"ProtocolProvider.{plugin.Name}";
            category = PluginCategory.ProtocolProvider;
            iconEmoji = "🔌";
        }
        else
        {
            pluginTypeId = $"Other.{plugin.Name}";
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
