using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

public interface IPluginManager
{
    List<PluginInfo> GetAllPlugins();
    List<T> GetEnabledPlugins<T>() where T : IPlugin;
    bool IsPluginEnabled(string pluginTypeId);
    void SetPluginEnabled(string pluginTypeId, bool enabled);
    Task InitializeAllAsync();
    Task ShutdownAllAsync();
    Task<PluginInfo?> InstallFromLocalFileAsync(string filePath, IProgress<(string, int)>? progress = null);
    Task<PluginInfo?> InstallFromGitHubAsync(string repoUrl, IProgress<(string, int)>? progress = null);
    Task<bool> UninstallPluginAsync(string pluginTypeId);
}
