using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Platforms.Android;
using System.Collections.Concurrent;

namespace CatClawMusic.UI.Services;

public class AndroidLocalScanner
{
    public static async Task<List<Song>> ScanAsync(
        List<string>? customFolders = null,
        IProgress<(int done, int total, string status)>? progress = null,
        Func<List<Song>, Task>? songCallback = null)
    {
        var allSongs = new List<Song>();
        var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

            progress?.Report((1, 2, "扫描本地文件..."));
            var scanDirs = new List<string> { "/storage/emulated/0/Music", "/storage/emulated/0/Download" };
            if (customFolders != null)
            {
                foreach (var f in customFolders)
                    if (!string.IsNullOrWhiteSpace(f) && !f.StartsWith("content://") && Directory.Exists(f) && !scanDirs.Contains(f))
                        scanDirs.Add(f);
            }

            var allScanPaths = new List<string>();
            foreach (var dir in scanDirs)
            {
                if (Directory.Exists(dir))
                {
                    try { allScanPaths.AddRange(MusicUtility.ScanFolderRecursive(dir)); }
                    catch { }
                }
            }

            var newPaths = allScanPaths.Where(p => !existingPaths.Contains(p)).ToList();

            if (newPaths.Count > 0)
            {
                var songBag = new ConcurrentBag<Song>();
                var processedCount = 0;
                var totalToProcess = newPaths.Count;

                Parallel.ForEach(newPaths, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4)
                }, path =>
                {
                    try
                    {
                        var song = TagReader.ReadSongInfo(path);
                        if (song != null)
                        {
                            songBag.Add(song);
                            existingPaths.Add(path);
                        }
                    }
                    catch { }

                    var count = Interlocked.Increment(ref processedCount);
                    if (count % 50 == 0 || count == totalToProcess)
                    {
                        progress?.Report((count, totalToProcess, $"正在读取标签 {count}/{totalToProcess}..."));
                    }
                });

                var batch = songBag.ToList();
                lock (locker) allSongs.AddRange(batch);

                if (batch.Count > 0 && songCallback != null)
                {
                    const int batchSize = 30;
                    for (int i = 0; i < batch.Count; i += batchSize)
                    {
                        var chunk = batch.Skip(i).Take(batchSize).ToList();
                        await songCallback(chunk);
                    }
                }
            }
        }
        else
        {
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
