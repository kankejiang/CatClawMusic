using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Widget;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using System.Threading;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// 全局兜底：为 <see cref="StreamImageSource"/> 提供降采样解码。
/// 任何走 <c>ImageSource.FromStream</c> 的图片（自定义背景、设置预览、未来新增路径）
/// 默认都会被 MAUI 的 StreamImageSource 处理器<b>原分辨率</b>解码，超大图（如 7168×7168）
/// 会直接抛出 <c>Canvas: trying to draw too large</c> 崩溃。本服务用 <c>BitmapFactory</c> 局部解码，
/// 先读头部算 InSampleSize，再按需解码，永远不分配整图内存。
/// 关键：不把整张流读进 byte[]（否则同样 OOM），而是对两次独立流分别做「读边界」与「按 InSampleSize 解码」。
/// </summary>
public class CachingStreamImageSourceService : IImageSourceService<StreamImageSource>
{
    private const int DefaultTargetPx = 1024;

    private static readonly SemaphoreSlim _decodeSemaphore = new(8, 8);

    public async Task<IImageSourceServiceResult?> LoadDrawableAsync(IImageSource imageSource, ImageView imageView, CancellationToken cancellationToken = default)
    {
        if (imageView == null) return default;
        if (imageSource is IStreamImageSource streamSource)
        {
            var targetPx = GetTargetPx(imageView);
            var bitmap = await ResolveBitmapAsync(streamSource, imageView.Context, targetPx, cancellationToken).ConfigureAwait(true);
            if (bitmap != null)
            {
                var drawable = new BitmapDrawable(imageView.Resources ?? imageView.Context?.Resources, bitmap);
                imageView.SetImageDrawable(drawable);
                return new CachingImageSourceResult(drawable);
            }
        }
        return default;
    }

    public async Task<IImageSourceServiceResult<Drawable>?> GetDrawableAsync(IImageSource imageSource, Context context, CancellationToken cancellationToken = default)
    {
        if (imageSource is IStreamImageSource streamSource)
        {
            var bitmap = await ResolveBitmapAsync(streamSource, context, DefaultTargetPx, cancellationToken).ConfigureAwait(true);
            if (bitmap != null)
            {
                var drawable = new BitmapDrawable(context?.Resources, bitmap);
                return new CachingDrawableResult(drawable);
            }
        }
        return default;
    }

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
                return Math.Clamp(px, 64, 1024);
            }
        }
        catch { }
        return 256;
    }

    /// <summary>
    /// 从 StreamImageSource 解析降采样 Bitmap：
    /// 1) 第一次取流仅读尺寸边界（InJustDecodeBounds，几乎零开销）；
    /// 2) 计算 InSampleSize；
    /// 3) 第二次取流按 InSampleSize 解码（只分配缩小后的内存）。
    /// 两次通过 GetStreamAsync 取得独立流，避免单流不可重复读的问题；
    /// 且绝不把整张流读进 byte[]（否则超大图同样 OOM）。
    /// </summary>
    private static async Task<Bitmap?> ResolveBitmapAsync(IStreamImageSource streamSource, Context? context, int targetPx, CancellationToken ct)
    {
        await _decodeSemaphore.WaitAsync(ct);
        try
        {
            // 第一遍：仅解析尺寸
            int outW, outH;
            {
                using var boundsStream = await streamSource.GetStreamAsync(ct).ConfigureAwait(false);
                if (boundsStream == null) return null;
                var boundsOpts = new BitmapFactory.Options { InJustDecodeBounds = true };
                BitmapFactory.DecodeStream(boundsStream, null, boundsOpts);
                outW = boundsOpts.OutWidth;
                outH = boundsOpts.OutHeight;
            }
            if (outW <= 0 || outH <= 0) return null;

            var maxDim = Math.Max(outW, outH);
            var sampleSize = 1;
            while (maxDim / sampleSize > targetPx) sampleSize *= 2;

            // 第二遍：按 InSampleSize 局部解码（不缓冲整图）
            using var decodeStream = await streamSource.GetStreamAsync(ct).ConfigureAwait(false);
            if (decodeStream == null) return null;
            var decodeOpts = new BitmapFactory.Options
            {
                InJustDecodeBounds = false,
                InSampleSize = sampleSize,
                InPreferredConfig = Bitmap.Config.Argb8888
            };
            return BitmapFactory.DecodeStream(decodeStream, null, decodeOpts);
        }
        catch (Exception ex)
        {
            Log.Debug("CachingStreamImageSourceService", $"[CachingStreamImageSourceService] Resolve failed: {ex.Message}");
            return null;
        }
        finally
        {
            _decodeSemaphore.Release();
        }
    }

    private sealed class CachingImageSourceResult : IImageSourceServiceResult
    {
        private readonly Drawable? _drawable;
        public CachingImageSourceResult(Drawable? drawable) => _drawable = drawable;
        public object? Value => _drawable;
        public bool IsResolutionDependent => false;
        public bool IsDisposed => false;
        public void Dispose() { /* 不释放共享 Bitmap */ }
    }

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
