using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services.AI;
using CatClawMusic.Core.Interfaces;
using SQLite;
using System.IO.Compression;
using System.Text.Json;

namespace CatClawMusic.Data;

/// <summary>备份恢复服务 —— partial 分域文件。</summary>
public partial class BackupService
{
    private static void CreateBackupZip(string zipPath, string jsonPath)
    {
        var coversDir = GetBackupCoversDirectory(jsonPath);
        using var zipStream = System.IO.File.Open(zipPath, FileMode.Create);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        // JSON 以固定名称存入 zip，简化恢复时定位
        archive.CreateEntryFromFile(jsonPath, "backup.json", CompressionLevel.Optimal);

        if (System.IO.Directory.Exists(coversDir))
        {
            foreach (var filePath in System.IO.Directory.GetFiles(coversDir))
            {
                var entryName = $"covers/{System.IO.Path.GetFileName(filePath)}";
                archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);
            }
        }
    }

    /// <summary>
    /// 清理备份过程中产生的中间文件（backup.json 和临时 covers 目录）。
    /// </summary>
    /// <param name="jsonPath">backup.json 文件路径。</param>
    private static void CleanupBackupTempFiles(string jsonPath)
    {
        try
        {
            if (System.IO.File.Exists(jsonPath))
                System.IO.File.Delete(jsonPath);
        }
        catch { /* 清理失败不影响备份结果 */ }

        try
        {
            var coversDir = GetBackupCoversDirectory(jsonPath);
            if (System.IO.Directory.Exists(coversDir))
                System.IO.Directory.Delete(coversDir, recursive: true);
        }
        catch { /* 清理失败不影响备份结果 */ }
    }

    /// <summary>
    /// 从 zip 备份文件中读取 backup.json 并反序列化为 BackupData。
    /// </summary>
    /// <param name="zipPath">zip 备份文件路径。</param>
    /// <returns>反序列化后的备份数据。</returns>
    /// <exception cref="InvalidOperationException">zip 中缺少 backup.json 或格式无效。</exception>
    private static async Task<BackupData> ReadBackupDataFromZipAsync(string zipPath)
    {
        using var zipStream = System.IO.File.OpenRead(zipPath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var jsonEntry = archive.GetEntry("backup.json")
            ?? throw new InvalidOperationException("备份 zip 中未找到 backup.json");

        await using var entryStream = jsonEntry.Open();
        using var reader = new StreamReader(entryStream);
        var json = await reader.ReadToEndAsync();

        return JsonSerializer.Deserialize<BackupData>(json, JsonOptions)
            ?? throw new InvalidOperationException("备份文件格式无效");
    }

    /// <summary>
    /// 从 zip 备份文件恢复艺术家封面到本地缓存目录，并更新数据库中的 Cover 路径。
    /// </summary>
    /// <param name="zipPath">zip 备份文件路径。</param>
    /// <param name="data">备份数据。</param>
    /// <param name="progress">进度回调。</param>
    /// <param name="startPercent">起始进度百分比。</param>
    /// <param name="endPercent">结束进度百分比。</param>
    private async Task RestoreArtistCoversFromZipAsync(
        string zipPath, BackupData data, IProgress<BackupProgress>? progress, int startPercent, int endPercent)
    {
        if (string.IsNullOrEmpty(_artistCoversDir)) return;
        if (data.ArtistCovers.Count == 0) return;

        System.IO.Directory.CreateDirectory(_artistCoversDir);

        var artists = await _db.GetAllArtistsAsync();
        var artistByName = artists.ToDictionary(a => a.Name, a => a, StringComparer.OrdinalIgnoreCase);

        using var zipStream = System.IO.File.OpenRead(zipPath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        int total = data.ArtistCovers.Count;
        int index = 0;
        foreach (var entry in data.ArtistCovers)
        {
            if (!artistByName.TryGetValue(entry.ArtistName, out var artist)) continue;

            var zipEntryName = $"covers/{entry.FileName}";
            var zipEntry = archive.GetEntry(zipEntryName);
            if (zipEntry == null) continue;

            try
            {
                var safeName = SanitizeFileName(artist.Name);
                var destPath = System.IO.Path.Combine(_artistCoversDir, $"{safeName}.jpg");

                await using var entryStream = zipEntry.Open();
                await using var destStream = System.IO.File.Create(destPath);
                await entryStream.CopyToAsync(destStream);

                artist.Cover = destPath;
                await _db.UpdateArtistAsync(artist);
            }
            catch { /* 单张封面恢复失败不影响整体 */ }

            index++;
            var percent = startPercent + (endPercent - startPercent) * index / Math.Max(total, 1);
            Report(progress, percent, $"正在解压艺术家照片 ({index}/{total})...");
        }
    }

    /// <summary>
    /// 加载艺术家封面文件到备份临时目录，返回备份条目列表。
    /// 优先使用数据库中记录的 Cover 路径；若失效，则在缓存目录按艺术家名兜底查找。
    /// </summary>
    /// <param name="backupFilePath">备份文件路径（用于推导临时 covers 目录）。</param>
    /// <param name="data">备份数据。</param>
    /// <param name="progress">进度回调。</param>
    /// <param name="startPercent">起始进度百分比。</param>
    /// <param name="endPercent">结束进度百分比。</param>
    /// <returns>艺术家封面备份条目列表。</returns>
    private async Task<List<ArtistCoverBackupEntry>> LoadArtistCoversAsync(
        string backupFilePath, BackupData data, IProgress<BackupProgress>? progress, int startPercent, int endPercent)
    {
        var entries = new List<ArtistCoverBackupEntry>();
        if (string.IsNullOrEmpty(_artistCoversDir) || !System.IO.Directory.Exists(_artistCoversDir))
        {
            Log.Debug("BackupService", $"[Backup] 艺术家封面缓存目录不存在或为空: {_artistCoversDir}");
            return entries;
        }

        var artists = await _db.GetAllArtistsAsync();
        var cachedFiles = System.IO.Directory.GetFiles(_artistCoversDir);
        Log.Debug("BackupService", $"[Backup] 艺术家总数: {artists.Count}, 缓存目录文件数: {cachedFiles.Length}, 目录: {_artistCoversDir}");

        var backupDir = GetBackupCoversDirectory(backupFilePath);
        System.IO.Directory.CreateDirectory(backupDir);

        // 优先使用数据库中记录的 Cover 路径；若失效，则在缓存目录按艺术家名兜底查找
        var artistsWithCover = artists
            .Select(a => new { Artist = a, CoverPath = ResolveArtistCoverPath(a) })
            .Where(x => x.CoverPath != null)
            .ToList();

        Log.Debug("BackupService", $"[Backup] 找到可备份的艺术家封面数: {artistsWithCover.Count}");
        if (artistsWithCover.Count == 0 && artists.Count > 0)
        {
            var sampleNoCover = artists.Take(5).Select(a => $"{a.Name}(Cover={a.Cover ?? "null"})");
            Log.Debug("BackupService", $"[Backup] 示例无封面艺术家: {string.Join(", ", sampleNoCover)}");
        }

        int total = artistsWithCover.Count;
        int index = 0;
        var artistsToUpdate = new List<Artist>();
        foreach (var item in artistsWithCover)
        {
            var artist = item.Artist;
            var coverPath = item.CoverPath!;
            try
            {
                var safeName = SanitizeFileName(artist.Name);
                var fileName = $"{safeName}.jpg";
                var destPath = System.IO.Path.Combine(backupDir, fileName);

                // 处理同名艺术家文件名冲突：追加序号
                var uniqueName = fileName;
                int nameIndex = 1;
                while (entries.Any(e => e.FileName == uniqueName))
                {
                    uniqueName = $"{safeName}_{nameIndex}.jpg";
                    destPath = System.IO.Path.Combine(backupDir, uniqueName);
                    nameIndex++;
                }

                await CopyFileAsync(coverPath, destPath);
                entries.Add(new ArtistCoverBackupEntry
                {
                    ArtistName = artist.Name,
                    FileName = uniqueName,
                });

                // 若数据库中的路径已失效，但缓存目录兜底找到，则收集后批量更新
                if (artist.Cover != coverPath)
                {
                    artist.Cover = coverPath;
                    artistsToUpdate.Add(artist);
                }
            }
            catch { /* 单张封面备份失败不影响整体 */ }

            index++;
            var percent = startPercent + (endPercent - startPercent) * index / Math.Max(total, 1);
            Report(progress, percent, $"正在复制艺术家照片 ({index}/{total})...");
        }

        // 批量更新数据库，避免逐条 UpdateArtistAsync
        if (artistsToUpdate.Count > 0)
            await _db.UpdateArtistsBatchAsync(artistsToUpdate);

        return entries;
    }

    /// <summary>
    /// 解析艺术家封面实际文件路径：先使用数据库记录，若失效则在 _artistCoversDir 兜底查找。
    /// </summary>
    private string? ResolveArtistCoverPath(Artist artist)
    {
        if (!string.IsNullOrEmpty(artist.Cover))
        {
            var recordedPath = artist.Cover!;
            if (System.IO.File.Exists(recordedPath))
                return recordedPath;

            // 兼容相对路径
            if (!System.IO.Path.IsPathRooted(recordedPath))
            {
                var relativePath = System.IO.Path.Combine(_artistCoversDir, recordedPath);
                if (System.IO.File.Exists(relativePath))
                    return relativePath;
            }
        }

        // 兜底：在缓存目录按艺术家安全文件名匹配常见后缀
        var safeName = SanitizeFileName(artist.Name);
        var candidates = new[]
        {
            $"{safeName}.jpg",
            $"{safeName}_qq.jpg",
            $"{safeName}_netease.jpg",
        };

        foreach (var candidate in candidates)
        {
            var fullPath = System.IO.Path.Combine(_artistCoversDir, candidate);
            if (System.IO.File.Exists(fullPath))
                return fullPath;
        }

        // 兜底2：遍历目录，按文件名前缀匹配（兼容 jpeg/png 等扩展名）
        try
        {
            foreach (var filePath in System.IO.Directory.GetFiles(_artistCoversDir))
            {
                var fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(filePath);
                if (fileNameWithoutExt.Equals(safeName, StringComparison.OrdinalIgnoreCase) ||
                    fileNameWithoutExt.StartsWith(safeName + "_", StringComparison.OrdinalIgnoreCase))
                {
                    return filePath;
                }
            }
        }
        catch { /* 兜底查找失败不影响整体 */ }

        return null;
    }

    /// <summary>
    /// 从旧版 JSON 备份的临时 covers 目录恢复艺术家封面（zip 备份请使用 RestoreArtistCoversFromZipAsync）。
    /// </summary>
    /// <param name="backupFilePath">备份文件路径。</param>
    /// <param name="data">备份数据。</param>
    /// <param name="progress">进度回调。</param>
    /// <param name="startPercent">起始进度百分比。</param>
    /// <param name="endPercent">结束进度百分比。</param>
    private async Task RestoreArtistCoversAsync(
        string backupFilePath, BackupData data, IProgress<BackupProgress>? progress, int startPercent, int endPercent)
    {
        if (string.IsNullOrEmpty(_artistCoversDir)) return;
        if (data.ArtistCovers.Count == 0) return;

        System.IO.Directory.CreateDirectory(_artistCoversDir);

        var artists = await _db.GetAllArtistsAsync();
        var artistByName = artists.ToDictionary(a => a.Name, a => a, StringComparer.OrdinalIgnoreCase);
        var backupDir = GetBackupCoversDirectory(backupFilePath);

        int total = data.ArtistCovers.Count;
        int index = 0;
        foreach (var entry in data.ArtistCovers)
        {
            if (!artistByName.TryGetValue(entry.ArtistName, out var artist)) continue;

            var sourcePath = System.IO.Path.Combine(backupDir, entry.FileName);
            if (!System.IO.File.Exists(sourcePath)) continue;

            try
            {
                var safeName = SanitizeFileName(artist.Name);
                var destPath = System.IO.Path.Combine(_artistCoversDir, $"{safeName}.jpg");
                await CopyFileAsync(sourcePath, destPath);
                artist.Cover = destPath;
                await _db.UpdateArtistAsync(artist);
            }
            catch { /* 单张封面恢复失败不影响整体 */ }

            index++;
            var percent = startPercent + (endPercent - startPercent) * index / Math.Max(total, 1);
            Report(progress, percent, $"正在恢复艺术家照片 ({index}/{total})...");
        }
    }

    /// <summary>
    /// 根据备份文件路径推导临时 covers 目录路径（与备份文件同名 + "_covers" 后缀）。
    /// </summary>
    /// <param name="backupFilePath">备份文件路径。</param>
    /// <returns>临时 covers 目录路径。</returns>
    private static string GetBackupCoversDirectory(string backupFilePath)
        => System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(backupFilePath)!,
            System.IO.Path.GetFileNameWithoutExtension(backupFilePath) + "_covers");

    /// <summary>
    /// 异步复制文件。
    /// </summary>
    /// <param name="sourcePath">源文件路径。</param>
    /// <param name="destPath">目标文件路径。</param>
    private static async Task CopyFileAsync(string sourcePath, string destPath)
    {
        await using var sourceStream = System.IO.File.OpenRead(sourcePath);
        await using var destStream = System.IO.File.Create(destPath);
        await sourceStream.CopyToAsync(destStream);
    }

    /// <summary>
    /// 将艺术家名中的非法文件名字符替换为下划线，空名返回 "unknown"。
    /// </summary>
    /// <param name="name">原始艺术家名。</param>
    /// <returns>安全文件名。</returns>
    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    /// <summary>
    /// 安全报告进度，progress 为 null 时无操作。
    /// </summary>
    /// <param name="progress">进度回调。</param>
    /// <param name="percent">百分比。</param>
    /// <param name="message">状态消息。</param>
    private static void Report(IProgress<BackupProgress>? progress, int percent, string message)
    {
        progress?.Report(new BackupProgress { Percent = percent, Message = message });
    }

    /// <summary>获取数据库连接（用于直接查询 PlaylistSong）</summary>
    private SQLiteAsyncConnection GetDatabaseConnection()
    {
        // 通过反射获取 _database 私有字段
        var field = typeof(MusicDatabase).GetField("_database",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (SQLiteAsyncConnection)field!.GetValue(_db)!;
    }
}
