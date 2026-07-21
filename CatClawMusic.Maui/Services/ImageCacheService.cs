using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 图片缓存管理：负责 cover_url_cache（网络封面）的容量/时效淘汰，
/// 并提供 artist_covers / album_covers 的手动清理入口。
/// 注意：自动 LRU + 时效淘汰仅作用于 cover_url_cache；
/// 艺术家/专辑封面由抓取器写入，手动清除即可，不参与自动淘汰，避免 DB 引用失效。
/// </summary>
public class ImageCacheService
{
    private static readonly Lazy<ImageCacheService> _lazy = new(() => new ImageCacheService());
    public static ImageCacheService Instance => _lazy.Value;

    public const int DefaultCacheSizeLimitMB = 200;
    public const int DefaultCacheAgeDays = 30;

    public const int MinCacheSizeLimitMB = 50;
    public const int MaxCacheSizeLimitMB = 2000;

    public const int MinCacheAgeDays = 1;
    public const int MaxCacheAgeDays = 365;

    private const string CoverCacheDirName = "cover_url_cache";
    private const string ArtistCoversDirName = "artist_covers";
    private const string AlbumCoversDirName = "album_covers";

    private const string PrefCacheSizeLimitMB = "image_cache_size_mb";
    private const string PrefCacheAgeDays = "image_cache_age_days";

    private readonly string _coverDir;
    private readonly string _artistDir;
    private readonly string _albumDir;
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private long _cacheSizeBytes;

    private ImageCacheService()
    {
        var appData = FileSystem.AppDataDirectory;
        _coverDir = Path.Combine(appData, CoverCacheDirName);
        _artistDir = Path.Combine(appData, ArtistCoversDirName);
        _albumDir = Path.Combine(appData, AlbumCoversDirName);

        try
        {
            Directory.CreateDirectory(_coverDir);
            Directory.CreateDirectory(_artistDir);
            Directory.CreateDirectory(_albumDir);
        }
        catch { }

        _ = Task.Run(InitCacheSizeAsync);
    }

    /// <summary>网络封面磁盘缓存目录路径（供 CachingUriImageSourceService 使用）</summary>
    public string CoverCacheDirectory => _coverDir;

    /// <summary>图片缓存上限（MB），超出后按 LRU 淘汰</summary>
    public int CacheSizeLimitMB
    {
        get => Preferences.Default.Get(PrefCacheSizeLimitMB, DefaultCacheSizeLimitMB);
        private set => Preferences.Default.Set(PrefCacheSizeLimitMB,
            Math.Clamp(value, MinCacheSizeLimitMB, MaxCacheSizeLimitMB));
    }

    /// <summary>图片缓存有效期（天），超过该时间未写入的封面会被删除</summary>
    public int CacheAgeDays
    {
        get => Preferences.Default.Get(PrefCacheAgeDays, DefaultCacheAgeDays);
        private set => Preferences.Default.Set(PrefCacheAgeDays,
            Math.Clamp(value, MinCacheAgeDays, MaxCacheAgeDays));
    }

    /// <summary>缓存上限（字节）</summary>
    public long CacheSizeLimitBytes => (long)CacheSizeLimitMB * 1024 * 1024;

    /// <summary>设置缓存上限并触发清理</summary>
    public void SetCacheSizeLimitMB(int mb)
    {
        CacheSizeLimitMB = mb;
        _ = Task.Run(CleanupAsync);
    }

    /// <summary>设置缓存有效期并触发清理</summary>
    public void SetCacheAgeDays(int days)
    {
        CacheAgeDays = days;
        _ = Task.Run(CleanupAsync);
    }

    /// <summary>计算所有图片缓存目录总大小（cover + artist + album）</summary>
    public long GetCacheSizeBytes()
    {
        long size = 0;
        foreach (var dir in new[] { _coverDir, _artistDir, _albumDir })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; }
                catch { }
            }
        }
        return size;
    }

    public async Task<long> GetCacheSizeBytesAsync()
    {
        return await Task.Run(GetCacheSizeBytes);
    }

    /// <summary>清空所有图片缓存（cover + artist + album）</summary>
    public async Task ClearAllAsync()
    {
        await Task.Run(() =>
        {
            foreach (var dir in new[] { _coverDir, _artistDir, _albumDir })
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        try { File.Delete(file); }
                        catch { }
                    }
                }
                catch { }
            }
        });
        Interlocked.Exchange(ref _cacheSizeBytes, 0);
    }

    /// <summary>
    /// 执行自动清理：先按时效删除，再按容量 LRU 淘汰。
    /// 仅处理 cover_url_cache，artist/album_covers 手动清理。
    /// </summary>
    public async Task CleanupAsync()
    {
        if (!await _cleanupLock.WaitAsync(0)) return;
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-CacheAgeDays);
            long size = 0;

            // 1. 时效淘汰
            if (Directory.Exists(_coverDir))
            {
                foreach (var file in Directory.GetFiles(_coverDir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.LastWriteTimeUtc < cutoff)
                        {
                            fi.Delete();
                            continue;
                        }
                        size += fi.Length;
                    }
                    catch { }
                }
            }

            Interlocked.Exchange(ref _cacheSizeBytes, size);

            // 2. 容量上限淘汰
            var limit = CacheSizeLimitBytes;
            if (size <= limit) return;

            var remaining = new List<FileInfo>();
            if (Directory.Exists(_coverDir))
            {
                foreach (var file in Directory.GetFiles(_coverDir, "*", SearchOption.AllDirectories))
                {
                    try { remaining.Add(new FileInfo(file)); }
                    catch { }
                }
            }

            foreach (var fi in remaining.OrderBy(f => f.LastWriteTimeUtc))
            {
                if (size <= (long)(limit * 0.9)) break;
                try
                {
                    var len = fi.Length;
                    fi.Delete();
                    size -= len;
                }
                catch { }
            }

            Interlocked.Exchange(ref _cacheSizeBytes, size);
        }
        finally
        {
            _cleanupLock.Release();
        }
    }

    /// <summary>
    /// 写入新图片后调用：更新内存统计并触发容量检查。
    /// 由 CachingUriImageSourceService 在下载完成后调用。
    /// </summary>
    public void NotifyWritten(long bytesWritten)
    {
        if (bytesWritten > 0)
            Interlocked.Add(ref _cacheSizeBytes, bytesWritten);
        _ = Task.Run(CleanupAsync);
    }

    private async Task InitCacheSizeAsync()
    {
        try
        {
            var size = await GetCacheSizeBytesAsync();
            Interlocked.Exchange(ref _cacheSizeBytes, size);
            await CleanupAsync();
        }
        catch { }
    }
}
