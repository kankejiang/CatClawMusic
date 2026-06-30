using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 封面缓存工具：提取嵌入封面并缓存到本地文件。
/// 后续加载直接命中磁盘缓存，无需重新解析音频文件。
/// </summary>
public static class CoverHelper
{
    private static readonly string _coverCacheDir;
    private static readonly HashSet<int> _resolvedSongIds = new();

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
    public static void BatchResolveCovers(IEnumerable<Song> songs)
    {
        foreach (var song in songs)
        {
            if (song.Id <= 0) continue;

            // 跳过已解析过的（同一会话内）
            if (_resolvedSongIds.Contains(song.Id))
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

            _resolvedSongIds.Add(song.Id);
        }
    }

    /// <summary>
    /// 解析单首歌曲的封面路径。
    /// 优先检查磁盘缓存，然后尝试从音频文件提取嵌入封面。
    /// </summary>
    public static string? ResolveSingleCover(Song song)
    {
        if (song.Id <= 0) return null;

        // 1. 检查磁盘缓存
        var cachedPath = GetCachedPath(song.Id);
        if (File.Exists(cachedPath))
            return cachedPath;

        // 2. 检查 CoverArtPath 是否已指向一个有效文件
        if (!string.IsNullOrEmpty(song.CoverArtPath) && File.Exists(song.CoverArtPath))
            return song.CoverArtPath;

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
                    // TagReader.ExtractCoverArtToFile 输出的文件名格式可能与缓存不同
                    // 复制到标准缓存路径
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
                System.Diagnostics.Debug.WriteLine($"[CoverHelper] Extract cover failed for {song.Title}: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>获取歌曲封面的标准缓存路径</summary>
    public static string GetCachedPath(int songId)
    {
        return Path.Combine(_coverCacheDir, $"cover_{songId}.jpg");
    }
}
