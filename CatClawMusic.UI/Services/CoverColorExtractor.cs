using Android.Graphics;

namespace CatClawMusic.UI.Services;

/// <summary>
/// 表示从封面中提取出的一种主色调及其在封面图片中的水平位置
/// <para>
/// 该结构用于动态渐变背景的生成：
/// <list type="bullet">
///   <item>Color：提取出的主色调，用于渐变色停（gradient stop）</item>
///   <item>CenterX：该色调在封面中的水平中心位置（0~1 归一化），用于确定渐变色停的水平分布</item>
/// </list>
/// 例如，若封面左侧为蓝色、右侧为橙色，则蓝色 ColorEntry 的 CenterX 接近 0，橙色接近 1，
/// 渐变背景将按此比例分布两种颜色，使背景与封面构图呼应。
/// </para>
/// </summary>
public class ColorEntry
{
    public int Color { get; set; }

    public float CenterX { get; set; }

    public float Weight { get; set; } = 1f;
}

/// <summary>
/// 从专辑封面位图中提取 1-6 种主色调及其水平位置，用于生成动态渐变背景
/// <para>
/// 算法概述（色彩量化 + 频率统计 + 饱和度加权筛选）：
/// <list type="number">
///   <item>降采样：将封面缩放至最大 120×120 像素，减少计算量</item>
///   <item>色彩量化：将每个像素的 RGB 各通道除以 32（QuantizeLevels），映射到 5 位整数（0-31），
///         三个通道组合为 15 位 key，大幅减少颜色种类</item>
///   <item>频率统计：统计每个量化 key 出现的像素数，同时累加 x 坐标用于计算水平中心</item>
///   <item>亮度过滤：跳过过暗（V &lt; 0.12）和过亮（V &gt; 0.92）的像素，避免黑/白等无意义色调</item>
///   <item>评分排序：每个量化颜色的得分 = 像素频率 × (0.5 + 饱和度)，饱和度越高且出现越多的颜色排名越靠前</item>
///   <item>色相去重：按得分从高到低选取，要求相邻选中颜色的色相差 ≥ 30°（前3个）或 ≥ 18°（第4-6个），
///         确保提取的颜色在视觉上有足够区分度</item>
///   <item>位置归一化：将各颜色的水平中心位置归一化到 0~1 范围</item>
///   <item>兜底策略：若上述流程未提取到任何颜色，对原图进行稀疏采样（每10像素取1个），
///         计算所有有效像素的平均颜色作为兜底</item>
/// </list>
/// </para>
/// </summary>
public static class CoverColorExtractor
{
    private static readonly Dictionary<string, List<ColorEntry>> _fileCache = new();
    private static readonly Dictionary<string, long> _fileCacheTimestamps = new();

    public static void InvalidateCache(string? filePath)
    {
        if (filePath != null)
        {
            _fileCache.Remove(filePath);
            _fileCacheTimestamps.Remove(filePath);
        }
    }

    public static void ClearCache()
    {
        _fileCache.Clear();
        _fileCacheTimestamps.Clear();
    }
    /// <summary>
    /// 色彩量化级别：将每个 RGB 通道从 256 级（8位）降为 32 级（5位）
    /// <para>量化公式：channel_key = channel_value / 32，范围 0-31</para>
    /// <para>三个通道组合为 15 位 key：(r_key << 10) | (g_key << 5) | b_key</para>
    /// </para>
    /// </summary>
    private const int QuantizeLevels = 32;

    /// <summary>
    /// 明度下限：低于此值的像素被视为"过暗"，不参与主色调提取
    /// <para>避免纯黑、极暗区域主导提取结果</para>
    /// </summary>
    private const float VMin = 0.05f;

    /// <summary>
    /// 明度上限：高于此值的像素被视为"过亮"，不参与主色调提取
    /// <para>避免纯白、极亮区域主导提取结果</para>
    /// </summary>
    private const float VMax = 0.97f;

    /// <summary>
    /// 最大采样尺寸：封面图片降采样后的最大边长（像素）
    /// <para>在保证提取精度的前提下，将计算量控制在合理范围</para>
    /// </summary>
    private const int MaxSampleSize = 120;

    /// <summary>
    /// 从位图提取主色调及水平位置，返回 1-6 个 ColorEntry
    /// <para>
    /// 完整流程：
    /// <list type="number">
    ///   <item>降采样：大图缩放至 MaxSampleSize 以内</item>
    ///   <item>逐像素遍历：跳过半透明像素（A &lt; 128）和过暗/过亮像素</item>
    ///   <item>色彩量化 + 频率统计：构建 colorFreq 和 xSumPerKey 字典</item>
    ///   <item>评分排序：得分 = 频率 × (0.5 + 饱和度)</item>
    ///   <item>色相去重选取：前3个色差≥30°，之后色差≥18°，最多6个</item>
    ///   <item>水平位置归一化</item>
    ///   <item>兜底：若无可选颜色，取全图平均色</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="bitmap">专辑封面位图</param>
    /// <returns>主色调列表（1-6 个 ColorEntry），若输入为空则返回空列表</returns>
    public static List<ColorEntry> Extract(Bitmap bitmap)
    {
        var result = new List<ColorEntry>();
        if (bitmap == null || bitmap.IsRecycled) return result;

        try
        {
            /* 优先使用 C++ 原生库取色（性能更优，避免 GetPixel JNI 开销） */
            var nativeResult = NativeInterop.ExtractColorsFromBitmap(bitmap);
            if (nativeResult != null && nativeResult.Count > 0)
                return nativeResult;

            bool isGrayscaleCover = DetectGrayscaleCover(bitmap);
            if (isGrayscaleCover)
                return ExtractGrayscaleColors(bitmap);

            /* C# 回退实现：当原生库不可用时使用 */
            // 第一步：降采样，将大图缩放至 MaxSampleSize 以内以减少计算量
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

            // 第二步：逐像素遍历，进行色彩量化和频率统计
            var colorFreq = new Dictionary<int, int>();     // 量化 key → 像素出现次数
            var xSumPerKey = new Dictionary<int, long>();    // 量化 key → 该颜色所有像素的 x 坐标之和

            for (int y = 0; y < sampled.Height; y++)
            {
                for (int x = 0; x < sampled.Width; x++)
                {
                    var pixel = new Color(sampled.GetPixel(x, y));

                    // 跳过半透明像素（alpha < 128），避免透明区域干扰取色
                    if (pixel.A < 128) continue;

                    // 转换为 HSV，进行亮度过滤
                    float[] hsv = { 0, 0, 0 };
                    Android.Graphics.Color.RGBToHSV(pixel.R, pixel.G, pixel.B, hsv);

                    // 跳过过暗和过亮的像素，避免黑/白色主导结果
                    if (hsv[2] < VMin || hsv[2] > VMax) continue;

                    // 色彩量化：将 RGB 各通道从 256 级降为 32 级（5位），组合为 15 位 key
                    var r = pixel.R / QuantizeLevels;
                    var g = pixel.G / QuantizeLevels;
                    var b = pixel.B / QuantizeLevels;
                    var key = (r << 10) | (g << 5) | b;

                    // 累加频率和 x 坐标
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

            // 释放降采样位图（仅当它是新建的缩放副本时）
            if (sampled != bitmap && sampled != null)
                sampled.Recycle();

            // 若无有效像素，返回空列表
            if (colorFreq.Count == 0) return result;

            // 第三步：评分排序
            // 评分公式：score = 频率 × (0.5 + 饱和度)
            // 饱和度越高的颜色得分越高（更鲜艳），出现频率越高的颜色得分也越高（更具代表性）
            // 0.5 的偏移确保低饱和度颜色仍有机会被选中（只要频率足够高）
            var scored = colorFreq
                .Select(kv =>
                {
                    var r = ((kv.Key >> 10) & 0x1F) * QuantizeLevels + QuantizeLevels / 2;
                    var g = ((kv.Key >> 5) & 0x1F) * QuantizeLevels + QuantizeLevels / 2;
                    var b = (kv.Key & 0x1F) * QuantizeLevels + QuantizeLevels / 2;
                    float[] hsv = { 0, 0, 0 };
                    Android.Graphics.Color.RGBToHSV(r, g, b, hsv);
                    var score = kv.Value * (0.5f + hsv[1] * 0.5f);
                    var avgX = (float)xSumPerKey[kv.Key] / kv.Value;
                    return (Color: Android.Graphics.Color.Rgb(r, g, b), Score: score, AvgX: avgX);
                })
                .OrderByDescending(x => x.Score)
                .ToList();

            // 第四步：色相去重选取
            // 从得分最高的颜色开始，依次检查与已选颜色的色相差
            // 前3个颜色要求色相差 ≥ 30°（确保主要色调差异明显）
            // 第4-6个颜色要求色相差 ≥ 18°（允许更细微的色调变化）
            var colors = new List<int>();
            var xPositions = new List<float>();
            var minHueDist = 20f;

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

                    bool bothLowSat = hsv1[1] < 0.15f && hsv2[1] < 0.15f;
                    if (bothLowSat)
                    {
                        if (Math.Abs(hsv1[2] - hsv2[2]) < 0.15f) { isDuplicate = true; break; }
                    }
                    else
                    {
                        var hueDist = Math.Abs(hsv1[0] - hsv2[0]);
                        if (hueDist < minHueDist || hueDist > 360f - minHueDist) { isDuplicate = true; break; }
                    }
                }
                if (isDuplicate) continue;

                // 通过去重检查，加入结果列表
                colors.Add(c);
                xPositions.Add(entry.AvgX);
                result.Add(new ColorEntry { Color = c, CenterX = entry.AvgX, Weight = entry.Score });

                // 最多提取 6 种主色调
                if (result.Count >= 6) break;
                // 前3个颜色选完后，放宽色相差要求至 18°，允许更多色调变化
                if (result.Count >= 3) minHueDist = 12f;
            }

            // 第五步：水平位置归一化到 0~1 范围
            // 将各颜色的平均 x 坐标映射到 [0, 1] 区间，用于渐变色停的水平分布
            if (result.Count > 0)
            {
                var maxX = xPositions.Max();
                var minX = xPositions.Min();
                var xRange = maxX - minX;
                if (xRange > 0)
                {
                    // 线性归一化：(x - min) / (max - min)
                    foreach (var entry in result)
                        entry.CenterX = (entry.CenterX - minX) / xRange;
                }
                else
                {
                    // 所有色调的 x 坐标相同（如纯色封面），均匀分布
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
                        // 放宽亮度限制（0.1-0.95），尽量获取一个可用颜色
                        if (hsv[2] < 0.1f || hsv[2] > 0.95f) continue;
                        rSum += p.R; gSum += p.G; bSum += p.B;
                        count++;
                    }
                }
                if (count > 0)
                    result.Add(new ColorEntry
                    {
                        Color = Android.Graphics.Color.Rgb((int)(rSum / count), (int)(gSum / count), (int)(bSum / count)),
                        CenterX = 0.5f,
                        Weight = 1f
                    });
            }
        }
        catch { }

        return result;
    }

    /// <summary>
    /// 从图片文件路径提取主色调（封装了 Bitmap 的加载与回收）
    /// <para>内部调用 Extract(Bitmap) 完成实际提取逻辑，自动处理 Bitmap 的生命周期</para>
    /// </summary>
    /// <param name="filePath">图片文件的绝对路径</param>
    /// <returns>主色调列表（1-6 个 ColorEntry），若文件不存在或解码失败则返回空列表</returns>
    public static List<ColorEntry> ExtractFromFile(string filePath)
    {
        if (_fileCache.TryGetValue(filePath, out var cached))
        {
            try
            {
                var lastWrite = System.IO.File.GetLastWriteTimeUtc(filePath).Ticks;
                if (_fileCacheTimestamps.TryGetValue(filePath, out var ts) && ts == lastWrite)
                    return cached;
            }
            catch { }
            _fileCache.Remove(filePath);
            _fileCacheTimestamps.Remove(filePath);
        }

        var entries = new List<ColorEntry>();
        try
        {
            var options = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(filePath, options);
            var sampleSize = 1;
            if (options.OutWidth > MaxSampleSize || options.OutHeight > MaxSampleSize)
            {
                var ratio = Math.Max(options.OutWidth, options.OutHeight) / MaxSampleSize;
                var shift = 0;
                while ((1 << (shift + 1)) <= ratio) shift++;
                sampleSize = 1 << shift;
            }
            using var bitmap = BitmapFactory.DecodeFile(filePath, new BitmapFactory.Options { InSampleSize = sampleSize });
            if (bitmap != null)
            {
                entries = Extract(bitmap);
            }
            _fileCache[filePath] = entries;
            try { _fileCacheTimestamps[filePath] = System.IO.File.GetLastWriteTimeUtc(filePath).Ticks; } catch { }
        }
        catch { }
        return entries;
    }

    private static bool DetectGrayscaleCover(Bitmap bitmap)
    {
        int w = Math.Min(bitmap.Width, 60);
        int h = Math.Min(bitmap.Height, 60);
        int total = 0, grayCount = 0;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var pixel = new Color(bitmap.GetPixel(x * bitmap.Width / w, y * bitmap.Height / h));
                if (pixel.A < 128) continue;
                total++;
                float[] hsv = { 0, 0, 0 };
                Android.Graphics.Color.RGBToHSV(pixel.R, pixel.G, pixel.B, hsv);
                if (hsv[1] < 0.12f) grayCount++;
            }
        }

        return total > 0 && grayCount > total * 0.7;
    }

    private static List<ColorEntry> ExtractGrayscaleColors(Bitmap bitmap)
    {
        var result = new List<ColorEntry>();

        Bitmap? sampled = null;
        if (bitmap.Width > MaxSampleSize || bitmap.Height > MaxSampleSize)
        {
            var scale = (float)MaxSampleSize / Math.Max(bitmap.Width, bitmap.Height);
            sampled = Bitmap.CreateScaledBitmap(bitmap, Math.Max((int)(bitmap.Width * scale), 1), Math.Max((int)(bitmap.Height * scale), 1), false);
        }
        else
        {
            sampled = bitmap;
        }

        var grayFreq = new Dictionary<int, int>();
        var grayXSum = new Dictionary<int, long>();

        for (int y = 0; y < sampled.Height; y++)
        {
            for (int x = 0; x < sampled.Width; x++)
            {
                var pixel = new Color(sampled.GetPixel(x, y));
                if (pixel.A < 128) continue;

                var r = pixel.R / 64;
                var g = pixel.G / 64;
                var b = pixel.B / 64;
                var key = (r << 6) | (g << 3) | b;

                if (grayFreq.ContainsKey(key))
                {
                    grayFreq[key]++;
                    grayXSum[key] += x;
                }
                else
                {
                    grayFreq[key] = 1;
                    grayXSum[key] = x;
                }
            }
        }

        if (sampled != bitmap && sampled != null)
            sampled.Recycle();

        var grayScored = grayFreq
            .Select(kv =>
            {
                var r = ((kv.Key >> 6) & 0x7) * 64 + 32;
                var g = ((kv.Key >> 3) & 0x7) * 64 + 32;
                var b = (kv.Key & 0x7) * 64 + 32;
                var avgX = (float)grayXSum[kv.Key] / kv.Value;
                return (Color: Android.Graphics.Color.Rgb(r, g, b), Score: (double)kv.Value, AvgX: avgX);
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        var selectedColors = new List<int>();
        var selectedX = new List<float>();

        foreach (var entry in grayScored)
        {
            float[] hsv1 = { 0, 0, 0 };
            Android.Graphics.Color.RGBToHSV(
                Android.Graphics.Color.GetRedComponent(entry.Color),
                Android.Graphics.Color.GetGreenComponent(entry.Color),
                Android.Graphics.Color.GetBlueComponent(entry.Color), hsv1);

            bool isDup = false;
            foreach (var existing in selectedColors)
            {
                float[] hsv2 = { 0, 0, 0 };
                Android.Graphics.Color.RGBToHSV(
                    Android.Graphics.Color.GetRedComponent(existing),
                    Android.Graphics.Color.GetGreenComponent(existing),
                    Android.Graphics.Color.GetBlueComponent(existing), hsv2);
                if (Math.Abs(hsv1[2] - hsv2[2]) < 0.15f) { isDup = true; break; }
            }
            if (isDup) continue;

            selectedColors.Add(entry.Color);
            selectedX.Add(entry.AvgX);
            result.Add(new ColorEntry { Color = entry.Color, CenterX = entry.AvgX, Weight = (float)entry.Score });

            if (result.Count >= 3) break;
        }

        if (result.Count > 0)
        {
            var maxX = selectedX.Max();
            var minX = selectedX.Min();
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

        return result;
    }
}
