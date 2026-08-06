namespace CatClawMusic.Core.Models;

/// <summary>
/// 在线音源功能入口（由插件声明，宿主动态渲染为卡片/横幅）。
/// <para>
/// 让插件自定义首屏展示哪些特色功能入口（如私人漫游、每日推荐、排行榜），
/// 宿主不再硬编码任何入口 —— 没有声明的音源首屏直接展示分类歌单。
/// </para>
/// </summary>
public class OnlineFeatureEntry
{
    /// <summary>功能标识（宿主点击时回传此 Key，由插件决定调用哪个能力）</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>入口标题（如 "私人漫游"）</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>入口副标题（如 "随机推荐，想听就听"）</summary>
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>入口图标 emoji（如 "🎧"）</summary>
    public string Icon { get; set; } = "🎵";

    /// <summary>渐变起始色（#RRGGBB）</summary>
    public string GradientFrom { get; set; } = "#667eea";

    /// <summary>渐变结束色（#RRGGBB）</summary>
    public string GradientTo { get; set; } = "#764ba2";

    /// <summary>
    /// 布局样式："card" 为半宽小卡片（两列），"banner" 为通栏大卡片。
    /// 宿主据此决定渲染宽度。
    /// </summary>
    public string Layout { get; set; } = "card";
}
