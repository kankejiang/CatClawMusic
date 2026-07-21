using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Widget;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Services;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// 自定义 UriImageSource 服务：为 http(s):// 封面 URL 提供 Bitmap 内存缓存 + 磁盘缓存，
/// 避免 CollectionView 滑动时每次都下载图片造成 LOS 堆 GC 风暴。
/// 流程：BitmapMemoryCache 命中 → 零开销返回；否则查磁盘缓存文件 → 解码入缓存；
/// 磁盘未命中 → 下载 byte[] 写磁盘 → 解码入缓存。下载的 byte[] 立即释放不持有。
/// </summary>
public class CachingUriImageSourceService : IImageSourceService<UriImageSource>
{
    private const int DefaultTargetPx = 512;

    /// <summary>并发解码/下载信号量：限制同时进行的封面解码数（默认 8），平滑设备负载。</summary>
    private static readonly SemaphoreSlim _decodeSemaphore = new(8, 8);

    private static readonly string _diskCacheDir = ImageCacheService.Instance.CoverCacheDirectory;

    /// <summary>按 URL 去重下载/解码任务，避免并发请求同一封面重复下载</summary>
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> _inflight = new();

    static CachingUriImageSourceService()
    {
        try { Directory.CreateDirectory(_diskCacheDir); } catch { }
    }

    public async Task<IImageSourceServiceResult?> LoadDrawableAsync(IImageSource imageSource, ImageView imageView, CancellationToken cancellationToken = default)
    {
        if (imageView == null) return default;
        if (imageSource is UriImageSource uriSource && uriSource.Uri?.IsAbsoluteUri == true)
        {
            var url = uriSource.Uri.AbsoluteUri;
            var targetPx = GetTargetPx(imageView);
            var bitmap = await ResolveBitmapAsync(url, imageView.Context, targetPx, cancellationToken).ConfigureAwait(true);
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
        if (imageSource is UriImageSource uriSource && uriSource.Uri?.IsAbsoluteUri == true)
        {
            var url = uriSource.Uri.AbsoluteUri;
            var bitmap = await ResolveBitmapAsync(url, context, DefaultTargetPx, cancellationToken).ConfigureAwait(true);
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
                return Math.Clamp(px, 64, 512);
            }
        }
        catch { }
        return 256;
    }

    /// <summary>
    /// 解析 URL 为 Bitmap（三级缓存：Bitmap 内存 → 磁盘文件 → 网络下载）。
    /// 用 ConcurrentDictionary 去重并发请求，避免同一 URL 重复下载/解码。
    /// </summary>
    private static Task<Bitmap?> ResolveBitmapAsync(string url, Context? context, int targetPx, CancellationToken ct)
    {
        var bucket = Math.Max(64, (targetPx / 64) * 64);
        var cacheKey = $"{url}@{bucket}";

        // 快速路径：Bitmap 内存缓存命中
        var cached = BitmapMemoryCache.Get(cacheKey);
        if (cached != null) return Task.FromResult(cached);

        // 慢路径：去重后下载/解码
        return _inflight.GetOrAdd(cacheKey, _ => Task.Run(async () =>
        {
            try
            {
                // 再次检查缓存（可能在等待锁期间已被其他线程填充）
                var existing = BitmapMemoryCache.Get(cacheKey);
                if (existing != null) return existing;

                var diskPath = GetDiskPath(url);
                Bitmap? bmp = null;

                // 限制并发解码/下载数，避免数百张同时发起打满线程池与带宽
                await _decodeSemaphore.WaitAsync(ct);
                try
                {
                    // 磁盘缓存命中
                    if (File.Exists(diskPath))
                    {
                        bmp = DecodeBitmapDownsampled(diskPath, bucket);
                    }

                    // 磁盘未命中：下载并写文件
                    if (bmp == null)
                    {
                        bmp = await DownloadAndDecodeAsync(url, diskPath, bucket, ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _decodeSemaphore.Release();
                }

                if (bmp != null)
                    BitmapMemoryCache.Put(cacheKey, bmp);
                return bmp;
            }
            catch (Exception ex)
            {
                Log.Debug("CachingUriImageSourceService", $"[CachingUriImageSourceService] Resolve failed: {url} - {ex.Message}");
                return null;
            }
            finally
            {
                _inflight.TryRemove(cacheKey, out var _);
            }
        }, ct));
    }

    /// <summary>下载 URL 到磁盘文件并降采样解码。byte[] 下载后立即释放，不长期持有。</summary>
    private static async Task<Bitmap?> DownloadAndDecodeAsync(string url, string diskPath, int targetPx, CancellationToken ct)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var bytes = await httpClient.GetByteArrayAsync(url, ct).ConfigureAwait(false);
        if (bytes.Length == 0) return null;

        // 写磁盘缓存（后续命中即免下载）
        try
        {
            await File.WriteAllBytesAsync(diskPath, bytes, ct).ConfigureAwait(false);
            ImageCacheService.Instance.NotifyWritten(bytes.Length);
        }
        catch { }

        // 从 byte[] 解码（降采样），解码后 bytes 即可被 GC 回收
        return DecodeBitmapFromBytes(bytes, targetPx);
    }

    private static string GetDiskPath(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        var sb = new StringBuilder(64);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return System.IO.Path.Combine(_diskCacheDir, sb.ToString(0, 32) + ".jpg");
    }

    private static Bitmap? DecodeBitmapDownsampled(string path, int targetPx)
    {
        try
        {
            var options = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, options);
            if (options.OutWidth <= 0 || options.OutHeight <= 0) return null;

            var maxDim = Math.Max(options.OutWidth, options.OutHeight);
            var sampleSize = 1;
            while (maxDim / sampleSize > targetPx) sampleSize *= 2;

            options.InJustDecodeBounds = false;
            options.InSampleSize = sampleSize;
            options.InPreferredConfig = Bitmap.Config.Argb8888;
            return BitmapFactory.DecodeFile(path, options);
        }
        catch { return null; }
    }

    private static Bitmap? DecodeBitmapFromBytes(byte[] bytes, int targetPx)
    {
        try
        {
            var options = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options);
            if (options.OutWidth <= 0 || options.OutHeight <= 0) return null;

            var maxDim = Math.Max(options.OutWidth, options.OutHeight);
            var sampleSize = 1;
            while (maxDim / sampleSize > targetPx) sampleSize *= 2;

            options.InJustDecodeBounds = false;
            options.InSampleSize = sampleSize;
            options.InPreferredConfig = Bitmap.Config.Argb8888;
            return BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options);
        }
        catch { return null; }
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
