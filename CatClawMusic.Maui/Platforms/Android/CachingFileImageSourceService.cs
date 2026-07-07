using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Widget;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// 自定义 FileImageSource 服务：使用 <see cref="BitmapMemoryCache"/> 缓存解码后的 Bitmap，
/// 避免 CollectionView 滑动时反复解码同一封面图片造成 GC 压力。
/// 关键点：按 ImageView 实际显示尺寸降采样解码（列表缩略图约 256px 即可，原实现固定 1024px 过大），
/// 缓存 key 带尺寸桶，使同一封面在不同尺寸（列表 / 播放页大图）各自缓存一份且互不挤占。
/// 文件路径走缓存（降采样解码），资源名走 Android Resource 加载。
/// </summary>
public class CachingFileImageSourceService : IImageSourceService<FileImageSource>
{
    /// <summary>无显式尺寸时（GetDrawableAsync）的默认解码上限</summary>
    private const int DefaultTargetPx = 512;

    /// <summary>将 FileImageSource 加载到指定 ImageView，优先命中内存缓存</summary>
    public Task<IImageSourceServiceResult?> LoadDrawableAsync(IImageSource imageSource, ImageView imageView, CancellationToken cancellationToken = default)
    {
        if (imageView == null) return Task.FromResult<IImageSourceServiceResult?>(default);
        if (imageSource is FileImageSource fileSource && !string.IsNullOrEmpty(fileSource.File))
        {
            var targetPx = GetTargetPx(imageView);
            var bitmap = ResolveBitmap(fileSource.File, imageView.Context, targetPx);
            if (bitmap != null)
            {
                var drawable = new BitmapDrawable(imageView.Resources ?? imageView.Context?.Resources, bitmap);
                imageView.SetImageDrawable(drawable);
                return Task.FromResult<IImageSourceServiceResult?>(new CachingImageSourceResult(drawable));
            }
        }
        return Task.FromResult<IImageSourceServiceResult?>(default);
    }

    /// <summary>获取 FileImageSource 对应的 Drawable，优先命中内存缓存</summary>
    public Task<IImageSourceServiceResult<Drawable>?> GetDrawableAsync(IImageSource imageSource, Context context, CancellationToken cancellationToken = default)
    {
        if (imageSource is FileImageSource fileSource && !string.IsNullOrEmpty(fileSource.File))
        {
            var bitmap = ResolveBitmap(fileSource.File, context, DefaultTargetPx);
            if (bitmap != null)
            {
                var drawable = new BitmapDrawable(context?.Resources, bitmap);
                return Task.FromResult<IImageSourceServiceResult<Drawable>?>(new CachingDrawableResult(drawable));
            }
        }
        return Task.FromResult<IImageSourceServiceResult<Drawable>?>(default);
    }

    /// <summary>
    /// 根据 ImageView 的实际显示尺寸（含设备密度、1.5x 过采样）计算目标解码边长。
    /// 列表缩略图通常为 58~160dp，解码到约 256px 已足够清晰；未知尺寸时回退默认值。
    /// </summary>
    private static int GetTargetPx(ImageView imageView)
    {
        try
        {
            if (imageView.Width > 0 && imageView.Height > 0)
            {
                var density = imageView.Resources?.DisplayMetrics?.Density ?? 1f;
                if (density <= 0) density = 1f;
                var dpMax = Math.Max(imageView.Width, imageView.Height) / density;
                var px = (int)(dpMax * density * 1.5f);
                return Math.Clamp(px, 64, 512);
            }
        }
        catch
        {
            // 尺寸读取异常时回退默认值
        }
        return 256;
    }

    /// <summary>
    /// 解析 FileImageSource.File 为 Bitmap：
    /// - 文件路径：按尺寸桶命中内存缓存，未命中则降采样解码后入缓存
    /// - 资源名：从 Android Resource 加载（资源已打包在 APK 中，无需缓存）
    /// </summary>
    private static Bitmap? ResolveBitmap(string path, Context? context, int targetPx)
    {
        if (string.IsNullOrEmpty(path)) return null;

        // 文件路径：走缓存（尺寸桶 key 避免不同显示尺寸互相挤占）
        if (File.Exists(path))
        {
            var bucket = Math.Max(64, (targetPx / 64) * 64);
            var cacheKey = $"{path}@{bucket}";
            var cached = BitmapMemoryCache.Get(cacheKey);
            if (cached != null) return cached;

            var decoded = DecodeBitmapDownsampled(path, bucket);
            if (decoded != null)
                BitmapMemoryCache.Put(cacheKey, decoded);
            return decoded;
        }

        // 资源名：从 Android drawable 资源加载
        if (context != null)
        {
            // 去掉可能的文件扩展名（avatar_yuki.png → avatar_yuki）
            var resourceName = System.IO.Path.GetFileNameWithoutExtension(path);
            var resourceId = context.Resources?.GetIdentifier(resourceName, "drawable", context.PackageName) ?? 0;
            if (resourceId != 0)
            {
                try { return BitmapFactory.DecodeResource(context.Resources, resourceId); }
                catch { return null; }
            }
        }

        return null;
    }

    /// <summary>降采样解码 Bitmap：长边限制为目标像素，兼顾清晰度与内存/解码耗时</summary>
    private static Bitmap? DecodeBitmapDownsampled(string path, int targetPx)
    {
        try
        {
            // 第一遍只解析尺寸
            var options = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, options);
            if (options.OutWidth <= 0 || options.OutHeight <= 0) return null;

            // 计算降采样倍数（长边目标 targetPx）
            var maxDim = Math.Max(options.OutWidth, options.OutHeight);
            var sampleSize = 1;
            while (maxDim / sampleSize > targetPx) sampleSize *= 2;

            // 第二遍真正解码
            options.InJustDecodeBounds = false;
            options.InSampleSize = sampleSize;
            options.InPreferredConfig = Bitmap.Config.Argb8888;
            return BitmapFactory.DecodeFile(path, options);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CachingFileImageSourceService] Decode failed: {path} - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// IImageSourceServiceResult 实现：缓存模式下 bitmap 由缓存统一管理生命周期，
    /// Dispose 不释放共享 Bitmap（同一实例可能被多个 ImageView 引用）。
    /// Value 返回实际 Drawable，符合 MAUI 契约，避免被默认解码路径二次处理。
    /// </summary>
    private sealed class CachingImageSourceResult : IImageSourceServiceResult
    {
        private readonly Drawable? _drawable;
        public CachingImageSourceResult(Drawable? drawable) => _drawable = drawable;
        public object? Value => _drawable;
        public bool IsResolutionDependent => false;
        public bool IsDisposed => false;
        public void Dispose() { /* 不释放共享 Bitmap */ }
    }

    /// <summary>
    /// IImageSourceServiceResult&lt;Drawable&gt; 实现：包装 BitmapDrawable，
    /// Dispose 不释放共享 Bitmap（同一实例可能被多个 ImageView 引用）。
    /// </summary>
    private sealed class CachingDrawableResult : IImageSourceServiceResult<Drawable>
    {
        private readonly Drawable _drawable;
        public Drawable Value => _drawable;
        public bool IsResolutionDependent => false;
        public bool IsDisposed => false;
        public CachingDrawableResult(Drawable drawable) => _drawable = drawable;
        public void Dispose() { /* 不释放共享 Bitmap */ }
    }
}
