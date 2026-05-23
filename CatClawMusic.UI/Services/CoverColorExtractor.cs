using Android.Graphics;

namespace CatClawMusic.UI.Services;

/// <summary>表示提取出的一个主色调及其在封面中的水平位置</summary>
public class ColorEntry
{
    /// <summary>颜色值（ARGB）</summary>
    public int Color { get; set; }
    /// <summary>颜色在封面中的水平中心位置（0~1 归一化）</summary>
    public float CenterX { get; set; }
}

/// <summary>
/// 从专辑封面位图中提取 1-6 种主色调及其水平位置，用于生成动态渐变背景
/// 采用色彩量化 + 频率统计 + 饱和度/亮度加权筛选算法
/// </summary>
public static class CoverColorExtractor
{
    private const int QuantizeLevels = 32;
    private const float VMin = 0.12f;
    private const float VMax = 0.92f;
    private const int MaxSampleSize = 120;

    /// <summary>
    /// 从位图提取主色调及水平位置，返回 1-6 个 ColorEntry
    /// </summary>
    public static List<ColorEntry> Extract(Bitmap bitmap)
    {
        var result = new List<ColorEntry>();
        if (bitmap == null || bitmap.IsRecycled) return result;

        try
        {
            Bitmap? sampled = null;
            if (bitmap.Width > MaxSampleSize || bitmap.Height > MaxSampleSize)
            {
                var scale = (float)MaxSampleSize / Math.Max(bitmap.Width, bitmap.Height);
                var w = (int)(bitmap.Width * scale);
                var h = (int)(bitmap.Height * scale);
                sampled = Bitmap.CreateScaledBitmap(bitmap, Math.Max(w, 1), Math.Max(h, 1), false);
            }
            else
            {
                sampled = bitmap;
            }

            var colorFreq = new Dictionary<int, int>();
            var xSumPerKey = new Dictionary<int, long>();

            for (int y = 0; y < sampled.Height; y++)
            {
                for (int x = 0; x < sampled.Width; x++)
                {
                    var pixel = new Color(sampled.GetPixel(x, y));
                    if (pixel.A < 128) continue;

                    float[] hsv = { 0, 0, 0 };
                    Android.Graphics.Color.RGBToHSV(pixel.R, pixel.G, pixel.B, hsv);
                    if (hsv[2] < VMin || hsv[2] > VMax) continue;

                    var r = pixel.R / QuantizeLevels;
                    var g = pixel.G / QuantizeLevels;
                    var b = pixel.B / QuantizeLevels;
                    var key = (r << 10) | (g << 5) | b;

                    if (colorFreq.ContainsKey(key))
                    {
                        colorFreq[key]++;
                        xSumPerKey[key] += x;
                    }
                    else
                    {
                        colorFreq[key] = 1;
                        xSumPerKey[key] = x;
                    }
                }
            }

            if (sampled != bitmap && sampled != null)
                sampled.Recycle();

            if (colorFreq.Count == 0) return result;

            var scored = colorFreq
                .Select(kv =>
                {
                    var r = ((kv.Key >> 10) & 0x1F) * QuantizeLevels + QuantizeLevels / 2;
                    var g = ((kv.Key >> 5) & 0x1F) * QuantizeLevels + QuantizeLevels / 2;
                    var b = (kv.Key & 0x1F) * QuantizeLevels + QuantizeLevels / 2;
                    float[] hsv = { 0, 0, 0 };
                    Android.Graphics.Color.RGBToHSV(r, g, b, hsv);
                    var score = kv.Value * (0.5f + hsv[1]);
                    var avgX = (float)xSumPerKey[kv.Key] / kv.Value;
                    return (Color: Android.Graphics.Color.Rgb(r, g, b), Score: score, AvgX: avgX);
                })
                .OrderByDescending(x => x.Score)
                .ToList();

            var colors = new List<int>();
            var xPositions = new List<float>();
            var minHueDist = 30f;

            foreach (var entry in scored)
            {
                var c = entry.Color;
                float[] hsv1 = { 0, 0, 0 };
                var cr = Android.Graphics.Color.GetRedComponent(c);
                var cg = Android.Graphics.Color.GetGreenComponent(c);
                var cb = Android.Graphics.Color.GetBlueComponent(c);
                Android.Graphics.Color.RGBToHSV(cr, cg, cb, hsv1);

                bool isDuplicate = false;
                foreach (var existing in colors)
                {
                    float[] hsv2 = { 0, 0, 0 };
                    var er = Android.Graphics.Color.GetRedComponent(existing);
                    var eg = Android.Graphics.Color.GetGreenComponent(existing);
                    var eb = Android.Graphics.Color.GetBlueComponent(existing);
                    Android.Graphics.Color.RGBToHSV(er, eg, eb, hsv2);
                    var hueDist = Math.Abs(hsv1[0] - hsv2[0]);
                    if (hueDist < minHueDist || hueDist > 360f - minHueDist)
                    {
                        isDuplicate = true;
                        break;
                    }
                }
                if (isDuplicate) continue;

                colors.Add(c);
                xPositions.Add(entry.AvgX);
                result.Add(new ColorEntry { Color = c, CenterX = entry.AvgX });

                if (result.Count >= 6) break;
                if (result.Count >= 3) minHueDist = 18f;
            }

            // 归一化到 0~1
            if (result.Count > 0)
            {
                var maxX = xPositions.Max();
                var minX = xPositions.Min();
                var xRange = maxX - minX;
                if (xRange > 0)
                {
                    foreach (var entry in result)
                        entry.CenterX = (entry.CenterX - minX) / xRange;
                }
                else
                {
                    for (int i = 0; i < result.Count; i++)
                        result[i].CenterX = (float)i / (result.Count - 1 > 0 ? result.Count - 1 : 1);
                }
            }

            if (result.Count == 0)
            {
                long rSum = 0, gSum = 0, bSum = 0;
                int count = 0;
                for (int y = 0; y < bitmap.Height; y += 10)
                {
                    for (int x = 0; x < bitmap.Width; x += 10)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        var p = new Color(pixel);
                        if (p.A < 128) continue;
                        float[] hsv = { 0, 0, 0 };
                        Android.Graphics.Color.RGBToHSV(p.R, p.G, p.B, hsv);
                        if (hsv[2] < 0.1f || hsv[2] > 0.95f) continue;
                        rSum += p.R; gSum += p.G; bSum += p.B;
                        count++;
                    }
                }
                if (count > 0)
                    result.Add(new ColorEntry
                    {
                        Color = Android.Graphics.Color.Rgb((int)(rSum / count), (int)(gSum / count), (int)(bSum / count)),
                        CenterX = 0.5f
                    });
            }
        }
        catch { }

        return result;
    }

    /// <summary>
    /// 从图片文件路径提取主色调（封装了 Bitmap 的加载与回收）
    /// </summary>
    public static List<ColorEntry> ExtractFromFile(string filePath)
    {
        var entries = new List<ColorEntry>();
        try
        {
            using var bitmap = BitmapFactory.DecodeFile(filePath);
            if (bitmap != null)
            {
                entries = Extract(bitmap);
            }
        }
        catch { }
        return entries;
    }
}
