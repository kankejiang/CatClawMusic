using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services.AI;
using CatClawMusic.Core.Interfaces;
using SQLite;
using System.Text.Json;

namespace CatClawMusic.Data;

/// <summary>备份恢复服务 —— partial 分域文件。</summary>
public partial class BackupService
{
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
}
