namespace CatClawMusic.Core.ColorQuantize;

/// <summary>
/// 封面主色提取辅助：把原始像素（BGRA/ARGB）喂给 ColorThief 的 median-cut 量化，
/// 得到封面真正的主导色，供雾面背景等逐色调参考（替代盲目的饱和增强）。
/// Windows 解码产物为 BGRA，Android 位图像素为 ARGB，本类同时兼容两者。
/// </summary>
public static class DominantColor
{
    /// <summary>从 BGRA 像素流提取主导色（b,g,r,a 每像素 4 字节）。无有效像素时返回 null。</summary>
    public static ColorTone? FromBgra(byte[] bgra, int w, int h)
    {
        var tones = new List<ColorTone>(Math.Max(16, w * h / 8));
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            byte b = bgra[i];
            byte g = bgra[i + 1];
            byte r = bgra[i + 2];
            byte a = bgra[i + 3];
            if (a >= 125)
                tones.Add(new ColorTone(r, g, b));
        }
        return Quantize(tones);
    }

    /// <summary>从 ARGB int 像素流提取主导色。无有效像素时返回 null。</summary>
    public static ColorTone? FromArgb(int[] argb)
    {
        var tones = new List<ColorTone>(Math.Max(16, argb.Length / 8));
        foreach (var p in argb)
        {
            int a = (p >> 24) & 0xFF;
            if (a < 125) continue;
            tones.Add(new ColorTone((byte)((p >> 16) & 0xFF), (byte)((p >> 8) & 0xFF), (byte)(p & 0xFF)));
        }
        return Quantize(tones);
    }

    private static ColorTone? Quantize(List<ColorTone> tones)
    {
        if (tones.Count == 0) return null;
        var pixels = tones.Select(t => new PixelRGBA(t.R, t.G, t.B, 255));
        try
        {
            var c = ColorThief.GetColor(pixels);
            return new ColorTone(c.R, c.G, c.B);
        }
        catch
        {
            return null;
        }
    }

    // 兼容旧构造，避免历史调用零散传参
    /// <summary>RGB 三字节主导色。</summary>
    public readonly record struct ColorTone(byte R, byte G, byte B);
}