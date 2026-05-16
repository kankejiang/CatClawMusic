using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 插件管理器接口，负责插件的发现、加载、启用/禁用和安装/卸载
/// </summary>
public interface IPluginManager
{
    /// <summary>获取所有插件信息</summary>
    List<PluginInfo> GetAllPlugins();
    /// <summary>获取指定类型的所有已启用插件实例</summary>
    List<T> GetEnabledPlugins<T>() where T : IPlugin;
    /// <summary>判断指定插件是否已启用</summary>
    bool IsPluginEnabled(string pluginTypeId);
    /// <summary>设置插件的启用状态</summary>
    void SetPluginEnabled(string pluginTypeId, bool enabled);
    /// <summary>初始化所有已启用的插件</summary>
    Task InitializeAllAsync();
    /// <summary>关闭所有已启用的插件</summary>
    Task ShutdownAllAsync();
    /// <summary>从本地文件安装插件</summary>
    Task<PluginInfo?> InstallFromLocalFileAsync(string filePath, IProgress<(string, int)>? progress = null);
    /// <summary>从 GitHub Release 安装插件</summary>
    Task<PluginInfo?> InstallFromGitHubAsync(string repoUrl, IProgress<(string, int)>? progress = null);
    /// <summary>卸载指定插件</summary>
    Task<bool> UninstallPluginAsync(string pluginTypeId);
}
