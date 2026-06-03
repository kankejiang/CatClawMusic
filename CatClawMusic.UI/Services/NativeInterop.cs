using Android.Graphics;
using CatClawMusic.Core.Services;

namespace CatClawMusic.UI.Services;

/// <summary>
/// Android 平台特有的原生库扩展方法，封装需要 Android Bitmap 的取色逻辑
/// </summary>
public static class NativeInteropAndroid
{
    [ThreadStatic]
    private static int[]? _cachedPixelBuffer;
    [ThreadStatic]
    private static uint[]? _cachedUintBuffer;

    /// <summary>
    /// 使用原生库从 Bitmap 提取主色调
    /// 如果原生库不可用，返回 null（由 C# 回退逻辑处理）
    /// </summary>
    public static List<ColorEntry>? ExtractColorsFromBitmap(Bitmap? bitmap)
    {
        if (!NativeInterop.IsAvailable || bitmap == null || bitmap.IsRecycled)
            return null;

        try
        {
            const int maxSampleSize = 120;
            Bitmap? sampled = null;
            int sampleW, sampleH;
            if (bitmap.Width > maxSampleSize || bitmap.Height > maxSampleSize)
            {
                var scale = (float)maxSampleSize / Math.Max(bitmap.Width, bitmap.Height);
                sampleW = Math.Max((int)(bitmap.Width * scale), 1);
                sampleH = Math.Max((int)(bitmap.Height * scale), 1);
                sampled = Bitmap.CreateScaledBitmap(bitmap, sampleW, sampleH, false);
            }
            else
            {
                sampled = bitmap;
                sampleW = sampled.Width;
                sampleH = sampled.Height;
            }

            int pixelCount = sampleW * sampleH;

            if (_cachedPixelBuffer == null || _cachedPixelBuffer.Length < pixelCount)
                _cachedPixelBuffer = new int[pixelCount];
            if (_cachedUintBuffer == null || _cachedUintBuffer.Length < pixelCount)
                _cachedUintBuffer = new uint[pixelCount];

            sampled.GetPixels(_cachedPixelBuffer, 0, sampleW, 0, 0, sampleW, sampleH);
            for (int i = 0; i < pixelCount; i++)
                _cachedUintBuffer[i] = (uint)_cachedPixelBuffer[i];

            if (sampled != bitmap && sampled != null)
                sampled.Recycle();

            var entries = new NativeInterop.NativeColorEntry[6];
            int count = NativeInterop.ExtractColors(_cachedUintBuffer, sampleW, sampleH, 6, entries);

            if (count <= 0) return null;

            var result = new List<ColorEntry>();
            for (int i = 0; i < count; i++)
            {
                result.Add(new ColorEntry
                {
                    Color = entries[i].Color,
                    CenterX = entries[i].CenterX,
                    Weight = entries[i].Weight
                });
            }
            return result;
        }
        catch
        {
            return null;
        }
    }
}
