namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 播放页提供者插件接口：插件向宿主提供一个可替换的播放页内容视图，
/// 宿主在 NowPlaying 页面的内容区挂载插件提供的视图（宿主保留外壳与播放控制条）。
/// </summary>
/// <para>
/// 设计动机：宿主播放页（NowPlayingPage）目前是固定 ContentPage、布局硬编码。
/// 本接口让插件提供自定义播放页内容（布局/样式/交互），宿主负责挂载与生命周期，
/// 实现"播放页插件化"。
/// </para>
/// <para>
/// 注意：返回类型为 <see cref="object"/> 而非 MAUI View，
/// 是为了避免 Core 项目依赖 Microsoft.Maui.Controls。宿主收到实例后强制转换为
/// <c>Microsoft.Maui.Controls.View</c> 即可。
/// </para>
/// </summary>
public interface IPlayerPagePlugin : IPlugin
{
    /// <summary>播放页显示名称（用于选择器，如 "沉浸歌词模式"）</summary>
    string PlayerPageName { get; }

    /// <summary>
    /// 优先级，数字越大越优先；同优先级按插件名称排序。
    /// 宿主内置播放页视为 0。多个插件竞争时取优先级最高者。
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 创建并返回播放页内容视图（MAUI View，宿主将强转挂载）。
    /// <para>
    /// 每次调用应返回新实例。宿主收到后强制转换为 <c>Microsoft.Maui.Controls.View</c>。
    /// 通过 <paramref name="services"/> 可获取宿主服务（如 PlayQueue、IAudioPlayerService、ILyricsService 等）。
    /// </para>
    /// </summary>
    /// <param name="services">宿主的服务提供者，用于解析宿主服务</param>
    /// <returns>MAUI View 实例（以 object 形式返回避免 Core 依赖 Maui）</returns>
    object CreatePlayerView(IServiceProvider services);
}
