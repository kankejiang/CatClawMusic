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
    /// <summary>AI 聊天记录（数据库持久化的消息）</summary>
    public List<ChatMessageRecord> ChatMessages { get; set; } = new();
    /// <summary>AI 记忆文件内容（ai_memory.md 全文）</summary>
    public string? AiMemoryContent { get; set; }
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
    ChatHistory    = 1 << 6,  // AI聊天记录
    AiMemory       = 1 << 7,  // AI记忆内容
    All            = Playlists | PlayHistory | Favorites | Artists | LlmConfigs | ArtistCovers | ChatHistory | AiMemory,
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
    /// <summary>数据库访问实例</summary>
    private readonly MusicDatabase _db;
    /// <summary>AI Agent 配置存储（保留兼容，当前直接通过 AgentService 静态方法访问）</summary>
    private readonly IAgentConfigStorage _configStorage;
    /// <summary>艺术家封面缓存目录绝对路径</summary>
    private readonly string _artistCoversDir;
    /// <summary>AI 记忆文件路径（ai_memory.md）</summary>
    private readonly string _aiMemoryFilePath;

    /// <summary>JSON 序列化选项：缩进输出 + camelCase 命名</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化备份与恢复服务。
    /// </summary>
    /// <param name="db">数据库访问实例。</param>
    /// <param name="configStorage">AI Agent 配置存储。</param>
    /// <param name="artistCoversDir">艺术家封面缓存目录路径。</param>
    /// <param name="aiMemoryFilePath">AI 记忆文件路径。</param>
    public BackupService(MusicDatabase db, IAgentConfigStorage configStorage, string artistCoversDir, string aiMemoryFilePath)
    {
        _db = db;
        _configStorage = configStorage;
        _artistCoversDir = artistCoversDir;
        _aiMemoryFilePath = aiMemoryFilePath;
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
        if (items.HasFlag(BackupItems.ChatHistory))
        {
            var chatCount = await _db.GetChatMessageCountAsync();
            data.ChatMessages = await _db.GetRecentChatMessagesAsync(chatCount);
            Report(progress, 65, $"已读取 {data.ChatMessages.Count} 条聊天记录");
        }
        if (items.HasFlag(BackupItems.AiMemory))
        {
            if (System.IO.File.Exists(_aiMemoryFilePath))
                data.AiMemoryContent = await System.IO.File.ReadAllTextAsync(_aiMemoryFilePath);
            Report(progress, 70, "已读取 AI 记忆");
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

        if (items.HasFlag(BackupItems.ChatHistory))
        {
            await RestoreChatHistoryAsync(data);
            Report(progress, 90, "已恢复聊天记录");
        }

        if (items.HasFlag(BackupItems.AiMemory))
        {
            await RestoreAiMemoryAsync(data);
            Report(progress, 95, "已恢复 AI 记忆");
        }

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

    /// <summary>
    /// 加载所有歌单的歌曲关联记录，附带歌曲标题和艺术家（用于跨设备恢复）。
    /// </summary>
    /// <returns>歌单歌曲备份条目列表。</returns>
    private async Task<List<PlaylistSongBackupEntry>> LoadAllPlaylistSongsWithInfoAsync()
    {
        var playlists = await _db.GetAllPlaylistsAsync();
        if (playlists.Count == 0) return new List<PlaylistSongBackupEntry>();

        var allSongs = await _db.GetSongsAsync();
        var songMap = allSongs.ToDictionary(s => s.Id, s => s);
        var allEntries = new List<PlaylistSongBackupEntry>();

        // 一次查询所有歌单歌曲，避免逐歌单 ToListAsync 累计 RTT
        var playlistIds = playlists.Select(p => p.Id).ToList();
        var dbConn = GetDatabaseConnection();
        var allPlaylistSongs = new List<PlaylistSong>();

        // SQLite-net 不支持 IN 大列表的批量查询，分块处理（500/批）
        const int chunkSize = 500;
        for (int i = 0; i < playlistIds.Count; i += chunkSize)
        {
            var chunk = playlistIds.Skip(i).Take(chunkSize).ToList();
            var rows = await dbConn.Table<PlaylistSong>()
                .Where(ps => chunk.Contains(ps.PlaylistId))
                .ToListAsync();
            allPlaylistSongs.AddRange(rows);
        }

        // 按 PlaylistId + Position 排序
        allPlaylistSongs = allPlaylistSongs
            .OrderBy(ps => ps.PlaylistId)
            .ThenBy(ps => ps.Position)
            .ToList();

        foreach (var ps in allPlaylistSongs)
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
        return allEntries;
    }

    /// <summary>
    /// 加载最近 200 条播放记录，附带歌曲标题和艺术家（用于跨设备恢复）。
    /// </summary>
    /// <returns>播放记录备份条目列表。</returns>
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

    /// <summary>
    /// 加载所有收藏记录，附带歌曲标题和艺术家（用于跨设备恢复）。
    /// </summary>
    /// <returns>收藏备份条目列表。</returns>
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

    /// <summary>
    /// 加载所有包含元数据（性别/生日/地区/简介）的艺术家。
    /// </summary>
    /// <returns>艺术家备份条目列表。</returns>
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

    /// <summary>
    /// 恢复歌单及其歌曲关联。
    /// 通过歌单名称去重，歌曲匹配优先用 SongId，其次用 Title+Artist 组合键。
    /// </summary>
    /// <param name="data">备份数据。</param>
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

        // 恢复歌单中的歌曲关联：按 PlaylistId 分组后批量写入
        var songsByPlaylist = new Dictionary<int, List<int>>();
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
                if (!songsByPlaylist.TryGetValue(newPlaylistId, out var list))
                {
                    list = new List<int>();
                    songsByPlaylist[newPlaylistId] = list;
                }
                list.Add(song.Id);
            }
        }

        foreach (var (playlistId, songIds) in songsByPlaylist)
            await _db.AddSongsToPlaylistBatchAsync(playlistId, songIds);
    }

    /// <summary>
    /// 恢复播放记录。通过 SongId 或 Title+Artist 匹配本地歌曲，重复 RecordPlay 以还原播放次数。
    /// </summary>
    /// <param name="data">备份数据。</param>
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

        // 按 SongId 合并 PlayCount，避免 N×PlayCount 次串行 await
        var mergedPlayCount = new Dictionary<int, int>();
        foreach (var ph in data.PlayHistory)
        {
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

            if (songId > 0 && ph.PlayCount > 0)
            {
                mergedPlayCount[songId] = mergedPlayCount.TryGetValue(songId, out var c) ? c + ph.PlayCount : ph.PlayCount;
            }
        }

        if (mergedPlayCount.Count > 0)
        {
            var entries = mergedPlayCount.Select(kv => (kv.Key, kv.Value)).ToList();
            await _db.RecordPlayBatchAsync(entries);
        }
    }

    /// <summary>
    /// 恢复收藏记录。通过 SongId 或 Title+Artist 匹配本地歌曲，已收藏的跳过。
    /// </summary>
    /// <param name="data">备份数据。</param>
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

        // 收集需要收藏的 SongId，一次性批量写入
        var toFavorite = new HashSet<int>();
        foreach (var fav in data.Favorites)
        {
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
            if (songId > 0) toFavorite.Add(songId);
        }

        if (toFavorite.Count > 0)
            await _db.SetFavoritesBatchAsync(toFavorite);
    }

    /// <summary>
    /// 恢复艺术家元数据。仅当本地对应字段为空时才填充，避免覆盖用户已编辑的数据。
    /// </summary>
    /// <param name="data">备份数据。</param>
    private async Task RestoreArtistMetadataAsync(BackupData data)
    {
        var artists = await _db.GetAllArtistsAsync();
        var artistByName = artists.ToDictionary(a => a.Name, a => a);

        // 收集需要更新的艺术家，一次性批量 UPDATE
        var toUpdate = new List<Artist>();
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

            if (changed) toUpdate.Add(artist);
        }

        if (toUpdate.Count > 0)
            await _db.UpdateArtistsBatchAsync(toUpdate);
    }

    /// <summary>
    /// 恢复 LLM 配置（AI 模型配置、当前配置名、当前 Agent ID）。
    /// </summary>
    /// <param name="data">备份数据。</param>
    private void RestoreLlmConfigs(BackupData data)
    {
        if (data.LlmConfigs.Count > 0)
            AgentService.SaveAllConfigs(data.LlmConfigs);
        if (!string.IsNullOrEmpty(data.CurrentConfigName))
            AgentService.SetCurrentConfigName(data.CurrentConfigName);
        if (!string.IsNullOrEmpty(data.CurrentAgentId))
            AgentService.SaveCurrentAgentId(data.CurrentAgentId);
    }

    /// <summary>
    /// 恢复 AI 聊天记录：先清空当前记录，再按时间顺序重新插入。
    /// </summary>
    private async Task RestoreChatHistoryAsync(BackupData data)
    {
        if (data.ChatMessages.Count == 0) return;
        await _db.ClearChatMessagesAsync();
        // 批量插入，避免逐条 await
        await _db.SaveChatMessagesBatchAsync(data.ChatMessages);
    }

    /// <summary>
    /// 恢复 AI 记忆文件：将备份的记忆内容覆盖写入 ai_memory.md。
    /// </summary>
    private async Task RestoreAiMemoryAsync(BackupData data)
    {
        if (string.IsNullOrEmpty(data.AiMemoryContent)) return;
        var dir = System.IO.Path.GetDirectoryName(_aiMemoryFilePath);
        if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);
        await System.IO.File.WriteAllTextAsync(_aiMemoryFilePath, data.AiMemoryContent);
    }

    // ═══════════ ZIP 打包 / 解压 ═══════════

    /// <summary>
    /// 将 backup.json 和 covers/ 目录打包成单一 zip 文件。
    /// </summary>
    /// <param name="zipPath">目标 zip 文件路径。</param>
    /// <param name="jsonPath">backup.json 文件路径。</param>
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

