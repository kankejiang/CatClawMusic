using Microsoft.Maui.Graphics;

namespace CatClawMusic.Maui.Helpers;

/// <summary>
/// 萌系糖果色板：发现页 Hero 卡等品牌渐变的单一来源。
/// 低饱和糖果系，与樱粉 Primary(#FF8FB8) 同族；深浅模式通用（卡上均为白字）。
/// </summary>
public static class MoePalette
{
    /// <summary>Hero 卡渐变（槽位顺序固定：每日推荐/最多播放/我的最爱/随机播放/AI 推荐）</summary>
    public static readonly (Color Start, Color End)[] HeroGradients =
    {
        (Color.FromArgb("#FF8FB8"), Color.FromArgb("#F56FA0")), // 樱粉
        (Color.FromArgb("#FFB86E"), Color.FromArgb("#FF8A65")), // 蜜桃杏
        (Color.FromArgb("#7ED8C3"), Color.FromArgb("#4FC3A1")), // 薄荷
        (Color.FromArgb("#B79CFF"), Color.FromArgb("#9F82F0")), // 芋紫
        (Color.FromArgb("#55D6FF"), Color.FromArgb("#7ED8C3")), // 冰青薄荷
    };

    /// <summary>Yuki/AI 卡渐变（薄荷 → 冰青）</summary>
    public static readonly (Color Start, Color End) YukiCard =
        (Color.FromArgb("#7ED8C3"), Color.FromArgb("#55D6FF"));

    /// <summary>扩展插件入口卡图标容器的糖果底色（按名称哈希取色，白图标）</summary>
    public static readonly Color[] EntryIconTints =
    {
        Color.FromArgb("#FF8FB8"), // 樱粉
        Color.FromArgb("#7ED8C3"), // 薄荷
        Color.FromArgb("#FFB86E"), // 杏黄
        Color.FromArgb("#B79CFF"), // 芋紫
    };

    /// <summary>按字符串稳定哈希取一个糖果色（同一插件每次同色）</summary>
    public static Color TintFor(string? name)
    {
        if (string.IsNullOrEmpty(name)) return EntryIconTints[0];
        uint hash = 2166136261;
        foreach (var c in name)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return EntryIconTints[hash % EntryIconTints.Length];
    }
}
