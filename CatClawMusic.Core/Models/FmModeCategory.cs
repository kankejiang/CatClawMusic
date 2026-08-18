namespace CatClawMusic.Core.Models;

/// <summary>
/// 私人漫游（FM）推荐模式/场景分类（由插件声明，宿主渲染为抽屉选择器）。
/// </summary>
public class FmModeCategory
{
    /// <summary>分类类型："mode"=推荐模式（默认/熟悉/探索），"scene"=场景模式</summary>
    public string Type { get; set; } = "mode";

    /// <summary>模式代码（传给 TrySetFmModeAsync，如 DEFAULT / FAMILIAR / EXPLORE / ROCK / JAZZ）</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>显示名称（如"默认模式"、"摇滚"）</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>副标题/描述（如"沿着目前喜好继续聆听"；可为空）</summary>
    public string? SubTitle { get; set; }

    /// <summary>图标 emoji（如🎸）；可为空</summary>
    public string? Icon { get; set; }
}
