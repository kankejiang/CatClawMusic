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
    All            = Playlists | PlayHistory | Favorites | Artists | LlmConfigs,
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
    /// <param name="items">要备份的数据类别</param>
    /// <returns>备份文件路径</returns>
    public async Task<string> BackupAsync(string externalStoragePath, BackupItems items = BackupItems.All)
    {
        await _db.EnsureInitializedAsync();

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
        if (items.HasFlag(BackupItems.Artists))
            data.Artists = await LoadArtistMetadataAsync();
        if (items.HasFlag(BackupItems.LlmConfigs))
        {
            data.LlmConfigs = AgentService.LoadAllConfigs();
            data.CurrentConfigName = AgentService.GetCurrentConfigName();
            data.CurrentAgentId = AgentService.LoadCurrentAgentId();
        }

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
    /// <param name="items">要恢复的数据类别</param>
    public async Task RestoreAsync(string filePath, BackupItems items = BackupItems.All)
    {
        await _db.EnsureInitializedAsync();

        var json = await System.IO.File.ReadAllTextAsync(filePath);
        var data = JsonSerializer.Deserialize<BackupData>(json, JsonOptions)
            ?? throw new InvalidOperationException("备份文件格式无效");

        if (items.HasFlag(BackupItems.Playlists))
            await RestorePlaylistsAsync(data);

        if (items.HasFlag(BackupItems.PlayHistory))
            await RestorePlayHistoryAsync(data);

        if (items.HasFlag(BackupItems.Favorites))
            await RestoreFavoritesAsync(data);

        if (items.HasFlag(BackupItems.Artists))
            await RestoreArtistMetadataAsync(data);

        if (items.HasFlag(BackupItems.LlmConfigs))
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

    /// <summary>获取数据库连接（用于直接查询 PlaylistSong）</summary>
    private SQLiteAsyncConnection GetDatabaseConnection()
    {
        // 通过反射获取 _database 私有字段
        var field = typeof(MusicDatabase).GetField("_database",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (SQLiteAsyncConnection)field!.GetValue(_db)!;
    }
}
