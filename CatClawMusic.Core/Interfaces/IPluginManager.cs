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
    /// <summary>检查插件是否有新版本（UpdateUrl manifest 或 GitHub releases/latest 对比）</summary>
    Task<PluginUpdateInfo?> CheckPluginUpdateAsync(PluginInfo plugin);
    /// <summary>更新插件：下载新版本 → 替换 → 重载，返回更新后的 PluginInfo</summary>
    Task<PluginInfo?> UpdatePluginAsync(PluginInfo plugin, IProgress<(string, int)>? progress = null);
}

/// <summary>插件更新信息（CheckPluginUpdateAsync 的返回结果）</summary>
public class PluginUpdateInfo
{
    /// <summary>是否存在新版本</summary>
    public bool HasUpdate { get; set; }
    /// <summary>最新版本号</summary>
    public string LatestVersion { get; set; } = "";
    /// <summary>新版本下载地址</summary>
    public string? DownloadUrl { get; set; }
    /// <summary>更新说明（Release notes 或 manifest notes）</summary>
    public string? ReleaseNotes { get; set; }
    /// <summary>项目主页（GitHub 仓库地址等）</summary>
    public string? Homepage { get; set; }
}
