using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.WinUI.Infrastructure;

namespace CatClawMusic.WinUI.Services;

/// <summary>
/// 插件宿主服务：把 Core 的 <see cref="PluginManager"/> 接到 WinUI 的插件目录与设置存储上。
/// 宿主本身不内置任何插件实例（`plugins` 传空），能力全部来自用户安装的 `.ccp`。
/// </summary>
public sealed class PluginService
{
    public PluginService(JsonSettingsStore settings)
    {
        Manager = new PluginManager(
            plugins: [],
            getPrefFunc: typeId => settings.Get($"plugin_enabled_{typeId}", true),
            setPrefFunc: (typeId, enabled) => settings.Set($"plugin_enabled_{typeId}", enabled),
            pluginsDir: AppPaths.PluginDirectory);
    }

    public PluginManager Manager { get; }

    /// <summary>初始化所有已启用插件（启动时后台跑，失败只记日志）</summary>
    public async Task InitializeAsync()
    {
        try
        {
            await Manager.InitializeAllAsync();
            Log.Info("plugin", $"插件初始化完成，共 {Manager.GetAllPlugins().Count} 个");
        }
        catch (Exception ex)
        {
            Log.Error("plugin", $"插件初始化失败：{ex}");
        }
    }
}
