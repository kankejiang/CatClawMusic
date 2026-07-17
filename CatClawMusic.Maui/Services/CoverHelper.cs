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

    /// <summary>下采样后的封面最大边长（像素）。UI 最大显示 132x132，2x 余量足够覆盖。</summary>
    private const int MaxCoverSize = 300;

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
    /// </summary>
    /// <param name="songs">待解析封面的歌曲集合</param>
    public static void BatchResolveCovers(IEnumerable<Song> songs)
    {
        foreach (var song in songs)
        {
            if (song.Id <= 0) continue;

            // 跳过已解析过的（同一会话内）
            if (_resolvedSongIds.ContainsKey(song.Id))
            {
                // 确保路径还在
                var cachedPath = GetCachedPath(song.Id);
                if (File.Exists(cachedPath))
                {
                    song.CoverArtPath = cachedPath;
                }
                continue;
            }

            var path = ResolveSingleCover(song);
            if (path != null)
            {
                song.CoverArtPath = path;
            }

            _resolvedSongIds.TryAdd(song.Id, 0);
        }
    }

    /// <summary>
    /// 解析单首歌曲的封面路径。
    /// 优先检查磁盘缓存，然后尝试从音频文件提取嵌入封面。
    /// 提取后会下采样到 MaxCoverSize 以减少 UI 解码开销。
    /// </summary>
    /// <param name="song">待解析封面的歌曲对象</param>
    /// <returns>封面文件路径；无可用封面时返回 null</returns>
    public static string? ResolveSingleCover(Song song)
    {
        if (song.Id <= 0) return null;

        // 1. 检查磁盘缓存（已下采样）
        var cachedPath = GetCachedPath(song.Id);
        if (File.Exists(cachedPath))
            return cachedPath;

        // 2. 检查 CoverArtPath 是否已指向一个有效文件
        if (!string.IsNullOrEmpty(song.CoverArtPath) && File.Exists(song.CoverArtPath))
        {
            // 下采样到缓存路径
            if (DownsampleToCache(song.CoverArtPath, cachedPath))
            {
                TryDeleteSource(song.CoverArtPath);
                return cachedPath;
            }
            return song.CoverArtPath;
        }

        // 3. 从嵌入封面提取
        if (!string.IsNullOrEmpty(song.FilePath)
            && !song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase)
            && File.Exists(song.FilePath))
        {
            try
            {
                var extracted = TagReader.ExtractCoverArtToFile(song.FilePath, _coverCacheDir);
                if (extracted != null)
                {
                    // 下采样到标准缓存路径
                    if (DownsampleToCache(extracted, cachedPath))
                    {
                        TryDeleteSource(extracted);
                        return cachedPath;
                    }
                    // 下采样失败则直接使用原始文件
                    if (extracted != cachedPath && File.Exists(extracted))
                    {
                        try
                        {
                            File.Copy(extracted, cachedPath, overwrite: true);
                            File.Delete(extracted);
                        }
                        catch { /* 复制失败就用原路径 */ }
                        return File.Exists(cachedPath) ? cachedPath : extracted;
                    }
                    return extracted;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("CoverHelper", $"[CoverHelper] Extract cover failed for {song.Title}: {ex.Message}");
            }
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
    private static bool DownsampleToCache(string sourcePath, string destPath)
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
            if (width <= MaxCoverSize && height <= MaxCoverSize)
            {
                if (sourcePath != destPath)
                {
                    File.Copy(sourcePath, destPath, overwrite: true);
                }
                return true;
            }

            // 等比缩放
            var ratio = Math.Min((double)MaxCoverSize / width, (double)MaxCoverSize / height);
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

    /// <summary>获取歌曲封面的标准缓存路径</summary>
    /// <param name="songId">歌曲唯一标识</param>
    /// <returns>封面缓存文件绝对路径</returns>
    public static string GetCachedPath(int songId)
    {
        return Path.Combine(_coverCacheDir, $"cover_{songId}.jpg");
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
    /// 迁移旧版未下采样的缓存封面：检查文件大小，超过阈值的重新下采样。
    /// 应在应用启动时调用一次（后台线程），避免阻塞 UI。
    /// </summary>
    public static Task MigrateLegacyCoversAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(_coverCacheDir)) return;
                // 50KB 以上认为可能是未下采样的原图（300x300 JPEG 约 10-30KB）
                const long threshold = 50 * 1024;
                foreach (var file in Directory.EnumerateFiles(_coverCacheDir, "*.jpg"))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length < threshold) continue;
                        // 原地覆盖下采样
                        var tempPath = file + ".tmp";
                        if (DownsampleToCache(file, tempPath))
                        {
                            File.Copy(tempPath, file, overwrite: true);
                            File.Delete(tempPath);
                        }
                    }
                    catch { /* 忽略单个文件失败 */ }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("CoverHelper", $"[CoverHelper] Migrate legacy covers failed: {ex.Message}");
            }
        });
    }
}
