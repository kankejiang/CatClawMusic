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
    public List<PlaylistSong> PlaylistSongs { get; set; } = new();
    public List<PlayHistory> PlayHistory { get; set; } = new();
    public List<Favorite> Favorites { get; set; } = new();
    public List<ArtistBackupEntry> Artists { get; set; } = new();
    public List<LlmConfig> LlmConfigs { get; set; } = new();
    public string? CurrentConfigName { get; set; }
    public string? CurrentAgentId { get; set; }
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

/// <summary>备份与恢复服务</summary>
public class BackupService
{
    private readonly MusicDatabase _db;
    private readonly IAgentConfigStorage _configStorage;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public BackupService(MusicDatabase db, IAgentConfigStorage configStorage)
    {
        _db = db;
        _configStorage = configStorage;
    }

    /// <summary>执行备份，将数据写入指定目录下的 CatClawMusic 文件夹</summary>
    /// <param name="externalStoragePath">外部存储根目录（如 /storage/emulated/0）</param>
    /// <returns>备份文件路径</returns>
    public async Task<string> BackupAsync(string externalStoragePath)
    {
        await _db.EnsureInitializedAsync();

        var data = new BackupData
        {
            Playlists = await _db.GetAllPlaylistsAsync(),
            PlaylistSongs = await LoadAllPlaylistSongsAsync(),
            PlayHistory = await _db.GetRecentPlaysAsync(200),
            Favorites = await _db.GetFavoritesAsync(),
            Artists = await LoadArtistMetadataAsync(),
            LlmConfigs = AgentService.LoadAllConfigs(),
            CurrentConfigName = AgentService.GetCurrentConfigName(),
            CurrentAgentId = AgentService.LoadCurrentAgentId(),
        };

        var dir = System.IO.Path.Combine(externalStoragePath, "CatClawMusic");
        System.IO.Directory.CreateDirectory(dir);

        var fileName = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var filePath = System.IO.Path.Combine(dir, fileName);

        var json = JsonSerializer.Serialize(data, JsonOptions);
        await System.IO.File.WriteAllTextAsync(filePath, json);

        return filePath;
    }

    /// <summary>从备份文件恢复数据</summary>
    /// <param name="filePath">备份文件路径</param>
    public async Task RestoreAsync(string filePath)
    {
        await _db.EnsureInitializedAsync();

        var json = await System.IO.File.ReadAllTextAsync(filePath);
        var data = JsonSerializer.Deserialize<BackupData>(json, JsonOptions)
            ?? throw new InvalidOperationException("备份文件格式无效");

        // 恢复歌单
        await RestorePlaylistsAsync(data);

        // 恢复播放记录
        await RestorePlayHistoryAsync(data);

        // 恢复收藏
        await RestoreFavoritesAsync(data);

        // 恢复艺术家元数据
        await RestoreArtistMetadataAsync(data);

        // 恢复 AI 配置
        RestoreLlmConfigs(data);
    }

    /// <summary>获取备份目录路径</summary>
    public static string GetBackupDirectory(string externalStoragePath)
        => System.IO.Path.Combine(externalStoragePath, "CatClawMusic");

    /// <summary>列出备份目录中所有备份文件</summary>
    public static List<string> ListBackups(string externalStoragePath)
    {
        var dir = GetBackupDirectory(externalStoragePath);
        if (!System.IO.Directory.Exists(dir)) return new List<string>();
        return System.IO.Directory.GetFiles(dir, "backup_*.json")
            .OrderByDescending(f => f)
            .ToList();
    }

    /// <summary>从备份文件读取简要信息</summary>
    public static async Task<BackupData?> ReadBackupInfoAsync(string filePath)
    {
        try
        {
            var json = await System.IO.File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<BackupData>(json, JsonOptions);
        }
        catch { return null; }
    }

    private async Task<List<PlaylistSong>> LoadAllPlaylistSongsAsync()
    {
        var playlists = await _db.GetAllPlaylistsAsync();
        var allSongs = new List<PlaylistSong>();
        foreach (var pl in playlists)
        {
            var entries = await _db.GetPlaylistSongsAsync(pl.Id);
            // 需要从数据库直接读取 PlaylistSong 记录
            try
            {
                var dbConn = GetDatabaseConnection();
                var songs = await dbConn.Table<PlaylistSong>()
                    .Where(ps => ps.PlaylistId == pl.Id)
                    .OrderBy(ps => ps.Position)
                    .ToListAsync();
                allSongs.AddRange(songs);
            }
            catch { }
        }
        return allSongs;
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

        // 恢复歌单中的歌曲关联
        var allSongs = await _db.GetSongsAsync();
        var songIdByTitleArtist = allSongs.ToDictionary(
            s => (s.Title?.Trim() ?? "").ToLowerInvariant() + "|" + (s.Artist?.Trim() ?? "").ToLowerInvariant(),
            s => s.Id);

        foreach (var ps in data.PlaylistSongs)
        {
            if (!oldIdToNewId.TryGetValue(ps.PlaylistId, out var newPlaylistId)) continue;

            // 通过歌曲标题+艺术家匹配本地歌曲
            var song = allSongs.FirstOrDefault(s => s.Id == ps.SongId);
            if (song != null)
            {
                await _db.AddSongToPlaylistAsync(newPlaylistId, song.Id);
            }
        }
    }

    private async Task RestorePlayHistoryAsync(BackupData data)
    {
        // 播放记录基于现有歌曲恢复
        var allSongs = await _db.GetSongsAsync();
        var songIds = allSongs.Select(s => s.Id).ToHashSet();

        foreach (var ph in data.PlayHistory)
        {
            if (songIds.Contains(ph.SongId))
            {
                // 重新记录播放（会自动合并或创建）
                for (int i = 0; i < ph.PlayCount; i++)
                    await _db.RecordPlayAsync(ph.SongId);
            }
        }
    }

    private async Task RestoreFavoritesAsync(BackupData data)
    {
        var allSongs = await _db.GetSongsAsync();
        var songIds = allSongs.Select(s => s.Id).ToHashSet();

        foreach (var fav in data.Favorites)
        {
            if (songIds.Contains(fav.SongId))
            {
                var isFav = await _db.IsFavoriteAsync(fav.SongId);
                if (!isFav)
                    await _db.SetFavoriteAsync(fav.SongId, true);
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

    /// <summary>获取数据库连接（用于直接查询 PlaylistSong）</summary>
    private SQLiteAsyncConnection GetDatabaseConnection()
    {
        // 通过反射获取 _database 私有字段
        var field = typeof(MusicDatabase).GetField("_database",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (SQLiteAsyncConnection)field!.GetValue(_db)!;
    }
}
