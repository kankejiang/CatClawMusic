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
/// 文件路径走缓存（降采样解码），资源名走 Android Resource 加载。
/// </summary>
public class CachingFileImageSourceService : IImageSourceService<FileImageSource>
{
    /// <summary>将 FileImageSource 加载到指定 ImageView，优先命中内存缓存</summary>
    public Task<IImageSourceServiceResult?> LoadDrawableAsync(IImageSource imageSource, ImageView imageView, CancellationToken cancellationToken = default)
    {
        if (imageSource is FileImageSource fileSource)
        {
            var bitmap = ResolveBitmap(fileSource.File, imageView.Context);
            if (bitmap != null)
            {
                imageView.SetImageBitmap(bitmap);
                return Task.FromResult<IImageSourceServiceResult?>(new CachingImageSourceResult());
            }
        }
        return Task.FromResult<IImageSourceServiceResult?>(default);
    }

    /// <summary>获取 FileImageSource 对应的 Drawable，优先命中内存缓存</summary>
    public Task<IImageSourceServiceResult<Drawable>?> GetDrawableAsync(IImageSource imageSource, Context context, CancellationToken cancellationToken = default)
    {
        if (imageSource is FileImageSource fileSource)
        {
            var bitmap = ResolveBitmap(fileSource.File, context);
            if (bitmap != null)
            {
                var drawable = new BitmapDrawable(context.Resources, bitmap);
                return Task.FromResult<IImageSourceServiceResult<Drawable>?>(new CachingDrawableResult(drawable));
            }
        }
        return Task.FromResult<IImageSourceServiceResult<Drawable>?>(default);
    }

    /// <summary>
    /// 解析 FileImageSource.File 为 Bitmap：
    /// - 文件路径：从内存缓存获取或降采样解码后入缓存
    /// - 资源名：从 Android Resource 加载（资源已打包在 APK 中，无需缓存）
    /// </summary>
    private static Bitmap? ResolveBitmap(string? path, Context? context)
    {
        if (string.IsNullOrEmpty(path)) return null;

        // 文件路径：走缓存
        if (File.Exists(path))
        {
            var cached = BitmapMemoryCache.Get(path);
            if (cached != null) return cached;

            var decoded = DecodeBitmapDownsampled(path);
            if (decoded != null)
                BitmapMemoryCache.Put(path, decoded);
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

    /// <summary>降采样解码 Bitmap：长边限制 1024px，兼顾播放页大封面与列表卡片显示质量</summary>
    private static Bitmap? DecodeBitmapDownsampled(string path)
    {
        try
        {
            // 第一遍只解析尺寸
            var options = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, options);

            // 计算降采样倍数（长边目标 1024px）
            var maxDim = Math.Max(options.OutWidth, options.OutHeight);
            var sampleSize = 1;
            while (maxDim / sampleSize > 1024) sampleSize *= 2;

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
    /// </summary>
    private sealed class CachingImageSourceResult : IImageSourceServiceResult
    {
        public object? Value => null;
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
