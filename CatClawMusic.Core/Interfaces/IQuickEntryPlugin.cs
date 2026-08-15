namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 快捷入口卡片信息：插件向宿主发现页 HeroTrack 注册的入口卡（AI 卡之后展示）。
/// 宿主只读字段渲染卡片；点击时回调 <see cref="IQuickEntryPlugin.ExecuteQuickEntry"/>。
/// </summary>
public class QuickEntryInfo
{
    /// <summary>入口唯一标识（插件内唯一，用于点击回调定位）</summary>
    public string Id { get; set; } = "";

    /// <summary>标题（如"私人漫游"）</summary>
    public string Title { get; set; } = "";

    /// <summary>图标（Emoji 或图片资源名）</summary>
    public string Icon { get; set; } = "";

    /// <summary>副标题（如"随机推荐 · 电台"）</summary>
    public string Subtitle { get; set; } = "";

    /// <summary>卡片渐变起始色（#RRGGBB）</summary>
    public string Color1 { get; set; } = "#667eea";

    /// <summary>卡片渐变结束色（#RRGGBB）</summary>
    public string Color2 { get; set; } = "#764ba2";

    /// <summary>
    /// 排序权重（升序，越小越靠前）。并列时按插件注册顺序排列（先注册在前），保证确定性不冲突。
    /// </summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// 快捷入口插件：插件向宿主发现页 HeroTrack 注册一张或多张入口卡。
/// <para>
/// 宿主发现页在 AI 助手卡之后渲染所有已启用插件的 <see cref="QuickEntries"/> 卡片；
/// 用户点击某张卡 → 宿主调用 <see cref="ExecuteQuickEntry"/>（传入宿主服务提供者，
/// 插件完全自主决定动作，如直接启动播放，无需打开页面）。
/// </para>
/// <para>
/// 设计动机：通用口子——新增插件快捷入口无需改动宿主；老插件未实现本接口时
/// 宿主静默跳过，不影响既有功能。
/// </para>
/// </summary>
public interface IQuickEntryPlugin : IPlugin
{
    /// <summary>注册的快捷入口卡片列表（可多张）</summary>
    IReadOnlyList<QuickEntryInfo> QuickEntries { get; }

    /// <summary>
    /// 执行指定快捷入口动作。插件可自行解析宿主服务（播放器/队列等）直接执行
    /// （如直接启动私人漫游电台播放），是否打开入口页面由插件自行决定。
    /// </summary>
    /// <param name="entryId">被点击的 <see cref="QuickEntryInfo.Id"/></param>
    /// <param name="services">宿主服务提供者（用于解析 PlayQueue、IAudioPlayerService 等）</param>
    void ExecuteQuickEntry(string entryId, IServiceProvider services);
}
