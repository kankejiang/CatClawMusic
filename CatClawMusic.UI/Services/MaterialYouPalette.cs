using Android.Graphics;

namespace CatClawMusic.UI.Services;

/// <summary>
/// Material You 色调方案：从种子色生成完整的 UI 配色（背景、表面、文字、主题色等）
/// 基于 HSL 色彩空间的感知均匀色调映射，模拟 Android 12+ 莫奈主题算法
/// </summary>
public class MaterialYouPalette
{
    public int Background { get; set; }
    public int Surface { get; set; }
    public int OnSurface { get; set; }
    public int OnSurfaceVariant { get; set; }
    public int Primary { get; set; }
    public int OnSurfaceForDarkBg { get; set; }
    public int Outline { get; set; }
    public int GlowAccent { get; set; }

    /// <summary>
    /// 从种子色（封面主色调）生成完整 Material You 配色方案
    /// </summary>
    public static MaterialYouPalette FromSeedColor(int seedColor)
    {
        var hsv = new float[3];
        Color.RGBToHSV(
            Color.GetRedComponent(seedColor),
            Color.GetGreenComponent(seedColor),
            Color.GetBlueComponent(seedColor), hsv);

        var hue = hsv[0];
        var sat = hsv[1];
        var val = hsv[2];

        var palette = new MaterialYouPalette
        {
            Background   = HsvToColor(hue, Math.Min(sat * 0.18f, 0.08f), Lerp(val, 0.95f, 0.35f)),
            Surface      = HsvToColor(hue, Math.Min(sat * 0.22f, 0.10f), Lerp(val, 0.91f, 0.30f)),
            OnSurface    = HsvToColor(hue, Math.Min(sat * 0.15f, 0.05f), Lerp(val, 0.12f, 0.33f)),
            OnSurfaceVariant = HsvToColor(hue, Math.Min(sat * 0.25f, 0.12f), Lerp(val, 0.42f, 0.28f)),
            Primary      = HsvToColor(hue, Math.Max(sat, 0.25f), Lerp(val, 0.50f, 0.40f)),
            OnSurfaceForDarkBg = HsvToColor(hue, Math.Min(sat * 0.10f, 0.04f), 0.92f),
            Outline      = HsvToColor(hue, Math.Min(sat * 0.10f, 0.06f), Lerp(val, 0.80f, 0.22f)),
            GlowAccent   = HsvToColor(hue, Math.Min(sat * 0.55f, 0.40f), Lerp(val, 0.60f, 0.45f))
        };

        return palette;
    }

    /// <summary>
    /// 将主色/种子色线性插值到目标亮度，ratio 越大越接近种子
    /// </summary>
    private static float Lerp(float seed, float target, float ratio)
        => seed + (target - seed) * (1f - ratio);

    private static int HsvToColor(float h, float s, float v)
    {
        s = Math.Clamp(s, 0f, 1f);
        v = Math.Clamp(v, 0f, 1f);
        return Color.HSVToColor(new[] { h, s, v });
    }
}
