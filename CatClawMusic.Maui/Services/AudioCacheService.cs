using System.Collections.Concurrent;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 网络音乐本地缓存服务：将 SMB/WebDAV 音频文件下载到本地，支持 LRU 淘汰和缓存大小管理。
/// </summary>
public class AudioCacheService
{
    private static readonly Lazy<AudioCacheService> _lazy = new(() => new AudioCacheService());
    public static AudioCacheService Instance => _lazy.Value;

    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, string> _urlToPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<string?>> _pendingDownloads = new(StringComparer.OrdinalIgnoreCase);
    private long _cacheSizeBytes;
    private readonly SemaphoreSlim _evictLock = new(1, 1);

    /// <summary>默认缓存大小上限（MB），可通过设置修改</summary>
    public const int DefaultCacheSizeMB = 500;

    /// <summary>预缓冲触发时间（秒），距歌曲结束多少秒开始缓冲下一首</summary>
    public const int PreBufferSeconds = 10;

    private AudioCacheService()
    {
        _cacheDir = Path.Combine(FileSystem.CacheDirectory, "music_cache");
        Directory.CreateDirectory(_cacheDir);
        _ = Task.Run(InitCacheSizeAsync);
    }

    /// <summary>获取当前缓存大小（字节）</summary>
    public long CacheSizeBytes => _cacheSizeBytes;

    /// <summary>获取缓存大小上限（字节），从 Preferences 读取</summary>
    public long CacheSizeLimitBytes
    {
        get
        {
            var mb = Preferences.Default.Get("audio_cache_size_mb", DefaultCacheSizeMB);
            return (long)mb * 1024 * 1024;
        }
    }

    /// <summary>设置缓存大小上限（MB）</summary>
    public void SetCacheSizeLimitMB(int mb)
    {
        Preferences.Default.Set("audio_cache_size_mb", Math.Clamp(mb, 100, 15000));
        _ = Task.Run(EvictIfNeededAsync);
    }

    /// <summary>
    /// 获取音频文件的本地缓存路径。如果已缓存则直接返回，否则返回 null。
    /// </summary>
    public string? GetCachedPath(string remoteUrl)
    {
        if (string.IsNullOrEmpty(remoteUrl)) return null;
        if (_urlToPath.TryGetValue(remoteUrl, out var path) && File.Exists(path))
        {
            // 更新访问时间（LRU 依据）
            try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); } catch { }
            return path;
        }
        return null;
    }

    /// <summary>
    /// 判断指定 URL 是否已缓存到本地。
    /// </summary>
    public bool IsCached(string remoteUrl) => GetCachedPath(remoteUrl) != null;

    /// <summary>
    /// 将远程音频文件下载到本地缓存。如果已在缓存中则直接返回路径。
    /// 如果同一 URL 正在下载中，会等待已有的下载完成。
    /// </summary>
    /// <param name="remoteUrl">远程音频 URL（smb://、http:// 等）</param>
    /// <param name="downloadFunc">下载委托：接收 URL，返回包含音频数据的 byte[]</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>本地缓存文件路径，失败返回 null</returns>
    public async Task<string?> CacheAsync(string remoteUrl, Func<string, Task<byte[]?>> downloadFunc, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(remoteUrl)) return null;

        // 已缓存
        var cached = GetCachedPath(remoteUrl);
        if (cached != null) return cached;

        // 正在下载中，等待
        if (_pendingDownloads.TryGetValue(remoteUrl, out var pendingTask))
            return await pendingTask.ConfigureAwait(false);

        // 开始下载
        var tcs = new TaskCompletionSource<string?>();
        _pendingDownloads[remoteUrl] = tcs.Task;

        try
        {
            var data = await Task.Run(() => downloadFunc(remoteUrl), ct).ConfigureAwait(false);
            if (data == null || data.Length == 0)
            {
                tcs.TrySetResult(null);
                return null;
            }

            var fileName = GetCacheFileName(remoteUrl);
            var filePath = Path.Combine(_cacheDir, fileName);

            await File.WriteAllBytesAsync(filePath, data, ct).ConfigureAwait(false);
            Interlocked.Add(ref _cacheSizeBytes, data.Length);

            _urlToPath[remoteUrl] = filePath;
            tcs.TrySetResult(filePath);

            // 后台淘汰
            _ = Task.Run(EvictIfNeededAsync);

            return filePath;
        }
        catch (Exception ex)
        {
            Log.Debug("AudioCacheService", $"[AudioCache] 缓存失败: {remoteUrl[..Math.Min(60, remoteUrl.Length)]}, {ex.Message}");
            tcs.TrySetResult(null);
            return null;
        }
        finally
        {
            _pendingDownloads.TryRemove(remoteUrl, out _);
        }
    }

    /// <summary>
    /// 如果缓存超过上限，按 LRU（最后访问时间最早）淘汰旧文件。
    /// </summary>
    public async Task EvictIfNeededAsync()
    {
        if (!await _evictLock.WaitAsync(0)) return;
        try
        {
            var limit = CacheSizeLimitBytes;
            if (_cacheSizeBytes <= limit) return;

            var files = Directory.GetFiles(_cacheDir)
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.LastAccessTimeUtc)
                .ToList();

            foreach (var file in files)
            {
                if (_cacheSizeBytes <= limit * 0.9) break; // 淘汰到 90% 容量

                try
                {
                    var size = file.Length;
                    file.Delete();
                    Interlocked.Add(ref _cacheSizeBytes, -size);

                    // 清理 URL 映射
                    var toRemove = _urlToPath.FirstOrDefault(kv => kv.Value == file.FullName);
                    if (toRemove.Key != null)
                        _urlToPath.TryRemove(toRemove.Key, out _);
                }
                catch { }
            }
        }
        finally
        {
            _evictLock.Release();
        }
    }

    /// <summary>
    /// 清空所有缓存。
    /// </summary>
    public async Task ClearAllAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(_cacheDir))
                {
                    Directory.Delete(_cacheDir, true);
                    Directory.CreateDirectory(_cacheDir);
                }
            });
            _urlToPath.Clear();
            Interlocked.Exchange(ref _cacheSizeBytes, 0);
        }
        catch (Exception ex)
        {
            Log.Debug("AudioCacheService", $"[AudioCache] 清除缓存失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取缓存目录路径（用于设置页显示大小）。
    /// </summary>
    public string CacheDirectory => _cacheDir;

    /// <summary>初始化时计算当前缓存大小</summary>
    private async Task InitCacheSizeAsync()
    {
        try
        {
            long size = 0;
            foreach (var file in Directory.GetFiles(_cacheDir))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
            Interlocked.Exchange(ref _cacheSizeBytes, size);

            // 启动时检查是否需要淘汰
            await EvictIfNeededAsync();
        }
        catch { }
    }

    /// <summary>根据 URL 生成缓存文件名（SHA256 哈希 + 原始扩展名）</summary>
    private static string GetCacheFileName(string url)
    {
        var ext = Path.GetExtension(url);
        if (string.IsNullOrEmpty(ext) || ext.Length > 10) ext = ".bin";

        // 用 URL 的哈希作为文件名，避免特殊字符
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url));
        var hashStr = Convert.ToHexString(hash)[..16]; // 取前 16 字符足够
        return $"{hashStr}{ext}";
    }
}
