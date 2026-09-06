namespace CatClawMusic.Core.ColorQuantize;

/// <summary>
/// 封面强调色选取（移植自 Halcyon 的 PlayerPalette.representativeAccent + toPlayerAccent）。
/// 与“取像素最多的中位切割”不同，这里用「样本数 × 饱和度加成 × 亮度平衡」打分，
/// 主动挑出封面鲜艳且居中亮度的主题色，避开纯黑/纯白/大块边框；纯亮中性封面回退整图平均色。
/// 纯数学，安卓(ARGB int[])与 Windows(BGRA 转 ARGB)共用。
/// </summary>
public static class AccentColorPicker
{
    /// <summary>取样网格步长：长/宽按较短边切 ~36 格，稀疏取样即可。</summary>
    private const int SampleGrid = 36;
    /// <summary>低饱和回退钢蓝（Halcyon 的 #4D72B8）。</summary>
    private const int FallbackSteelBlue = 0x4D72B8;

    /// <summary>ARGB 像素流 → 强调色（0x00RRGGBB）。无法取色返回 0。</summary>
    public static int Calculate(int[] argb, int width, int height)
    {
        if (argb == null || width <= 0 || height <= 0) return 0;
        var cands = RepresentativeCandidates(argb, width, height);
        return cands.Count > 0 ? ToPlayerAccent(cands[0].R, cands[0].G, cands[0].B) : 0;
    }

    /// <summary>
    /// 强调色对（0xRRGGBB）：主色同 <see cref="Calculate"/>；
    /// 次色 = 得分次高且色相距主色 ≥30° 的色桶（供动态背景的双色光晕层次），
    /// 无足够分离度的次候选时回退主色（调用方可按需旋转色相合成次色）。无法取色返回 (0, 0)。
    /// </summary>
    public static (int Primary, int Secondary) CalculatePair(int[] argb, int width, int height)
    {
        if (argb == null || width <= 0 || height <= 0) return (0, 0);
        var cands = RepresentativeCandidates(argb, width, height);
        if (cands.Count == 0) return (0, 0);
        var primary = ToPlayerAccent(cands[0].R, cands[0].G, cands[0].B);
        var secondary = cands.Count > 1 ? ToPlayerAccent(cands[1].R, cands[1].G, cands[1].B) : primary;
        return (primary, secondary);
    }

    /// <summary>候选色列表（按得分降序、色相互距 ≥30° 去重，最多取前 2 个）。
    /// 无彩/亮中性回退路径返回单元素列表（整图平均色）。</summary>
    private static List<(byte R, byte G, byte B)> RepresentativeCandidates(int[] argb, int width, int height)
    {
        int step = Math.Max(1, Math.Min(width, height) / SampleGrid);

        // top-4bit 量化桶（12bit → 4096），记录 数量 / R和 / G和 / B和
        var bucketCount = new int[4096];
        var bucketSumR = new long[4096];
        var bucketSumG = new long[4096];
        var bucketSumB = new long[4096];

        long fallbackCount = 0, fallbackR = 0, fallbackG = 0, fallbackB = 0;
        int sampled = 0, brightNeutral = 0, eligible = 0, lowSat = 0;

        for (int y = 0; y < height; y += step)
        {
            int row = y * width;
            for (int x = 0; x < width; x += step)
            {
                int p = argb[row + x];
                int a = (p >> 24) & 0xFF;
                if (a <= 24) continue;

                int r = (p >> 16) & 0xFF;
                int g = (p >> 8) & 0xFF;
                int b = p & 0xFF;

                RgbToHsv(r, g, b, out float h, out float s, out float v);
                sampled++;
                fallbackCount++;
                fallbackR += r; fallbackG += g; fallbackB += b;
                if (s < 0.22f) lowSat++;

                if (v > 0.78f && s < 0.18f) brightNeutral++;
                if (v > 0.08f && !(v > 0.94f && s < 0.20f))
                {
                    eligible++;
                    int key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                    bucketCount[key]++;
                    bucketSumR[key] += r; bucketSumG[key] += g; bucketSumB[key] += b;
                }
            }
        }

        if (fallbackCount == 0) return new List<(byte R, byte G, byte B)>();

        // 无彩封面（黑白灰，不限亮度）：JPEG 色度噪声会让个别"微彩灰"桶饱和度略高，
        // 若走打分会被饱和度加成捧成冠军、再被 ToPlayerAccent 强提为鲜艳紫粉。
        // 整图低饱和占比 >80% 时直接回退平均色（走中性回退，不参与打分）。
        if (sampled > 0 && lowSat / (float)sampled > 0.80f)
        {
            long _n = Math.Max(1L, fallbackCount);
            return new List<(byte R, byte G, byte B)> { ((byte)(fallbackR / _n), (byte)(fallbackG / _n), (byte)(fallbackB / _n)) };
        }

        // 封面绝大多数是亮中性色、彩色像素稀少 → 回退整图平均色
        if (sampled > 0
            && brightNeutral / (float)sampled > 0.56f
            && eligible / (float)sampled < 0.24f)
        {
            long _count = Math.Max(1L, fallbackCount);
            return new List<(byte R, byte G, byte B)> { ((byte)(fallbackR / _count), (byte)(fallbackG / _count), (byte)(fallbackB / _count)) };
        }

        // 打分：样本多 + 高饱和 + 亮度接近中间值。
        // 低饱和桶(<0.15)一律跳过：它们不可能承载主题色，只会让噪声灰夺冠。
        var scored = new List<(double Score, long Count, int R, int G, int B)>();
        for (int key = 0; key < 4096; key++)
        {
            long count = bucketCount[key];
            if (count == 0) continue;
            long c = Math.Max(1L, count);
            int r = (int)(bucketSumR[key] / c);
            int g = (int)(bucketSumG[key] / c);
            int b = (int)(bucketSumB[key] / c);

            RgbToHsv(r, g, b, out _, out float sat, out _);
            if (sat < 0.15f) continue;
            float lum = (0.2126f * r + 0.7152f * g + 0.0722f * b) / 255f;
            float balance = 1f - Math.Abs(lum - 0.50f).Clamp01() * 1.25f;
            double score = count * (0.55 + sat * 1.65) * (0.75 + balance * 0.55);
            scored.Add((score, count, r, g, b));
        }

        if (scored.Count == 0) return new List<(byte R, byte G, byte B)>();
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));

        // 贪心取前 2 个色相分离（≥30°）的候选：主色 = 最高分桶，次色 = 色相离主色最远的次高分行
        //（scored 中 r/g/b 已是桶平均值，直接使用，勿再除以 count）
        var picked = new List<(byte R, byte G, byte B)>();
        foreach (var (_, _, r, g, b) in scored)
        {
            RgbToHsv(r, g, b, out float h, out _, out _);
            bool tooClose = false;
            foreach (var (pr, pg, pb) in picked)
            {
                RgbToHsv(pr, pg, pb, out float ph2, out _, out _);
                float d = Math.Abs(h - ph2);
                if (Math.Min(d, 360f - d) < 30f) { tooClose = true; break; }
            }
            if (tooClose) continue;

            picked.Add(((byte)r, (byte)g, (byte)b));
            if (picked.Count >= 2) break;
        }
        return picked;
    }

    /// <summary>强调色规范：低饱和回退钢蓝；否则提升饱和度下限、钳制亮度(0.46~0.88)。</summary>
    private static int ToPlayerAccent(int r, int g, int b)
    {
        RgbToHsv(r, g, b, out float h, out float s, out float v);
        if (s < 0.12f) return FallbackSteelBlue;
        s = Math.Max(s, 0.34f);
        v = Math.Clamp(v, 0.46f, 0.88f);
        HsvToRgb(h, s, v, out int rr, out int gg, out int bb);
        return (rr << 16) | (gg << 8) | bb;
    }

    private static void RgbToHsv(int r, int g, int b, out float h, out float s, out float v)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        float dv = max - min;
        v = max;
        s = max != 0f ? dv / max : 0f;
        h = 0f;
        if (s != 0f)
        {
            if (rf == max) h = (gf - bf) / dv;
            else if (gf == max) h = 2f + (bf - rf) / dv;
            else h = 4f + (rf - gf) / dv;
            h *= 60f;
            if (h < 0f) h += 360f;
        }
    }

    private static void HsvToRgb(float h, float s, float v, out int r, out int g, out int b)
    {
        if (s == 0f)
        {
            int iv = (int)Math.Round(v * 255f);
            r = g = b = iv;
            return;
        }
        h /= 60f;
        int i = (int)Math.Floor(h);
        float f = h - i;
        float p = v * (1f - s);
        float q = v * (1f - s * f);
        float t = v * (1f - s * (1f - f));
        float rr = 0f, gg = 0f, bb = 0f;
        switch (((i % 6) + 6) % 6)
        {
            case 0: rr = v; gg = t; bb = p; break;
            case 1: rr = q; gg = v; bb = p; break;
            case 2: rr = p; gg = v; bb = t; break;
            case 3: rr = p; gg = q; bb = v; break;
            case 4: rr = t; gg = p; bb = v; break;
            default: rr = v; gg = p; bb = q; break;
        }
        r = (int)Math.Round(rr * 255f);
        g = (int)Math.Round(gg * 255f);
        b = (int)Math.Round(bb * 255f);
    }
}

internal static class FloatClampExt
{
    public static float Clamp01(this float value) => Math.Clamp(value, 0f, 1f);
}