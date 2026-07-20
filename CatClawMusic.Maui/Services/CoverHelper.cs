using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using System.Collections.Concurrent;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 封面缓存工具：提取嵌入封面并缓存到本地文件。
/// 后续加载直接命中磁盘缓存，无需重新解析音频文件。
/// </summary>
public static class CoverHelper
{
    private static readonly string _coverCacheDir;
    private static readonly ConcurrentDictionary<int, byte> _resolvedSongIds = new();

    /// <summary>封面尺寸分级（最大边长，像素）——按使用场景限制，减少内存与解码开销。</summary>
    public const int NowPlayingSize = 1000;   // 播放页大图
    public const int DiscoverSize = 800;      // 发现页卡片 / 精选大图
    public const int ThumbnailSize = 300;     // 歌单 / 列表 / 缩略图
    private const int DefaultMaxSize = 1000;

    static CoverHelper()
    {
        _coverCacheDir = Path.Combine(FileSystem.CacheDirectory, "covers");
        Directory.CreateDirectory(_coverCacheDir);
    }

    /// <summary>获取封面缓存目录路径</summary>
    public static string CacheDirectory => _coverCacheDir;

    /// <summary>
    /// 批量解析歌曲封面：先查磁盘缓存，未命中则提取嵌入封面。
    /// 直接修改 song.CoverArtPath 为缓存文件路径。
    /// 使用并行处理（最多 8 线程）以充分利用八核 CPU。
    /// </summary>
    /// <param name="songs">待解析封面的歌曲集合</param>
    public static void BatchResolveCovers(IEnumerable<Song> songs, int maxSize = ThumbnailSize)
    {
        var songList = songs as IList<Song> ?? songs.ToList();
        if (songList.Count == 0) return;

        // 单首或少量歌曲直接串行，避免线程调度开销
        if (songList.Count <= 2)
        {
            foreach (var song in songList)
                ResolveOneInline(song, maxSize);
            return;
        }

        // 并行度：上限 8（八核设备满负载解码），封顶避免过多线程争抢；下限 2 避免双核设备过慢。
        // 该解析运行在后台线程（BatchResolveCoversAsync 内 Task.Run），不阻塞 UI 渲染与输入。
        var degree = Math.Min(8, Math.Max(2, Environment.ProcessorCount));
        var options = new ParallelOptions { MaxDegreeOfParallelism = degree };
        Parallel.ForEach(songList, options, s => ResolveOneInline(s, maxSize));
    }

    /// <summary>
    /// 分块异步解析封面，每处理一小批后让出 CPU/主线程，避免一次性并行解码成千上万个
    /// 音频文件内嵌封面导致设备整体卡顿、GC 压力剧增（表现为进入音乐库各页面时主线程被拖垮）。
    /// 用于进入"歌曲/艺术家/专辑"页面时的后台封面填充：列表先以占位图即时渲染，
    /// 封面在后台分批就绪后通过绑定（INotifyPropertyChanged）自动刷新。
    /// </summary>
    /// <param name="songs">待解析封面的歌曲集合</param>
    /// <param name="chunkSize">每批处理的歌曲数</param>
    /// <param name="yieldDelayMs">每批之间的让出间隔（毫秒），给渲染/输入让路</param>
    /// <param name="ct">取消令牌</param>
    public static async Task BatchResolveCoversAsync(IEnumerable<Song> songs, int chunkSize = 64, int yieldDelayMs = 6, CancellationToken ct = default)
    {
        var list = songs as List<Song> ?? songs.ToList();
        if (list.Count == 0) return;

        for (int i = 0; i < list.Count; i += chunkSize)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = i + chunkSize >= list.Count
                ? list.Skip(i).ToList()
                : list.GetRange(i, chunkSize);
            await Task.Run(() => BatchResolveCovers(chunk), ct);
            if (yieldDelayMs > 0)
                await Task.Delay(yieldDelayMs, ct);
        }
    }

    /// <summary>单首歌曲封面解析的内联方法（线程安全，无共享状态）</summary>
    private static void ResolveOneInline(Song song, int maxSize = ThumbnailSize)
    {
        if (song.Id <= 0) return;

        // 跳过已解析过的（同一会话内）
        if (_resolvedSongIds.ContainsKey(song.Id))
        {
            var cachedPath = GetCachedPath(song.Id, maxSize);
            if (File.Exists(cachedPath))
                song.CoverArtPath = cachedPath;
            return;
        }

        var path = ResolveSingleCover(song, maxSize);
        if (path != null)
            song.CoverArtPath = path;

        _resolvedSongIds.TryAdd(song.Id, 0);
    }

    /// <summary>
    /// 解析单首歌曲的封面路径。
    /// 优先检查磁盘缓存（已下采样），然后尝试从音频文件提取嵌入封面。
    /// 提取后会下采样到 MaxCoverSize 以减少 UI 解码开销。
    /// </summary>
    /// <param name="song">待解析封面的歌曲对象</param>
    /// <returns>封面文件路径；无可用封面时返回 null</returns>
    public static string? ResolveSingleCover(Song song, int maxSize = DefaultMaxSize)
    {
        if (song.Id <= 0) return null;

        // 1. 命中尺寸分桶缓存
        var cachedPath = GetCachedPath(song.Id, maxSize);
        if (File.Exists(cachedPath))
            return cachedPath;

        // 2. 选择可用源：优先使用 >= maxSize 的已有文件，否则从音频文件重新提取全分辨率
        string? source = null;
        if (!string.IsNullOrEmpty(song.CoverArtPath) && File.Exists(song.CoverArtPath)
            && MaxDimension(song.CoverArtPath) >= maxSize)
        {
            source = song.CoverArtPath;
        }

        if (source == null
            && !string.IsNullOrEmpty(song.FilePath)
            && !song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase)
            && !song.FilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !song.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !song.FilePath.StartsWith("smb://", StringComparison.OrdinalIgnoreCase)
            && File.Exists(song.FilePath))
        {
            try
            {
                source = TagReader.ExtractCoverArtToFile(song.FilePath, _coverCacheDir);
            }
            catch (Exception ex)
            {
                Log.Debug("CoverHelper", $"[CoverHelper] Extract cover failed for {song.Title}: {ex.Message}");
            }
        }

        // 3. 兜底：退而求其次使用任意已有的封面文件（可能小于 maxSize，但不至于无图）
        if (source == null && !string.IsNullOrEmpty(song.CoverArtPath) && File.Exists(song.CoverArtPath))
            source = song.CoverArtPath;

        if (source != null)
        {
            if (DownsampleToCache(source, cachedPath, maxSize))
            {
                // 仅清理临时提取文件；song.CoverArtPath 是列表/歌单缩略图，不能删
                if (source != song.CoverArtPath && source != cachedPath)
                    TryDeleteSource(source);
                return cachedPath;
            }
            // 下采样失败则直接使用源文件
            if (source != cachedPath && File.Exists(source))
            {
                try { File.Copy(source, cachedPath, overwrite: true); } catch { }
                if (source != song.CoverArtPath) TryDeleteSource(source);
                return File.Exists(cachedPath) ? cachedPath : source;
            }
            return source;
        }

        return null;
    }

    /// <summary>
    /// 将源图片下采样到 MaxCoverSize 并保存到目标路径。
    /// 使用 Microsoft.Maui.Graphics 跨平台 API，避免主线程同步解码大图。
    /// </summary>
    /// <param name="sourcePath">原始图片路径</param>
    /// <param name="destPath">目标缓存路径</param>
    /// <returns>下采样成功返回 true；失败返回 false</returns>
    public static bool DownsampleToCache(string sourcePath, string destPath, int maxSize = DefaultMaxSize)
    {
        try
        {
            if (!File.Exists(sourcePath)) return false;

            using var srcStream = File.OpenRead(sourcePath);
            using var image = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(srcStream);
            if (image == null) return false;

            var width = (int)image.Width;
            var height = (int)image.Height;
            if (width <= 0 || height <= 0) return false;

            // 已足够小，无需下采样
            if (width <= maxSize && height <= maxSize)
            {
                if (sourcePath != destPath)
                {
                    File.Copy(sourcePath, destPath, overwrite: true);
                }
                return true;
            }

            // 等比缩放
            var ratio = Math.Min((double)maxSize / width, (double)maxSize / height);
            var newWidth = (int)(width * ratio);
            var newHeight = (int)(height * ratio);

            using var downsized = image.Downsize(newWidth, newHeight);
            using var destStream = File.Create(destPath);
            downsized.Save(destStream);
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug("CoverHelper", $"[CoverHelper] Downsample failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>安全删除源文件（忽略失败）</summary>
    private static void TryDeleteSource(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                File.Delete(path);
        }
        catch { /* 忽略删除失败 */ }
    }

    /// <summary>快速读取图片最大边长（像素），用于判断是否需要重新提取更高分辨率源。</summary>
    private static int MaxDimension(string path)
    {
        try
        {
            using var s = File.OpenRead(path);
            using var img = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(s);
            if (img == null) return 0;
            return (int)Math.Max(img.Width, img.Height);
        }
        catch { return 0; }
    }

    /// <summary>将原始封面字节下采样并保存到 outputPath（用于 SAF 扫描期写入封面）。</summary>
    public static string? SaveCoverBytes(byte[] art, string outputPath, int maxSize = ThumbnailSize)
    {
        try
        {
            var tmp = outputPath + ".tmp";
            File.WriteAllBytes(tmp, art);
            if (DownsampleToCache(tmp, outputPath, maxSize))
            {
                TryDeleteSource(tmp);
                return outputPath;
            }
            if (File.Exists(tmp))
            {
                File.Move(tmp, outputPath, overwrite: true);
                return outputPath;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("CoverHelper", $"[CoverHelper] SaveCoverBytes failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>获取歌曲封面的标准缓存路径</summary>
    /// <param name="songId">歌曲唯一标识</param>
    /// <returns>封面缓存文件绝对路径</returns>
    /// <summary>获取歌曲封面的标准缓存路径（按尺寸分桶，避免不同尺寸互相覆盖）。</summary>
    public static string GetCachedPath(int songId, int maxSize = DefaultMaxSize)
    {
        return Path.Combine(_coverCacheDir, $"cover_{songId}_{maxSize}.jpg");
    }

    /// <summary>
    /// 清空已解析歌曲ID的内存缓存。
    /// 当音乐库刷新或需要释放内存时调用，防止 _resolvedSongIds 无限增长。
    /// 调用后下次访问歌曲时会重新检查磁盘缓存或重新提取封面。
    /// </summary>
    public static void ClearCache()
    {
        _resolvedSongIds.Clear();
    }

    /// <summary>
        /// 迁移旧版缓存封面：不做任何操作，旧缓存继续使用。
        /// 新提取的封面按尺寸分桶（播放页 1000 / 发现页 800 / 缩略图 300）。
    /// </summary>
    public static Task MigrateLegacyCoversAsync()
    {
        return Task.CompletedTask;
    }
}
