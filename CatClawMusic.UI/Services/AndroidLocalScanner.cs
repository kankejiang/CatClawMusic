using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Platforms.Android;

namespace CatClawMusic.UI.Services;

public class AndroidLocalScanner
{
    public static async Task<List<Song>> ScanAsync(
        List<string>? customFolders = null,
        IProgress<(int done, int total, string status)>? progress = null,
        Func<List<Song>, Task>? songCallback = null)
    {
        var allSongs = new List<Song>();
        var existingPaths = new HashSet<string>();
        var locker = new object();

        bool hasManageStorage = global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.R
            && global::Android.OS.Environment.IsExternalStorageManager;

        bool hasSafFolders = customFolders != null && customFolders.Count > 0
            && customFolders.Any(f => f.StartsWith("content://", StringComparison.OrdinalIgnoreCase));

        if (hasSafFolders)
        {
            progress?.Report((0, 1, "扫描选中的文件夹..."));
            try
            {
                await SafeContentScanner.ScanSavedFolderAsync(async (songs) =>
                {
                    var newSongs = songs.Where(s => existingPaths.Add(s.FilePath)).ToList();
                    if (newSongs.Count == 0) return;
                    lock (locker) allSongs.AddRange(newSongs);
                    if (songCallback != null)
                        await songCallback(newSongs);
                }, progress);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF scan error: {ex.Message}");
            }
        }
        else if (hasManageStorage)
        {
            // ── 全文件访问模式：MediaStore + 文件系统扫描 ──
            progress?.Report((0, 2, "扫描系统媒体库..."));
            try
            {
                var mediaSongs = AndroidMediaScanner.ScanFromMediaStore();
                foreach (var s in mediaSongs)
                {
                    if (existingPaths.Add(s.FilePath))
                        allSongs.Add(s);
                }
            }
            catch { }

            if (allSongs.Count > 0 && songCallback != null)
                await songCallback(allSongs.ToList());

            // 文件系统补充扫描
            progress?.Report((1, 2, "扫描本地文件..."));
            var scanDirs = new List<string> { "/storage/emulated/0/Music", "/storage/emulated/0/Download" };
            if (customFolders != null)
            {
                foreach (var f in customFolders)
                    if (!string.IsNullOrWhiteSpace(f) && !f.StartsWith("content://") && Directory.Exists(f) && !scanDirs.Contains(f))
                        scanDirs.Add(f);
            }

            foreach (var dir in scanDirs)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        var scanPaths = MusicUtility.ScanFolderRecursive(dir);
                        var batch = new List<Song>();
                        foreach (var path in scanPaths)
                        {
                            if (existingPaths.Contains(path)) continue;
                            var song = TagReader.ReadSongInfo(path);
                            if (song != null)
                            {
                                existingPaths.Add(path);
                                batch.Add(song);
                                if (batch.Count >= 20 && songCallback != null)
                                {
                                    allSongs.AddRange(batch);
                                    await songCallback(batch);
                                    batch = new List<Song>();
                                }
                            }
                        }
                        if (batch.Count > 0)
                        {
                            allSongs.AddRange(batch);
                            if (songCallback != null)
                                await songCallback(batch);
                        }
                    }
                    catch { }
                }
            }
        }
        else
        {
            // ── 无权限也无 SAF：尝试 MediaStore（只读） ──
            progress?.Report((0, 1, "扫描系统媒体库..."));
            try
            {
                var mediaSongs = AndroidMediaScanner.ScanFromMediaStore();
                foreach (var s in mediaSongs)
                {
                    if (existingPaths.Add(s.FilePath))
                        allSongs.Add(s);
                }
            }
            catch { }

            if (allSongs.Count > 0 && songCallback != null)
                await songCallback(allSongs.ToList());
        }

        progress?.Report((1, 1, $"扫描完成，共 {allSongs.Count} 首"));
        return allSongs;
    }
}
