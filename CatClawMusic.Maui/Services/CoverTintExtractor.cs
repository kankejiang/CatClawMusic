using System;
using System.IO;
using CatClawMusic.Core.ColorQuantize;
using CatClawMusic.Maui.Services.Frosted;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 从封面提取强调色（Halcyon 方案，供流光背景随封面着色）。
/// 平台私有：Android 用 BitmapFactory 采样解码，Windows 用 BitmapDecoder 缩略解码，
/// 统一转成 ARGB 喂给共享的 AccentColorPicker（样本数 × 饱和度 × 亮度平衡打分，避开灰黑灰白）。
/// 解码到 ~480px 长边（与 Halcyon 一致），避免过低分辨率把颜色平均成灰、稀释饱和度；跑在后台线程。
/// </summary>
public static class CoverTintExtractor
{
    private const int MaxSide = 480;

    /// <summary>封面视觉分析结果：主题色 + 封面流背景源</summary>
    public readonly record struct CoverVisualData(Color? Tint, CoverFlowProcessor.CoverSource Source);

    /// <summary>一次性解码封面并同时产出主题色与封面流源（旧流程 Extract + ExtractCoverSource
    /// 各自完整解码同一张图两次，480px ARGB 解码 ≈ 每次 0.9MB 像素分配）。</summary>
    public static CoverVisualData ExtractVisualData(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return default;
#if ANDROID
        return ExtractAndroidVisual(o => global::Android.Graphics.BitmapFactory.DecodeFile(path, o));
#elif WINDOWS
        try
        {
            var file = Windows.Storage.StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
            using var stream = file.OpenReadAsync().AsTask().GetAwaiter().GetResult();
            var decoder = Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
            return DecodeVisual(decoder);
        }
        catch { return default; }
#else
        return default;
#endif
    }

    /// <summary>从内存字节一次性解码封面并产出主题色与封面流源（在线封面直显，不落盘）。</summary>
    public static CoverVisualData ExtractVisualData(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return default;
#if ANDROID
        return ExtractAndroidVisual(o => global::Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, o));
#elif WINDOWS
        try
        {
            using var ms = new MemoryStream(bytes);
            using var stream = ms.AsRandomAccessStream();
            var decoder = Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
            return DecodeVisual(decoder);
        }
        catch { return default; }
#else
        return default;
#endif
    }

#if WINDOWS
    /// <summary>共享：一次像素解码同时算主题色与封面流源。</summary>
    private static CoverVisualData DecodeVisual(Windows.Graphics.Imaging.BitmapDecoder decoder)
    {
        uint ow = decoder.PixelWidth, oh = decoder.PixelHeight;
        if (ow == 0 || oh == 0) return default;
        double scale = Math.Min(1.0, MaxSide / (double)Math.Max(ow, oh));
        uint tw = (uint)Math.Max(1, ow * scale);
        uint th = (uint)Math.Max(1, oh * scale);

        var transform = new Windows.Graphics.Imaging.BitmapTransform
        {
            ScaledWidth = tw,
            ScaledHeight = th,
            InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Fant,
        };
        var data = decoder.GetPixelDataAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            transform,
            Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage).AsTask().GetAwaiter().GetResult();
        var bytes = data.DetachPixelData();

        // BGRA → ARGB
        var argb = new int[tw * th];
        for (int i = 0, idx = 0; idx + 3 < bytes.Length; i++, idx += 4)
        {
            argb[i] = (bytes[idx + 3] << 24) | (bytes[idx + 2] << 16) | (bytes[idx + 1] << 8) | bytes[idx];
        }
        var tint = ToneToColor(argb, (int)tw, (int)th);
        var source = CoverFlowProcessor.ScaleSource(argb, (int)tw, (int)th, CoverFlowProcessor.SourceMaxDim);
        return new CoverVisualData(tint, source);
    }
#endif

#if ANDROID
    /// <summary>Android 共享：一次采样解码同时算主题色与封面流源（强制 ARGB_8888 防色偏）。</summary>
    private static CoverVisualData ExtractAndroidVisual(
        Func<global::Android.Graphics.BitmapFactory.Options, global::Android.Graphics.Bitmap?> decode)
    {
        var opts = new global::Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        try { decode(opts); } catch { return default; }
        int w = opts.OutWidth, h = opts.OutHeight;
        if (w <= 0 || h <= 0) return default;

        int sample = (int)Math.Ceiling(Math.Max(w, h) / (double)MaxSide);
        if (sample < 1) sample = 1;
        opts.InJustDecodeBounds = false;
        opts.InSampleSize = sample;
        opts.InPreferredConfig = global::Android.Graphics.Bitmap.Config.Argb8888!;

        global::Android.Graphics.Bitmap? bmp = null;
        try { bmp = decode(opts); } catch { return default; }
        if (bmp == null) return default;

        try
        {
            var px = new int[bmp.Width * bmp.Height];
            bmp.GetPixels(px, 0, bmp.Width, 0, 0, bmp.Width, bmp.Height);
            var tint = ToneToColor(px, bmp.Width, bmp.Height);
            var source = CoverFlowProcessor.ScaleSource(px, bmp.Width, bmp.Height, CoverFlowProcessor.SourceMaxDim);
            return new CoverVisualData(tint, source);
        }
        finally { bmp.Recycle(); }
    }
#endif

    /// <summary>提取封面强调色；路径无效或取色失败返回 null。</summary>
    public static Color? Extract(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
#if ANDROID
        return ExtractAndroid(path);
#elif WINDOWS
        return ExtractWindowsOptimistic(path);
#else
        return null;
#endif
    }

    /// <summary>从内存字节解码封面并提取强调色（用于在线封面直显，不落盘）。</summary>
    public static Color? Extract(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
#if ANDROID
        return ExtractAndroid(bytes);
#elif WINDOWS
        return ExtractWindowsOptimistic(bytes);
#else
        return null;
#endif
    }

    /// <summary>共享：ARGB 像素流 → 强调色 → MAUI Color。</summary>
    private static Color? ToneToColor(int[] argb, int width, int height)
    {
        int accent = AccentColorPicker.Calculate(argb, width, height);
        return accent == 0
            ? null
            : Microsoft.Maui.Graphics.Color.FromRgb(
                (accent >> 16) & 0xFF, (accent >> 8) & 0xFF, accent & 0xFF);
    }

    /// <summary>解码封面为封面流源（ARGB，长边缩到 ≤ CoverFlowProcessor.SourceMaxDim）。</summary>
    public static CoverFlowProcessor.CoverSource ExtractCoverSource(string path)
    {
#if ANDROID
        return ExtractAndroidCover(path);
#elif WINDOWS
        return ExtractWindowsCover(path);
#else
        return default;
#endif
    }

    /// <summary>从内存字节解码封面为封面流源（用于在线封面直显，不落盘）。</summary>
    public static CoverFlowProcessor.CoverSource ExtractCoverSource(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return default;
#if ANDROID
        return ExtractAndroidCover(bytes);
#elif WINDOWS
        return ExtractWindowsCover(bytes);
#else
        return default;
#endif
    }

#if ANDROID
    private static Color? ExtractAndroid(string path)
        => ExtractAndroidPixels(o => global::Android.Graphics.BitmapFactory.DecodeFile(path, o));

    private static CoverFlowProcessor.CoverSource ExtractAndroidCover(string path)
        => ExtractAndroidCoverPixels(o => global::Android.Graphics.BitmapFactory.DecodeFile(path, o));

    private static CoverFlowProcessor.CoverSource ExtractAndroidCover(byte[] bytes)
        => ExtractAndroidCoverPixels(o => global::Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, o));

    /// <summary>Android 封面流：采样解码到 ~MaxSide 后取像素，再缩到封面流源尺寸。
    /// 强制 ARGB_8888：BitmapFactory 对 JPEG 默认 RGB_565(5/6/5 量化)，会被封面流放大铺满全屏时暴露色偏。</summary>
    private static CoverFlowProcessor.CoverSource ExtractAndroidCoverPixels(
        Func<global::Android.Graphics.BitmapFactory.Options, global::Android.Graphics.Bitmap?> decode)
    {
        var opts = new global::Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        try { decode(opts); } catch { return default; }
        int w = opts.OutWidth, h = opts.OutHeight;
        if (w <= 0 || h <= 0) return default;

        int sample = (int)Math.Ceiling(Math.Max(w, h) / (double)MaxSide);
        if (sample < 1) sample = 1;
        opts.InJustDecodeBounds = false;
        opts.InSampleSize = sample;
        opts.InPreferredConfig = global::Android.Graphics.Bitmap.Config.Argb8888!;

        global::Android.Graphics.Bitmap? bmp = null;
        try { bmp = decode(opts); } catch { return default; }
        if (bmp == null) return default;
        try
        {
            var px = new int[bmp.Width * bmp.Height];
            bmp.GetPixels(px, 0, bmp.Width, 0, 0, bmp.Width, bmp.Height);
            return CoverFlowProcessor.ScaleSource(px, bmp.Width, bmp.Height, CoverFlowProcessor.SourceMaxDim);
        }
        finally { bmp.Recycle(); }
    }

    private static Color? ExtractAndroid(byte[] bytes)
        => ExtractAndroidPixels(o => global::Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, o));

    /// <summary>Android 路径与字节流共用：先用别名解码测尺寸定采样率，再全解码取像素。</summary>
    private static Color? ExtractAndroidPixels(Func<global::Android.Graphics.BitmapFactory.Options, global::Android.Graphics.Bitmap?> decode)
    {
        var opts = new global::Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        try { decode(opts); } catch { return null; }
        int w = opts.OutWidth, h = opts.OutHeight;
        if (w <= 0 || h <= 0) return null;

        int sample = (int)Math.Ceiling(Math.Max(w, h) / (double)MaxSide);
        if (sample < 1) sample = 1;
        opts.InJustDecodeBounds = false;
        opts.InSampleSize = sample;
        opts.InPreferredConfig = global::Android.Graphics.Bitmap.Config.Argb8888!;

        global::Android.Graphics.Bitmap? bmp = null;
        try { bmp = decode(opts); } catch { return null; }
        if (bmp == null) return null;

        try
        {
            var px = new int[bmp.Width * bmp.Height];
            bmp.GetPixels(px, 0, bmp.Width, 0, 0, bmp.Width, bmp.Height);
            return ToneToColor(px, bmp.Width, bmp.Height);
        }
        finally { bmp.Recycle(); }
    }
#endif

#if WINDOWS
    private static Color? ExtractWindowsOptimistic(string path)
    {
        try { return ExtractWindows(path); }
        catch { return null; }
    }

    private static Color? ExtractWindowsOptimistic(byte[] bytes)
    {
        try { return ExtractWindows(bytes); }
        catch { return null; }
    }

    private static Color? ExtractWindows(string path)
    {
        var file = Windows.Storage.StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
        using var stream = file.OpenReadAsync().AsTask().GetAwaiter().GetResult();
        var decoder = Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
        return DecodeTone(decoder);
    }

    private static Color? ExtractWindows(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var stream = ms.AsRandomAccessStream();
        var decoder = Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
        return DecodeTone(decoder);
    }

    private static Color? DecodeTone(Windows.Graphics.Imaging.BitmapDecoder decoder)
    {
        uint ow = decoder.PixelWidth, oh = decoder.PixelHeight;
        if (ow == 0 || oh == 0) return null;
        double scale = Math.Min(1.0, MaxSide / (double)Math.Max(ow, oh));
        uint tw = (uint)Math.Max(1, ow * scale);
        uint th = (uint)Math.Max(1, oh * scale);

        var transform = new Windows.Graphics.Imaging.BitmapTransform
        {
            ScaledWidth = tw,
            ScaledHeight = th,
            InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Fant,
        };
        var data = decoder.GetPixelDataAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            transform,
            Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage).AsTask().GetAwaiter().GetResult();
        var bytes = data.DetachPixelData();

        // BGRA → ARGB
        var argb = new int[tw * th];
        for (int i = 0, idx = 0; idx + 3 < bytes.Length; i++, idx += 4)
        {
            int a = bytes[idx + 3];
            int r = bytes[idx + 2];
            int g = bytes[idx + 1];
            int b = bytes[idx];
            argb[i] = (a << 24) | (r << 16) | (g << 8) | b;
        }
        return ToneToColor(argb, (int)tw, (int)th);
    }

    /// <summary>Windows 封面流源：解码 + 缩到封面流源尺寸。</summary>
    private static CoverFlowProcessor.CoverSource ExtractWindowsCover(string path)
    {
        try { return ExtractWindowsCoverCore(path); }
        catch { return default; }
    }

    private static CoverFlowProcessor.CoverSource ExtractWindowsCover(byte[] bytes)
    {
        try { return ExtractWindowsCoverCore(bytes); }
        catch { return default; }
    }

    private static CoverFlowProcessor.CoverSource ExtractWindowsCoverCore(string path)
    {
        var file = Windows.Storage.StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
        using var stream = file.OpenReadAsync().AsTask().GetAwaiter().GetResult();
        var decoder = Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
        return DecodeCoverSource(decoder);
    }

    private static CoverFlowProcessor.CoverSource ExtractWindowsCoverCore(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var stream = ms.AsRandomAccessStream();
        var decoder = Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
        return DecodeCoverSource(decoder);
    }

    private static CoverFlowProcessor.CoverSource DecodeCoverSource(Windows.Graphics.Imaging.BitmapDecoder decoder)
    {
        uint ow = decoder.PixelWidth, oh = decoder.PixelHeight;
        if (ow == 0 || oh == 0) return default;
        double scale = Math.Min(1.0, MaxSide / (double)Math.Max(ow, oh));
        uint tw = (uint)Math.Max(1, ow * scale);
        uint th = (uint)Math.Max(1, oh * scale);
        var transform = new Windows.Graphics.Imaging.BitmapTransform
        {
            ScaledWidth = tw, ScaledHeight = th,
            InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Fant,
        };
        var data = decoder.GetPixelDataAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            transform,
            Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage).AsTask().GetAwaiter().GetResult();
        var bytes = data.DetachPixelData();
        var argb = new int[tw * th];
        for (int i = 0, idx = 0; idx + 3 < bytes.Length; i++, idx += 4)
        {
            argb[i] = (bytes[idx + 3] << 24) | (bytes[idx + 2] << 16) | (bytes[idx + 1] << 8) | bytes[idx];
        }
        return CoverFlowProcessor.ScaleSource(argb, (int)tw, (int)th, CoverFlowProcessor.SourceMaxDim);
    }
#endif
}