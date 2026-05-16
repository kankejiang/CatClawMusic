using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 插件信息模型，封装插件元数据和运行时实例
/// </summary>
public class PluginInfo
{
    /// <summary>插件类型唯一标识（如 LyricsProvider.xxx）</summary>
    public string PluginTypeId { get; set; } = string.Empty;

    /// <summary>插件实例</summary>
    public IPlugin Plugin { get; set; } = null!;

    /// <summary>子插件列表</summary>
    public List<IPlugin> SubPlugins { get; set; } = new();

    /// <summary>是否已启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>显示名称覆盖值</summary>
    public string? DisplayNameOverride { get; set; }

    /// <summary>显示名称（优先使用覆盖值）</summary>
    public string DisplayName => DisplayNameOverride ?? Plugin.Name;

    /// <summary>版本号</summary>
    public string Version => Plugin.Version;

    /// <summary>作者</summary>
    public string Author => Plugin.Author;

    /// <summary>描述文本</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>能力列表</summary>
    public List<string> Capabilities => Plugin.Capabilities;

    /// <summary>插件分类</summary>
    public PluginCategory Category { get; set; }

    /// <summary>分类图标 Emoji</summary>
    public string IconEmoji { get; set; } = "🧩";

    /// <summary>来源类型（内置 / 已安装）</summary>
    public PluginSource Source { get; set; } = PluginSource.BuiltIn;

    /// <summary>程序集文件路径</summary>
    public string? AssemblyPath { get; set; }

    /// <summary>是否可卸载（仅已安装来源）</summary>
    public bool CanUninstall => Source == PluginSource.Installed;

    /// <summary>安装源 URL</summary>
    public string? InstallUrl { get; set; }
}

/// <summary>
/// 插件分类
/// </summary>
public enum PluginCategory
{
    /// <summary>歌词提供者</summary>
    LyricsProvider,
    /// <summary>协议提供者</summary>
    ProtocolProvider,
    /// <summary>封面提供者</summary>
    CoverProvider,
    /// <summary>音频增强器</summary>
    AudioEnhancer,
    /// <summary>菜单贡献者</summary>
    MenuContributor,
    /// <summary>其他</summary>
    Other
}

/// <summary>
/// 插件来源
/// </summary>
public enum PluginSource
{
    /// <summary>内置</summary>
    BuiltIn,
    /// <summary>用户安装</summary>
    Installed
}
