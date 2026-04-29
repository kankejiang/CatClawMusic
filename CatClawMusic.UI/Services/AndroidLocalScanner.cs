using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Platforms.Android;
using TagLibFile = TagLib.File;

namespace CatClawMusic.UI.Services;

/// <summary>
/// Android 端本地音乐扫描器
/// 放在 UI 项目里是因为 TargetFramework=net9.0-android，#if ANDROID 才生效
/// </summary>
public class AndroidLocalScanner
{
    /// <summary>三路径扫描：MediaStore → 全文件路径 → SAF Content URI</summary>
    public static async Task<List<Song>> ScanAsync(List<string>? customFolders = null)
    {
        var allSongs = new List<Song>();

        // 1. MediaStore 扫描
        try
        {
            var mediaSongs = AndroidMediaScanner.ScanFromMediaStore();
            System.Diagnostics.Debug.WriteLine($"[CatClaw] MediaStore 返回 {mediaSongs.Count} 首");
            allSongs.AddRange(mediaSongs);
        }
        catch { }

        // 2. 判断是否有全文件访问权限
        bool hasManageStorage = global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.R
            && global::Android.OS.Environment.IsExternalStorageManager;

        if (hasManageStorage)
        {
            // 有全文件访问 → 传统文件系统路径扫描
            var scanDirs = new List<string> { "/storage/emulated/0/Music", "/storage/emulated/0/Download" };
            if (customFolders != null)
            {
                foreach (var f in customFolders)
                    if (!string.IsNullOrWhiteSpace(f) && Directory.Exists(f) && !scanDirs.Contains(f))
                        scanDirs.Add(f);
            }
            foreach (var dir in scanDirs)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        var scanPaths = MusicUtility.ScanFolderRecursive(dir);
                        foreach (var path in scanPaths)
                        {
                            if (!allSongs.Any(s => s.FilePath == path))
                            {
                                var song = TagReader.ReadSongInfo(path);
                                if (song != null) allSongs.Add(song);
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        else
        {
            // 3. 无全文件访问 → SAF Content URI 扫描（MIUI 兼容）
            System.Diagnostics.Debug.WriteLine("[CatClaw] 进入 SAF 扫描路径...");
            try
            {
                var safSongs = await SafeContentScanner.ScanSavedFolderAsync();
                foreach (var s in safSongs)
                {
                    if (!allSongs.Any(existing => existing.FilePath == s.FilePath))
                        allSongs.Add(s);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF scan error: {ex.Message}");
            }
        }

        // 预提取封面缓存
        _ = Task.Run(() => ExtractCoversAsync(allSongs));

        return allSongs;
    }

    /// <summary>后台提取歌曲内嵌封面到缓存目录</summary>
    private static async Task ExtractCoversAsync(List<Song> songs)
    {
        var cacheDir = Path.Combine(
            global::Android.App.Application.Context.CacheDir!.AbsolutePath, "covers");
        Directory.CreateDirectory(cacheDir);

        foreach (var song in songs)
        {
            var coverPath = Path.Combine(cacheDir, $"cover_{song.Id}.jpg");
            if (File.Exists(coverPath)) continue; // 已有缓存，跳过

            try
            {
                byte[]? coverBytes = null;

                if (song.FilePath.StartsWith("content://"))
                {
                    var ctx = global::Android.App.Application.Context;
                    using var stream = ctx.ContentResolver!
                        .OpenInputStream(global::Android.Net.Uri.Parse(song.FilePath));
                    if (stream != null)
                    {
                        var abstraction = new CatClawMusic.Core.Services.ReadOnlyFileAbstraction(
                            song.FilePath, stream);
                        using var tagFile = TagLibFile.Create(abstraction);
                        if (tagFile.Tag.Pictures is { Length: > 0 })
                            coverBytes = tagFile.Tag.Pictures[0].Data.Data;
                    }
                }
                else
                {
                    coverBytes = CatClawMusic.Core.Services.TagReader.ExtractCoverArt(song.FilePath);
                }

                if (coverBytes != null)
                    await File.WriteAllBytesAsync(coverPath, coverBytes);
            }
            catch { /* 单首失败不影响整体 */ }
        }

        System.Diagnostics.Debug.WriteLine(
            $"[CatClaw] 封面预提取完成: {songs.Count} 首歌曲");
    }
}
