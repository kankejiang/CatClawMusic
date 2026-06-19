using System.IO.Compression;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services.AI;
using SQLite;

namespace CatClawMusic.Data;

/// <summary>备份数据结构</summary>
public class BackupData
{
    public string Version { get; set; } = "1.0";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<Playlist> Playlists { get; set; } = new();
    public List<PlaylistSongBackupEntry> PlaylistSongs { get; set; } = new();
    public List<PlayHistoryBackupEntry> PlayHistory { get; set; } = new();
    public List<FavoriteBackupEntry> Favorites { get; set; } = new();
    public List<ArtistBackupEntry> Artists { get; set; } = new();
    public List<ArtistCoverBackupEntry> ArtistCovers { get; set; } = new();
    public List<LlmConfig> LlmConfigs { get; set; } = new();
    public string? CurrentConfigName { get; set; }
    public string? CurrentAgentId { get; set; }
}


/// <summary>备份选项，控制包含哪些数据类别</summary>
[Flags]
public enum BackupItems
{
    None           = 0,
    Playlists      = 1 << 0,  // 歌单 + 歌单歌曲
    PlayHistory    = 1 << 1,  // 播放记录
    Favorites      = 1 << 2,  // 收藏
    Artists        = 1 << 3,  // 艺术家元数据
    LlmConfigs     = 1 << 4,  // AI模型配置
    ArtistCovers   = 1 << 5,  // 艺术家照片
    All            = Playlists | PlayHistory | Favorites | Artists | LlmConfigs | ArtistCovers,
}

/// <summary>备份/恢复进度信息</summary>
public class BackupProgress
{
    public int Percent { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>艺术家备份条目（仅保存元数据，不保存本地封面文件路径）</summary>
public class ArtistBackupEntry
{
    public string Name { get; set; } = "";
    public string? Gender { get; set; }
    public string? Birthday { get; set; }
    public string? Region { get; set; }
    public string? Description { get; set; }
}

/// <summary>艺术家封面备份条目（保存文件名，实际图片存于 backup_xxx_covers 目录）</summary>
public class ArtistCoverBackupEntry
{
    public string ArtistName { get; set; } = "";
    public string FileName { get; set; } = "";
}

/// <summary>歌单歌曲备份条目（包含歌曲标题和艺术家，用于跨设备恢复）</summary>
public class PlaylistSongBackupEntry
{
    public int PlaylistId { get; set; }
    public int SongId { get; set; }
    public string? SongTitle { get; set; }
    public string? SongArtist { get; set; }
    public int Position { get; set; }
}

/// <summary>播放记录备份条目（包含歌曲标题和艺术家，用于跨设备恢复）</summary>
public class PlayHistoryBackupEntry
{
    public int SongId { get; set; }
    public string? SongTitle { get; set; }
    public string? SongArtist { get; set; }
    public int PlayCount { get; set; }
    public long PlayedAt { get; set; }
}

/// <summary>收藏备份条目（包含歌曲标题和艺术家，用于跨设备恢复）</summary>
public class FavoriteBackupEntry
{
    public int SongId { get; set; }
    public string? SongTitle { get; set; }
    public string? SongArtist { get; set; }
    public long AddedAt { get; set; }
}

/// <summary>备份与恢复服务</summary>
public class BackupService
{
    private readonly MusicDatabase _db;
    private readonly IAgentConfigStorage _configStorage;
    private readonly string _artistCoversDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public BackupService(MusicDatabase db, IAgentConfigStorage configStorage, string artistCoversDir)
    {
        _db = db;
        _configStorage = configStorage;
        _artistCoversDir = artistCoversDir;
    }

    /// <summary>执行备份，将数据写入指定目录下的 CatClawMusic 文件夹</summary>
    /// <param name="externalStoragePath">外部存储根目录（如 /storage/emulated/0）</param>
    /// <param name="items">要备份的数据类别</param>
    /// <param name="progress">进度回调</param>
    /// <returns>备份文件路径</returns>
    public async Task<string> BackupAsync(string externalStoragePath, BackupItems items = BackupItems.All, IProgress<BackupProgress>? progress = null)
    {
        await _db.EnsureInitializedAsync();
        Report(progress, 0, "准备备份...");

        var data = new BackupData();

        if (items.HasFlag(BackupItems.Playlists))
        {
            data.Playlists = await _db.GetAllPlaylistsAsync();
            data.PlaylistSongs = await LoadAllPlaylistSongsWithInfoAsync();
        }
        if (items.HasFlag(BackupItems.PlayHistory))
            data.PlayHistory = await LoadPlayHistoryWithInfoAsync();
        if (items.HasFlag(BackupItems.Favorites))
            data.Favorites = await LoadFavoritesWithInfoAsync();
        Report(progress, 15, "正在读取基础数据...");

        var dir = System.IO.Path.Combine(externalStoragePath, "CatClawMusic");
        System.IO.Directory.CreateDirectory(dir);

        var baseName = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}";
        var jsonPath = System.IO.Path.Combine(dir, $"{baseName}.json");
        var zipPath = System.IO.Path.Combine(dir, $"{baseName}.zip");

        if (items.HasFlag(BackupItems.Artists))
        {
            data.Artists = await LoadArtistMetadataAsync();
            Report(progress, 25, "正在读取艺术家元数据...");
        }
        if (items.HasFlag(BackupItems.ArtistCovers))
        {
            data.ArtistCovers = await LoadArtistCoversAsync(jsonPath, data, progress, 30, 60);
            Report(progress, 60, "正在整理备份数据...");
        }
        else
        {
            Report(progress, 50, "正在整理备份数据...");
        }
        if (items.HasFlag(BackupItems.LlmConfigs))
        {
            data.LlmConfigs = AgentService.LoadAllConfigs();
            data.CurrentConfigName = AgentService.GetCurrentConfigName();
            data.CurrentAgentId = AgentService.LoadCurrentAgentId();
        }

        var json = JsonSerializer.Serialize(data, JsonOptions);
        await System.IO.File.WriteAllTextAsync(jsonPath, json);
        Report(progress, 75, "正在生成备份文件...");

        // 打包为单一 zip 文件（JSON + covers），完成后删除中间文件
        CreateBackupZip(zipPath, jsonPath);
        CleanupBackupTempFiles(jsonPath);
        Report(progress, 100, "备份完成");

        return zipPath;
    }

    /// <summary>从备份文件恢复数据</summary>
    /// <param name="filePath">备份文件路径</param>
    /// <param name="items">要恢复的数据类别</param>
    /// <param name="progress">进度回调</param>
    public async Task RestoreAsync(string filePath, BackupItems items = BackupItems.All, IProgress<BackupProgress>? progress = null)
    {
        await _db.EnsureInitializedAsync();
        Report(progress, 0, "准备恢复...");

        var isZip = System.IO.Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase);
        var data = isZip
            ? await ReadBackupDataFromZipAsync(filePath)
            : JsonSerializer.Deserialize<BackupData>(await System.IO.File.ReadAllTextAsync(filePath), JsonOptions)
                ?? throw new InvalidOperationException("备份文件格式无效");
        Report(progress, 15, "正在读取备份信息...");

        if (items.HasFlag(BackupItems.Playlists))
        {
            await RestorePlaylistsAsync(data);
            Report(progress, 35, "正在恢复歌单...");
        }

        if (items.HasFlag(BackupItems.PlayHistory))
        {
            await RestorePlayHistoryAsync(data);
            Report(progress, 50, "正在恢复播放记录...");
        }

        if (items.HasFlag(BackupItems.Favorites))
        {
            await RestoreFavoritesAsync(data);
            Report(progress, 60, "正在恢复收藏...");
        }

        if (items.HasFlag(BackupItems.Artists))
        {
            await RestoreArtistMetadataAsync(data);
            Report(progress, 70, "正在恢复艺术家元数据...");
        }

        if (items.HasFlag(BackupItems.ArtistCovers))
        {
            if (isZip)
                await RestoreArtistCoversFromZipAsync(filePath, data, progress, 75, 95);
            else
                await RestoreArtistCoversAsync(filePath, data, progress, 75, 95);
            Report(progress, 95, "正在完成恢复...");
        }
        else if (items.HasFlag(BackupItems.Artists))
        {
            Report(progress, 85, "正在完成恢复...");
        }

        if (items.HasFlag(BackupItems.LlmConfigs))
            RestoreLlmConfigs(data);

        Report(progress, 100, "恢复完成");
    }

    /// <summary>获取备份目录路径</summary>
    public static string GetBackupDirectory(string externalStoragePath)
        => System.IO.Path.Combine(externalStoragePath, "CatClawMusic");

    /// <summary>列出备份目录中所有备份文件（支持 .zip 和旧版 .json）</summary>
    public static List<string> ListBackups(string externalStoragePath)
    {
        var dir = GetBackupDirectory(externalStoragePath);
        if (!System.IO.Directory.Exists(dir)) return new List<string>();
        return System.IO.Directory.GetFiles(dir)
            .Where(f =>
            {
                var name = System.IO.Path.GetFileName(f);
                return name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase) &&
                       (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            })
            .OrderByDescending(f => f)
            .ToList();
    }

    /// <summary>从备份文件读取简要信息（支持 .zip 和旧版 .json）</summary>
    public static async Task<BackupData?> ReadBackupInfoAsync(string filePath)
    {
        try
        {
            var isZip = System.IO.Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase);
            if (isZip)
                return await ReadBackupDataFromZipAsync(filePath);

            var json = await System.IO.File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<BackupData>(json, JsonOptions);
        }
        catch { return null; }
    }

    private async Task<List<PlaylistSongBackupEntry>> LoadAllPlaylistSongsWithInfoAsync()
    {
        var playlists = await _db.GetAllPlaylistsAsync();
        var allSongs = await _db.GetSongsAsync();
        var songMap = allSongs.ToDictionary(s => s.Id, s => s);
        var allEntries = new List<PlaylistSongBackupEntry>();

        foreach (var pl in playlists)
        {
            try
            {
                var dbConn = GetDatabaseConnection();
                var songs = await dbConn.Table<PlaylistSong>()
                    .Where(ps => ps.PlaylistId == pl.Id)
                    .OrderBy(ps => ps.Position)
                    .ToListAsync();

                foreach (var ps in songs)
                {
                    songMap.TryGetValue(ps.SongId, out var song);
                    allEntries.Add(new PlaylistSongBackupEntry
                    {
                        PlaylistId = ps.PlaylistId,
                        SongId = ps.SongId,
                        SongTitle = song?.Title,
                        SongArtist = song?.Artist,
                        Position = ps.Position,
                    });
                }
            }
            catch { }
        }
        return allEntries;
    }

    private async Task<List<PlayHistoryBackupEntry>> LoadPlayHistoryWithInfoAsync()
    {
        var history = await _db.GetRecentPlaysAsync(200);
        var allSongs = await _db.GetSongsAsync();
        var songMap = allSongs.ToDictionary(s => s.Id, s => s);

        return history.Select(h =>
        {
            songMap.TryGetValue(h.SongId, out var song);
            return new PlayHistoryBackupEntry
            {
                SongId = h.SongId,
                SongTitle = song?.Title,
                SongArtist = song?.Artist,
                PlayCount = h.PlayCount,
                PlayedAt = h.PlayedAt,
            };
        }).ToList();
    }

    private async Task<List<FavoriteBackupEntry>> LoadFavoritesWithInfoAsync()
    {
        var favs = await _db.GetFavoritesAsync();
        var allSongs = await _db.GetSongsAsync();
        var songMap = allSongs.ToDictionary(s => s.Id, s => s);

        return favs.Select(f =>
        {
            songMap.TryGetValue(f.SongId, out var song);
            return new FavoriteBackupEntry
            {
                SongId = f.SongId,
                SongTitle = song?.Title,
                SongArtist = song?.Artist,
                AddedAt = f.AddedAt,
            };
        }).ToList();
    }

    private async Task<List<ArtistBackupEntry>> LoadArtistMetadataAsync()
    {
        var artists = await _db.GetAllArtistsAsync();
        return artists.Where(a =>
            !string.IsNullOrEmpty(a.Gender) ||
            !string.IsNullOrEmpty(a.Birthday) ||
            !string.IsNullOrEmpty(a.Region) ||
            !string.IsNullOrEmpty(a.Description))
            .Select(a => new ArtistBackupEntry
            {
                Name = a.Name,
                Gender = a.Gender,
                Birthday = a.Birthday,
                Region = a.Region,
                Description = a.Description,
            })
            .ToList();
    }

    private async Task RestorePlaylistsAsync(BackupData data)
    {
        // 获取当前歌单名称集合，避免重复
        var existing = await _db.GetAllPlaylistsAsync();
        var existingNames = existing.Select(p => p.Name).ToHashSet();

        // 歌单名称 → 新ID 映射
        var oldIdToNewId = new Dictionary<int, int>();

        foreach (var pl in data.Playlists)
        {
            if (existingNames.Contains(pl.Name)) continue;
            var newId = await _db.CreatePlaylistAsync(pl.Name);
            oldIdToNewId[pl.Id] = newId;
        }

        // 构建本地歌曲 Title+Artist → SongId 映射
        var allSongs = await _db.GetSongsAsync();
        var songKeyMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in allSongs)
        {
            var key = $"{(s.Title?.Trim() ?? "")}|{(s.Artist?.Trim() ?? "")}";
            if (!songKeyMap.ContainsKey(key))
                songKeyMap[key] = s.Id;
        }

        // 恢复歌单中的歌曲关联
        foreach (var ps in data.PlaylistSongs)
        {
            if (!oldIdToNewId.TryGetValue(ps.PlaylistId, out var newPlaylistId)) continue;

            // 优先通过 SongId 匹配，其次通过 Title+Artist 匹配
            var song = allSongs.FirstOrDefault(s => s.Id == ps.SongId);
            if (song == null && !string.IsNullOrEmpty(ps.SongTitle))
            {
                var key = $"{(ps.SongTitle?.Trim() ?? "")}|{(ps.SongArtist?.Trim() ?? "")}";
                songKeyMap.TryGetValue(key, out var matchedId);
                if (matchedId > 0)
                    song = allSongs.FirstOrDefault(s => s.Id == matchedId);
            }

            if (song != null)
            {
                await _db.AddSongToPlaylistAsync(newPlaylistId, song.Id);
            }
        }
    }

    private async Task RestorePlayHistoryAsync(BackupData data)
    {
        var allSongs = await _db.GetSongsAsync();
        var songIdSet = allSongs.Select(s => s.Id).ToHashSet();

        // 构建 Title+Artist → SongId 映射
        var songKeyMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in allSongs)
        {
            var key = $"{(s.Title?.Trim() ?? "")}|{(s.Artist?.Trim() ?? "")}";
            if (!songKeyMap.ContainsKey(key))
                songKeyMap[key] = s.Id;
        }

        foreach (var ph in data.PlayHistory)
        {
            // 优先 SongId，其次 Title+Artist
            int songId = 0;
            if (songIdSet.Contains(ph.SongId))
            {
                songId = ph.SongId;
            }
            else if (!string.IsNullOrEmpty(ph.SongTitle))
            {
                var key = $"{(ph.SongTitle?.Trim() ?? "")}|{(ph.SongArtist?.Trim() ?? "")}";
                songKeyMap.TryGetValue(key, out songId);
            }

            if (songId > 0)
            {
                for (int i = 0; i < ph.PlayCount; i++)
                    await _db.RecordPlayAsync(songId);
            }
        }
    }

    private async Task RestoreFavoritesAsync(BackupData data)
    {
        var allSongs = await _db.GetSongsAsync();
        var songIdSet = allSongs.Select(s => s.Id).ToHashSet();

        // 构建 Title+Artist → SongId 映射
        var songKeyMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in allSongs)
        {
            var key = $"{(s.Title?.Trim() ?? "")}|{(s.Artist?.Trim() ?? "")}";
            if (!songKeyMap.ContainsKey(key))
                songKeyMap[key] = s.Id;
        }

        foreach (var fav in data.Favorites)
        {
            // 优先 SongId，其次 Title+Artist
            int songId = 0;
            if (songIdSet.Contains(fav.SongId))
            {
                songId = fav.SongId;
            }
            else if (!string.IsNullOrEmpty(fav.SongTitle))
            {
                var key = $"{(fav.SongTitle?.Trim() ?? "")}|{(fav.SongArtist?.Trim() ?? "")}";
                songKeyMap.TryGetValue(key, out songId);
            }

            if (songId > 0)
            {
                var isFav = await _db.IsFavoriteAsync(songId);
                if (!isFav)
                    await _db.SetFavoriteAsync(songId, true);
            }
        }
    }

    private async Task RestoreArtistMetadataAsync(BackupData data)
    {
        var artists = await _db.GetAllArtistsAsync();
        var artistByName = artists.ToDictionary(a => a.Name, a => a);

        foreach (var entry in data.Artists)
        {
            if (!artistByName.TryGetValue(entry.Name, out var artist)) continue;

            bool changed = false;
            if (string.IsNullOrEmpty(artist.Gender) && !string.IsNullOrEmpty(entry.Gender))
                { artist.Gender = entry.Gender; changed = true; }
            if (string.IsNullOrEmpty(artist.Birthday) && !string.IsNullOrEmpty(entry.Birthday))
                { artist.Birthday = entry.Birthday; changed = true; }
            if (string.IsNullOrEmpty(artist.Region) && !string.IsNullOrEmpty(entry.Region))
                { artist.Region = entry.Region; changed = true; }
            if (string.IsNullOrEmpty(artist.Description) && !string.IsNullOrEmpty(entry.Description))
                { artist.Description = entry.Description; changed = true; }

            if (changed)
                await _db.UpdateArtistAsync(artist);
        }
    }

    private void RestoreLlmConfigs(BackupData data)
    {
        if (data.LlmConfigs.Count > 0)
            AgentService.SaveAllConfigs(data.LlmConfigs);
        if (!string.IsNullOrEmpty(data.CurrentConfigName))
            AgentService.SetCurrentConfigName(data.CurrentConfigName);
        if (!string.IsNullOrEmpty(data.CurrentAgentId))
            AgentService.SaveCurrentAgentId(data.CurrentAgentId);
    }

    // ═══════════ ZIP 打包 / 解压 ═══════════

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

    private async Task<List<ArtistCoverBackupEntry>> LoadArtistCoversAsync(
        string backupFilePath, BackupData data, IProgress<BackupProgress>? progress, int startPercent, int endPercent)
    {
        var entries = new List<ArtistCoverBackupEntry>();
        if (string.IsNullOrEmpty(_artistCoversDir) || !System.IO.Directory.Exists(_artistCoversDir))
        {
            System.Diagnostics.Debug.WriteLine($"[Backup] 艺术家封面缓存目录不存在或为空: {_artistCoversDir}");
            return entries;
        }

        var artists = await _db.GetAllArtistsAsync();
        var cachedFiles = System.IO.Directory.GetFiles(_artistCoversDir);
        System.Diagnostics.Debug.WriteLine($"[Backup] 艺术家总数: {artists.Count}, 缓存目录文件数: {cachedFiles.Length}, 目录: {_artistCoversDir}");

        var backupDir = GetBackupCoversDirectory(backupFilePath);
        System.IO.Directory.CreateDirectory(backupDir);

        // 优先使用数据库中记录的 Cover 路径；若失效，则在缓存目录按艺术家名兜底查找
        var artistsWithCover = artists
            .Select(a => new { Artist = a, CoverPath = ResolveArtistCoverPath(a) })
            .Where(x => x.CoverPath != null)
            .ToList();

        System.Diagnostics.Debug.WriteLine($"[Backup] 找到可备份的艺术家封面数: {artistsWithCover.Count}");
        if (artistsWithCover.Count == 0 && artists.Count > 0)
        {
            var sampleNoCover = artists.Take(5).Select(a => $"{a.Name}(Cover={a.Cover ?? "null"})");
            System.Diagnostics.Debug.WriteLine($"[Backup] 示例无封面艺术家: {string.Join(", ", sampleNoCover)}");
        }

        int total = artistsWithCover.Count;
        int index = 0;
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

                // 若数据库中的路径已失效，但缓存目录兜底找到，则顺便修正数据库
                if (artist.Cover != coverPath)
                {
                    artist.Cover = coverPath;
                    await _db.UpdateArtistAsync(artist);
                }
            }
            catch { /* 单张封面备份失败不影响整体 */ }

            index++;
            var percent = startPercent + (endPercent - startPercent) * index / Math.Max(total, 1);
            Report(progress, percent, $"正在复制艺术家照片 ({index}/{total})...");
        }

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

    private static string GetBackupCoversDirectory(string backupFilePath)
        => System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(backupFilePath)!,
            System.IO.Path.GetFileNameWithoutExtension(backupFilePath) + "_covers");

    private static async Task CopyFileAsync(string sourcePath, string destPath)
    {
        await using var sourceStream = System.IO.File.OpenRead(sourcePath);
        await using var destStream = System.IO.File.Create(destPath);
        await sourceStream.CopyToAsync(destStream);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

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

