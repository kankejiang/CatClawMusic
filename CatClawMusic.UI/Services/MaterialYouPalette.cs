using Android.Graphics;

namespace CatClawMusic.UI.Services;

/// <summary>
/// Material You 色调方案：从种子色生成完整的 UI 配色（背景、表面、文字、主题色等）
/// <para>
/// 基于 HSV 色彩空间的感知均匀色调映射，模拟 Android 12+ 莫奈（Monet）主题算法。
/// 核心思路：保持种子色的色相（Hue）不变，通过调整饱和度（Saturation）和明度（Value），
/// 生成一组在视觉上协调统一的配色方案。
/// </para>
/// <para>
/// 色调映射算法概述：
/// <list type="bullet">
///   <item>背景色（Background）：极低饱和度 + 高明度，呈现柔和的种子色底色</item>
///   <item>表面色（Surface）：略高于背景的饱和度 + 略低于背景的明度，形成层次感</item>
///   <item>表面文字色（OnSurface）：极低饱和度 + 极低明度，保证在浅色表面上的可读性</item>
///   <item>表面次要文字色（OnSurfaceVariant）：低饱和度 + 中等明度，用于次要信息</item>
///   <item>主题色（Primary）：保持或增强饱和度 + 中等明度，作为强调色</item>
///   <item>深色背景文字色（OnSurfaceForDarkBg）：极低饱和度 + 高明度，用于深色背景上的文字</item>
///   <item>轮廓色（Outline）：极低饱和度 + 较高明度，用于边框和分割线</item>
///   <item>辉光强调色（GlowAccent）：中等饱和度 + 中等明度，用于光晕和强调效果</item>
/// </list>
/// </para>
/// <para>
/// Lerp 插值说明：每个颜色的明度（V）通过 Lerp(seed, target, ratio) 计算，
/// ratio 越大越保留种子色的明度特征，ratio 越小越趋向目标明度。
/// 这样可以在保持种子色风格的同时，确保各颜色在明度上有足够的区分度。
/// </para>
/// </summary>
public class MaterialYouPalette
{
    /// <summary>
    /// 背景色 — 页面最底层的大面积底色
    /// <para>算法：饱和度降至种子色的 40%（上限 22%），明度向 0.88 插值（ratio=0.40，更多保留种子色特征）</para>
    /// <para>效果：呈现柔和的种子色调，作为整体页面的底色，与封面色彩更贴合</para>
    /// </summary>
    public int Background { get; set; }

    /// <summary>
    /// 表面色 — 卡片、对话框等浮层组件的底色
    /// <para>算法：饱和度降至种子色的 35%（上限 18%），明度向 0.84 插值（ratio=0.35）</para>
    /// <para>效果：比背景色略深，形成视觉层次，同时保持与背景的色调统一</para>
    /// </summary>
    public int Surface { get; set; }

    /// <summary>
    /// 表面文字色 — 在浅色表面（Surface/Background）上显示的主要文字颜色
    /// <para>算法：饱和度降至种子色的 15%（上限 5%），明度向 0.12 插值（ratio=0.33，偏向低明度）</para>
    /// <para>效果：接近黑色但带有微弱的种子色调，确保在浅色背景上的高对比度可读性</para>
    /// </summary>
    public int OnSurface { get; set; }

    /// <summary>
    /// 表面次要文字色 — 在浅色表面上显示的次要/辅助文字颜色
    /// <para>算法：饱和度降至种子色的 25%（上限 12%），明度向 0.42 插值（ratio=0.28）</para>
    /// <para>效果：中等灰度，用于副标题、描述文字等不需要高强调的信息</para>
    /// </summary>
    public int OnSurfaceVariant { get; set; }

    /// <summary>
    /// 主题色/强调色 — 按钮、选中态、高亮等核心交互元素的颜色
    /// <para>算法：饱和度取种子色与 0.25 的较大值（确保最低饱和度），明度向 0.50 插值（ratio=0.40）</para>
    /// <para>效果：保持种子色的鲜艳特征，作为页面中最醒目的颜色</para>
    /// </summary>
    public int Primary { get; set; }

    /// <summary>
    /// 深色背景文字色 — 在深色/主题色背景上显示的文字颜色
    /// <para>算法：饱和度降至种子色的 10%（上限 4%），明度固定为 0.92</para>
    /// <para>效果：接近白色但带有微弱的种子色调，确保在深色背景上的可读性</para>
    /// </summary>
    public int OnSurfaceForDarkBg { get; set; }

    /// <summary>
    /// 轮廓色 — 边框、分割线、卡片描边等
    /// <para>算法：饱和度降至种子色的 10%（上限 6%），明度向 0.80 插值（ratio=0.22）</para>
    /// <para>效果：浅灰色调，提供视觉边界但不抢夺注意力</para>
    /// </summary>
    public int Outline { get; set; }

    /// <summary>
    /// 辉光强调色 — 光晕效果、渐变高亮等装饰性颜色
    /// <para>算法：饱和度降至种子色的 65%（上限 50%），明度向 0.60 插值（ratio=0.45）</para>
    /// <para>效果：比主题色更柔和但仍有色彩感，用于背景光晕、渐变等装饰效果</para>
    /// </summary>
    public int GlowAccent { get; set; }

    /// <summary>
    /// 从种子色（封面主色调）生成完整 Material You 配色方案
    /// <para>
    /// 算法流程：
    /// <list type="number">
    ///   <item>将种子色从 RGB 转换为 HSV 色彩空间，提取色相（H）、饱和度（S）、明度（V）</item>
    ///   <item>保持色相不变，对每个配色角色分别调整饱和度和明度</item>
    ///   <item>饱和度调整：乘以系数并设置上限，降低色彩浓度以适应大面积使用</item>
    ///   <item>明度调整：通过 Lerp 将种子色明度向目标值插值，ratio 控制保留种子色特征的程度</item>
    ///   <item>将调整后的 HSV 值转回 ARGB 颜色值</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="seedColor">种子色（ARGB 格式），通常来自专辑封面的主色调</param>
    /// <returns>包含完整配色方案的 MaterialYouPalette 实例</returns>
    public static MaterialYouPalette FromSeedColor(int seedColor)
    {
        // 将种子色从 RGB 转换为 HSV，提取色相、饱和度、明度三个分量
        var hsv = new float[3];
        Color.RGBToHSV(
            Color.GetRedComponent(seedColor),
            Color.GetGreenComponent(seedColor),
            Color.GetBlueComponent(seedColor), hsv);

        var hue = hsv[0];   // 色相：0-360，决定颜色基调（红/橙/黄/绿/青/蓝/紫）
        var sat = hsv[1];   // 饱和度：0-1，决定颜色的鲜艳程度
        var val = hsv[2];   // 明度：0-1，决定颜色的明暗程度

        // 基于种子色的 HSV 分量，通过调整饱和度和明度生成各配色角色
        // 饱和度调整策略：乘以系数 + 设置上限，确保大面积使用的颜色不会过于鲜艳
        // 明度调整策略：通过 Lerp 插值，在种子色明度和目标明度之间取值
        var palette = new MaterialYouPalette
        {
            Background   = HsvToColor(hue, Math.Min(sat * 0.40f, 0.22f), Lerp(val, 0.88f, 0.40f)),
            Surface      = HsvToColor(hue, Math.Min(sat * 0.35f, 0.18f), Lerp(val, 0.84f, 0.35f)),
            OnSurface    = HsvToColor(hue, Math.Min(sat * 0.15f, 0.05f), Lerp(val, 0.12f, 0.33f)),
            OnSurfaceVariant = HsvToColor(hue, Math.Min(sat * 0.25f, 0.12f), Lerp(val, 0.42f, 0.28f)),
            Primary      = HsvToColor(hue, Math.Max(sat, 0.25f), Lerp(val, 0.50f, 0.40f)),
            OnSurfaceForDarkBg = HsvToColor(hue, Math.Min(sat * 0.10f, 0.04f), 0.92f),
            Outline      = HsvToColor(hue, Math.Min(sat * 0.10f, 0.06f), Lerp(val, 0.80f, 0.22f)),
            GlowAccent   = HsvToColor(hue, Math.Min(sat * 0.65f, 0.50f), Lerp(val, 0.60f, 0.45f))
        };

        return palette;
    }

    /// <summary>
    /// 线性插值：将种子色明度向目标明度过渡
    /// <para>
    /// 计算公式：result = seed + (target - seed) * (1 - ratio)
    /// <list type="bullet">
    ///   <item>ratio = 1 时，result = seed（完全保留种子色明度）</item>
    ///   <item>ratio = 0 时，result = target（完全使用目标明度）</item>
    ///   <item>ratio 介于 0-1 之间时，在两者之间插值</item>
    /// </list>
    /// </para>
    /// <para>此设计使得种子色明度较高的封面和明度较低的封面都能生成合理的配色</para>
    /// </summary>
    /// <param name="seed">种子色的明度值（V 分量）</param>
    /// <param name="target">目标明度值</param>
    /// <param name="ratio">保留种子色特征的比例，越大越接近种子色</param>
    /// <returns>插值后的明度值</returns>
    private static float Lerp(float seed, float target, float ratio)
        => seed + (target - seed) * (1f - ratio);

    /// <summary>
    /// 将 HSV 色彩值转换为 ARGB 颜色整数
    /// <para>包含安全钳位，确保饱和度和明度在 [0, 1] 范围内</para>
    /// </summary>
    /// <param name="h">色相（0-360）</param>
    /// <param name="s">饱和度（0-1）</param>
    /// <param name="v">明度（0-1）</param>
    /// <returns>ARGB 格式的颜色值</returns>
    private static int HsvToColor(float h, float s, float v)
    {
        s = Math.Clamp(s, 0f, 1f);
        v = Math.Clamp(v, 0f, 1f);
        return Color.HSVToColor(new[] { h, s, v });
    }
}
