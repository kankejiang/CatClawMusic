using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 插件管理器接口
/// </summary>
public interface IPluginManager
{
    /// <summary>获取所有已注册插件信息</summary>
    List<PluginInfo> GetAllPlugins();

    /// <summary>获取已启用的指定类型插件实例列表</summary>
    List<T> GetEnabledPlugins<T>() where T : IPlugin;

    /// <summary>检查插件是否启用</summary>
    bool IsPluginEnabled(string pluginTypeId);

    /// <summary>设置插件启用/禁用（更新内存 + 持久化）</summary>
    void SetPluginEnabled(string pluginTypeId, bool enabled);

    /// <summary>初始化所有已启用插件</summary>
    Task InitializeAllAsync();

    /// <summary>优雅关闭所有已启用插件</summary>
    Task ShutdownAllAsync();
}
