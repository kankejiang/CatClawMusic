using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Platforms.Android;

namespace CatClawMusic.UI.Services;

/// <summary>
/// Android 端本地音乐扫描器
/// 放在 UI 项目里是因为 TargetFramework=net9.0-android，#if ANDROID 才生效
/// </summary>
public class AndroidLocalScanner
{
    /// <summary>本地扫描：只扫描用户选择的文件夹</summary>
    /// <param name="customFolders">用户通过 SAF 选择的文件夹 URI 列表</param>
    /// <param name="progress">进度回调 (done, total, status)</param>
    /// <param name="songCallback">每扫描完一个批次后的回调，用于增量更新 UI</param>
    public static async Task<List<Song>> ScanAsync(
        List<string>? customFolders = null,
        IProgress<(int done, int total, string status)>? progress = null,
        Func<List<Song>, Task>? songCallback = null)
    {
        var allSongs = new List<Song>();
        var existingPaths = new HashSet<string>();

        // 判断扫描模式
        bool hasManageStorage = global::Android.OS.Build.Version.SdkInt >= global::Android.OS.BuildVersionCodes.R
            && global::Android.OS.Environment.IsExternalStorageManager;

        // SAF 文件夹是否存在（用户通过文件选择器选的）
        bool hasSafFolders = customFolders != null && customFolders.Count > 0
            && customFolders.Any(f => f.StartsWith("content://", StringComparison.OrdinalIgnoreCase));

        if (hasSafFolders)
        {
            // ── SAF 模式：只扫描用户选择的文件夹 ──
            // 跳过 MediaStore 全设备扫描，避免扫到未选择的文件夹
            progress?.Report((0, 1, "扫描选中的文件夹..."));
            System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF 模式：扫描 {customFolders!.Count} 个用户选择的文件夹");
            try
            {
                var safSongs = await SafeContentScanner.ScanSavedFolderAsync();
                var batch = new List<Song>();
                foreach (var s in safSongs)
                {
                    if (existingPaths.Add(s.FilePath))
                    {
                        batch.Add(s);
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
                System.Diagnostics.Debug.WriteLine($"[CatClaw] MediaStore 返回 {mediaSongs.Count} 首");
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
                System.Diagnostics.Debug.WriteLine($"[CatClaw] MediaStore 返回 {mediaSongs.Count} 首");
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
