using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 运行时插件信息
/// </summary>
public class PluginInfo
{
    /// <summary>插件类型标识，格式 "{接口短名}.{Plugin.Name}"，如 "LyricsProvider.NetEaseLyrics"</summary>
    public string PluginTypeId { get; set; } = string.Empty;

    /// <summary>原始插件实例</summary>
    public IPlugin Plugin { get; set; } = null!;

    /// <summary>是否启用（从 Preferences 加载）</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>展示名称</summary>
    public string DisplayName => Plugin.Name;

    /// <summary>版本</summary>
    public string Version => Plugin.Version;

    /// <summary>作者</summary>
    public string Author => Plugin.Author;

    /// <summary>描述</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>插件类型分类（用于 UI 分组）</summary>
    public PluginCategory Category { get; set; }

    /// <summary>插件图标 emoji</summary>
    public string IconEmoji { get; set; } = "🧩";
}

/// <summary>
/// 插件类型分类
/// </summary>
public enum PluginCategory
{
    /// <summary>歌词源</summary>
    LyricsProvider,

    /// <summary>协议</summary>
    ProtocolProvider,

    /// <summary>其他</summary>
    Other
}
