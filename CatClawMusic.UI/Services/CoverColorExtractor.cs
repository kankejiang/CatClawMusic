using Android.Graphics;

namespace CatClawMusic.UI.Services;

/// <summary>
/// 从专辑封面位图中提取 1-3 种主色调，用于生成动态渐变背景
/// 采用色彩量化 + 频率统计 + 饱和度/亮度加权筛选算法
/// </summary>
public static class CoverColorExtractor
{
    /// <summary>量化精度：将每个通道划分为 32 个色阶，共 32768 个可能的量化颜色</summary>
    private const int QuantizeLevels = 32;

    /// <summary>排除接近纯黑/纯白的颜色：明度在 [VMin, VMax] 范围之外的颜色将被过滤</summary>
    private const float VMin = 0.12f;
    private const float VMax = 0.92f;

    /// <summary>最大采样尺寸：缩小位图以减少处理时间</summary>
    private const int MaxSampleSize = 120;

    /// <summary>
    /// 从位图提取主色调，返回 1-3 个 Android.Graphics.Color 值
    /// </summary>
    public static List<int> Extract(Bitmap bitmap)
    {
        var colors = new List<int>();
        if (bitmap == null || bitmap.IsRecycled) return colors;

        try
        {
            // 缩放到固定尺寸采样
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

            // 色彩频率统计
            var colorFreq = new Dictionary<int, int>();
            for (int y = 0; y < sampled.Height; y++)
            {
                for (int x = 0; x < sampled.Width; x++)
                {
                    var pixel = new Color(sampled.GetPixel(x, y));
                    // 过滤透明像素
                    if (pixel.A < 128) continue;

                    // 过滤过暗/过亮的颜色（避免黑底白字图片的极端值）
                    float[] hsv = { 0, 0, 0 };
                    Android.Graphics.Color.RGBToHSV(pixel.R, pixel.G, pixel.B, hsv);
                    if (hsv[2] < VMin || hsv[2] > VMax) continue;

                    // 色彩量化
                    var r = pixel.R / QuantizeLevels;
                    var g = pixel.G / QuantizeLevels;
                    var b = pixel.B / QuantizeLevels;
                    var key = (r << 10) | (g << 5) | b;

                    if (colorFreq.ContainsKey(key))
                        colorFreq[key]++;
                    else
                        colorFreq[key] = 1;
                }
            }

            if (sampled != bitmap && sampled != null)
                sampled.Recycle();

            if (colorFreq.Count == 0) return colors;

            // 按加权分数排序：频率 × 饱和度系数，优先选择鲜艳且频繁的颜色
            var scored = colorFreq
                .Select(kv =>
                {
                    var r = ((kv.Key >> 10) & 0x1F) * QuantizeLevels + QuantizeLevels / 2;
                    var g = ((kv.Key >> 5) & 0x1F) * QuantizeLevels + QuantizeLevels / 2;
                    var b = (kv.Key & 0x1F) * QuantizeLevels + QuantizeLevels / 2;
                    float[] hsv = { 0, 0, 0 };
                    Android.Graphics.Color.RGBToHSV(r, g, b, hsv);
                    // 分数 = 频率 × (0.5 + 饱和度) 以提升鲜艳颜色的权重
                    var score = kv.Value * (0.5f + hsv[1]);
                    return (Color: Android.Graphics.Color.Rgb(r, g, b), Score: score);
                })
                .OrderByDescending(x => x.Score)
                .ToList();

            // 选取最多 3 个颜色，跳过色相过于接近的重复色
            foreach (var entry in scored)
            {
                var c = entry.Color;
                float[] hsv1 = { 0, 0, 0 };
                var cr = Android.Graphics.Color.GetRedComponent(c);
                var cg = Android.Graphics.Color.GetGreenComponent(c);
                var cb = Android.Graphics.Color.GetBlueComponent(c);
                Android.Graphics.Color.RGBToHSV(cr, cg, cb, hsv1);

                // 检查与已选颜色的色相距离，避免重复
                bool isDuplicate = false;
                foreach (var existing in colors)
                {
                    float[] hsv2 = { 0, 0, 0 };
                    var er = Android.Graphics.Color.GetRedComponent(existing);
                    var eg = Android.Graphics.Color.GetGreenComponent(existing);
                    var eb = Android.Graphics.Color.GetBlueComponent(existing);
                    Android.Graphics.Color.RGBToHSV(er, eg, eb, hsv2);
                    var hueDist = Math.Abs(hsv1[0] - hsv2[0]);
                    if (hueDist < 30 || hueDist > 330)
                    {
                        isDuplicate = true;
                        break;
                    }
                }
                if (isDuplicate) continue;

                colors.Add(c);
                if (colors.Count >= 3) break;
            }

            // 兜底：量化过滤太严格导致没有结果时，使用采样图的简单平均色
            if (colors.Count == 0)
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
                    colors.Add(Android.Graphics.Color.Rgb(
                        (int)(rSum / count), (int)(gSum / count), (int)(bSum / count)));
            }
        }
        catch { }

        return colors;
    }

    /// <summary>
    /// 从图片文件路径提取主色调（封装了 Bitmap 的加载与回收）
    /// </summary>
    public static List<int> ExtractFromFile(string filePath)
    {
        var colors = new List<int>();
        try
        {
            using var bitmap = BitmapFactory.DecodeFile(filePath);
            if (bitmap != null)
            {
                colors = Extract(bitmap);
            }
        }
        catch { }
        return colors;
    }
}
