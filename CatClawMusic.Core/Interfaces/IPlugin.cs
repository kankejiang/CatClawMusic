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

    /// <summary>
    /// 更新源地址（可选）。为空时走 GitHub 约定：用安装时记录的仓库地址查 releases/latest
    /// 的 tag 对比版本。非 GitHub 托管的插件可返回 manifest JSON 地址，格式：
    /// {"version":"1.2.0","download_url":"https://...","notes":"更新说明"}。
    /// </summary>
    string UpdateUrl => "";
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
/// 视图贡献者插件接口：插件向宿主贡献一个完整的入口页面。
/// <para>
/// 宿主在发现页/导航栏渲染所有已启用的 <see cref="IViewContributorPlugin"/> 入口，
/// 用户点击后宿主调用 <see cref="CreateEntryPage"/> 获取页面实例（实际类型为 MAUI ContentPage），
/// 通过 <c>Shell.Current.Navigation.PushAsync</c> 推入导航栈。
/// </para>
/// <para>
/// 设计动机：客户端不再内置"在线音乐"页面，由插件自行提供完整 UI 与逻辑，
/// 实现真正的"客户端空壳，插件自治"。
/// </para>
/// <para>
/// 注意：返回类型为 <see cref="object"/> 而非 <c>ContentPage</c>，
/// 是为了避免 Core 项目依赖 Microsoft.Maui.Controls。宿主收到实例后强制转换为
/// <c>Microsoft.Maui.Controls.Page</c> 即可。
/// </para>
/// </summary>
public interface IViewContributorPlugin : IPlugin
{
    /// <summary>入口显示标题（如"在线音乐"）</summary>
    string EntryTitle { get; }

    /// <summary>入口图标（Emoji 或图片资源名）</summary>
    string EntryIcon { get; }

    /// <summary>
    /// 创建并返回入口页面实例。
    /// <para>
    /// 每次调用应返回新实例。宿主会将其强制转换为 <c>Microsoft.Maui.Controls.Page</c>。
    /// 通过 <paramref name="services"/> 可获取宿主注册的服务（如 PlayQueue、IAudioPlayerService 等）。
    /// </para>
    /// </summary>
    /// <param name="services">宿主的服务提供者，用于解析宿主服务</param>
    /// <returns>MAUI ContentPage 实例（以 object 形式返回避免 Core 依赖 Maui）</returns>
    object CreateEntryPage(IServiceProvider services);
}

/// <summary>
/// 扩展歌词能力插件接口：声明插件提供扩展歌词（译文 / 罗马音）显示能力。
/// <para>
/// 宿主歌词设置弹窗检测到已启用的本接口实现时，才显示「扩展歌词」分区
/// （显示歌词译文 / 显示罗马音开关），译文/罗马音也参与歌词渲染；
/// 未加载提供该能力的插件时，分区隐藏且译文/罗马音不显示。
/// 扩展歌词功能由插件自治（宿主空壳架构）。
/// </para>
/// </summary>
public interface IExtendedLyricsPlugin : IPlugin
{
    /// <summary>歌词设置弹窗中的分区标题（如「扩展歌词」）</summary>
    string ExtensionTitle { get; }
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
