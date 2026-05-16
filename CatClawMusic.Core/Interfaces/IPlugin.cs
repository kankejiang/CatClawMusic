using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 插件基础接口，所有插件类型均需实现
/// </summary>
public interface IPlugin
{
    /// <summary>插件唯一标识</summary>
    string PluginId { get; }
    /// <summary>插件名称</summary>
    string Name { get; }
    /// <summary>版本号</summary>
    string Version { get; }
    /// <summary>作者</summary>
    string Author { get; }
    /// <summary>描述信息</summary>
    string Description { get; }
    /// <summary>能力列表</summary>
    List<string> Capabilities { get; }
    /// <summary>初始化插件</summary>
    Task InitializeAsync();
    /// <summary>关闭插件</summary>
    Task ShutdownAsync();
}

/// <summary>
/// 歌词提供者插件接口
/// </summary>
public interface ILyricsProviderPlugin : IPlugin
{
    /// <summary>获取指定歌曲的歌词</summary>
    Task<LrcLyrics?> GetLyricsAsync(Song song);
    /// <summary>歌词服务是否可用</summary>
    bool IsAvailable { get; }
}

/// <summary>
/// 协议提供者插件接口
/// </summary>
public interface IProtocolProviderPlugin : IPlugin
{
    /// <summary>协议名称</summary>
    string ProtocolName { get; }
    /// <summary>列出指定路径下的文件</summary>
    Task<List<RemoteFile>> ListFilesAsync(string path);
    /// <summary>打开远程文件读取流</summary>
    Task<Stream> OpenReadAsync(string filePath);
    /// <summary>测试连接配置是否有效</summary>
    Task<bool> TestConnectionAsync(ConnectionProfile profile);
}

/// <summary>
/// 封面提供者插件接口
/// </summary>
public interface ICoverProviderPlugin : IPlugin
{
    /// <summary>获取指定歌曲的封面图片</summary>
    Task<byte[]?> GetCoverAsync(Song song);
    /// <summary>封面服务是否可用</summary>
    bool IsAvailable { get; }
}

/// <summary>
/// 音频增强器插件接口
/// </summary>
public interface IAudioEnhancerPlugin : IPlugin
{
    /// <summary>是否启用增强效果</summary>
    bool IsEnabled { get; set; }
    /// <summary>处理音频采样数据</summary>
    float[] ProcessSamples(float[] samples, int sampleRate, int channels);
    /// <summary>重置增强器状态</summary>
    void Reset();
}

/// <summary>
/// 菜单贡献者插件接口
/// </summary>
public interface IMenuContributorPlugin : IPlugin
{
    /// <summary>获取菜单项列表</summary>
    List<MenuItemEntry> GetMenuItems(Song song);
    /// <summary>菜单项点击回调</summary>
    Task OnMenuItemClicked(int itemId, Song song, object fragment);
}

/// <summary>
/// 菜单项条目
/// </summary>
public class MenuItemEntry
{
    /// <summary>菜单项 ID</summary>
    public int Id { get; set; }
    /// <summary>菜单项标题</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>无参构造函数</summary>
    public MenuItemEntry() { }

    /// <summary>带参构造函数</summary>
    public MenuItemEntry(int id, string title)
    {
        Id = id;
        Title = title;
    }
}
