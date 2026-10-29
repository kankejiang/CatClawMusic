using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.WinUI.Infrastructure;

namespace CatClawMusic.WinUI.Services;

/// <summary>
/// 封面抽取（对照 MAUI 的 <c>CoverHelper</c>）：读音频内嵌图 → 落盘到
/// <see cref="AppPaths.CoversDirectory"/> → 把路径回写 Songs 表。
/// 只做本地文件；网络歌曲与封面刮削留到 P3。
/// </summary>
public sealed class CoverService
{
    private readonly IMusicLibraryService _library;
    private readonly Dictionary<int, string?> _cache = new();

    public CoverService(IMusicLibraryService library) => _library = library;

    public async Task<string?> GetCoverPathAsync(Song song)
    {
        if (song.CoverArtPath is { Length: > 0 } existing && File.Exists(existing))
            return existing;

        if (_cache.TryGetValue(song.Id, out var cached))
            return cached;

        var path = await Task.Run(() => Extract(song));
        _cache[song.Id] = path;

        if (path is null)
            return null;

        song.CoverArtPath = path;
        try
        {
            await _library.SaveSongAsync(song);
        }
        catch (Exception ex)
        {
            // 回写失败不影响显示；下次进页面会重新读一次内嵌图。
            Log.Warn("cover", $"封面路径回写失败：{ex.Message}");
        }

        return path;
    }

    private static string? Extract(Song song)
    {
        if (string.IsNullOrWhiteSpace(song.FilePath) || !File.Exists(song.FilePath))
            return null;

        try
        {
            using var file = TagLib.File.Create(song.FilePath);
            var picture = file.Tag.Pictures?.FirstOrDefault();
            if (picture?.Data?.Data is not { Length: > 0 } bytes)
                return null;

            var extension = picture.MimeType?.Contains("png", StringComparison.OrdinalIgnoreCase) == true
                ? ".png"
                : ".jpg";

            var path = Path.Combine(AppPaths.CoversDirectory, $"{song.Id}{extension}");
            File.WriteAllBytes(path, bytes);
            return path;
        }
        catch (Exception ex)
        {
            Log.Debug("cover", $"读取内嵌封面失败：{song.FilePath} — {ex.Message}");
            return null;
        }
    }
}
