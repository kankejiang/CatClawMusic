using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using SQLite;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Data;

/// <summary>SQLite 数据库操作层 —— partial 分域文件之一。</summary>
public partial class MusicDatabase
{
    public async Task<int> SaveConnectionProfileAsync(ConnectionProfile profile)
    {
        await EnsureMaintenanceCompletedAsync();
        if (profile.Id != 0) return await _database.UpdateAsync(profile);
        return await _database.InsertAsync(profile);
    }

    /// <summary>
    /// 获取所有连接配置
    /// </summary>
    /// <returns>连接配置列表</returns>
    public Task<List<ConnectionProfile>> GetConnectionProfilesAsync()
        => _database.Table<ConnectionProfile>().ToListAsync();

    /// <summary>
    /// 删除指定连接配置
    /// </summary>
    /// <param name="profileId">连接配置主键</param>
    /// <returns>受影响的行数</returns>
    public async Task<int> DeleteConnectionProfileAsync(int profileId)
    {
        await EnsureMaintenanceCompletedAsync();
        return await _database.DeleteAsync<ConnectionProfile>(profileId);
    }

    // ═══════════ Playlist CRUD ═══════════

    /// <summary>
    /// 获取所有播放列表
    /// </summary>
    /// <returns>播放列表列表</returns>
    public Task<List<Playlist>> GetAllPlaylistsAsync()
    {
        return _database.Table<Playlist>().ToListAsync();
    }

    /// <summary>
    /// 根据 ID 获取播放列表
    /// </summary>
    /// <param name="id">播放列表 ID</param>
    /// <returns>播放列表对象，未找到时返回 null</returns>
    public Task<Playlist?> GetPlaylistByIdAsync(int id)
    {
        return _database.Table<Playlist>().Where(p => p.Id == id).FirstOrDefaultAsync();
    }

    /// <summary>
    /// 创建新的播放列表
    /// </summary>
    /// <param name="name">播放列表名称</param>
    /// <returns>新播放列表的 ID</returns>
    public async Task<int> CreatePlaylistAsync(string name)
    {
        await EnsureMaintenanceCompletedAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var playlist = new Playlist { Name = name, CreatedAt = now, UpdatedAt = now };
        return await _database.InsertAsync(playlist);
    }

    /// <summary>
    /// 更新播放列表信息（自动刷新更新时间）
    /// </summary>
    /// <param name="playlist">播放列表对象</param>
    public async Task UpdatePlaylistAsync(Playlist playlist)
    {
        await EnsureMaintenanceCompletedAsync();
        playlist.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _database.UpdateAsync(playlist);
    }

    /// <summary>
    /// 删除播放列表及其所有关联歌曲
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    public async Task DeletePlaylistAsync(int playlistId)
    {
        await EnsureMaintenanceCompletedAsync();
        await _database.ExecuteAsync("DELETE FROM PlaylistSongs WHERE PlaylistId = ?", playlistId);
        await _database.DeleteAsync<Playlist>(playlistId);
    }

    /// <summary>
    /// 将歌曲添加到播放列表末尾（重复则忽略）
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="songId">歌曲 ID</param>
    public async Task AddSongToPlaylistAsync(int playlistId, int songId)
    {
        await EnsureMaintenanceCompletedAsync();
        var existing = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId && ps.SongId == songId)
            .FirstOrDefaultAsync();
        if (existing != null) return;

        var maxPos = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .CountAsync();
        await _database.InsertAsync(new PlaylistSong
        {
            PlaylistId = playlistId,
            SongId = songId,
            Position = maxPos
        });

        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist != null)
        {
            playlist.SongCount = maxPos + 1;
            await UpdatePlaylistAsync(playlist);
        }
    }

    /// <summary>
    /// 批量添加歌曲到播放列表（跳过已存在的）。
    /// 内部在单事务中顺序写入，Position 从当前 maxPos+1 递增。
    /// </summary>
    /// <param name="playlistId">目标播放列表 ID</param>
    /// <param name="songIds">需要添加的歌曲 ID 集合</param>
    public async Task AddSongsToPlaylistBatchAsync(int playlistId, IEnumerable<int> songIds)
    {
        var ids = songIds?.Distinct().Where(id => id > 0).ToList();
        if (ids == null || ids.Count == 0) return;
        await EnsureMaintenanceCompletedAsync();

        // 一次性查询已存在的 (避免逐首 IsExisting 查询)
        const int chunkSize = 500;
        var existingIds = new HashSet<int>();
        for (int i = 0; i < ids.Count; i += chunkSize)
        {
            var chunk = ids.Skip(i).Take(chunkSize).ToList();
            var existing = await _database.Table<PlaylistSong>()
                .Where(ps => ps.PlaylistId == playlistId && chunk.Contains(ps.SongId))
                .ToListAsync();
            foreach (var ps in existing) existingIds.Add(ps.SongId);
        }

        var missing = ids.Where(id => !existingIds.Contains(id)).ToList();
        if (missing.Count == 0) return;

        // 起始 Position = 当前数量（在事务外查询，事务内递增写入）
        var startPos = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .CountAsync();

        await _database.RunInTransactionAsync(tran =>
        {
            int pos = startPos;
            foreach (var songId in missing)
            {
                tran.Execute(
                    "INSERT INTO PlaylistSong(PlaylistId, SongId, Position) VALUES (?, ?, ?)",
                    playlistId, songId, pos);
                pos++;
            }
        });

        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist != null)
        {
            playlist.SongCount = startPos + missing.Count;
            await UpdatePlaylistAsync(playlist);
        }
    }

    /// <summary>
    /// 从播放列表中移除歌曲并重新调整位置
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="songId">歌曲 ID</param>
    public async Task RemoveSongFromPlaylistAsync(int playlistId, int songId)
    {
        await EnsureMaintenanceCompletedAsync();
        var entry = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId && ps.SongId == songId)
            .FirstOrDefaultAsync();
        if (entry == null) return;

        await _database.DeleteAsync(entry);

        var remaining = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .OrderBy(ps => ps.Position)
            .ToListAsync();
        for (int i = 0; i < remaining.Count; i++)
        {
            remaining[i].Position = i;
            await _database.UpdateAsync(remaining[i]);
        }

        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist != null)
        {
            playlist.SongCount = remaining.Count;
            await UpdatePlaylistAsync(playlist);
        }
    }

    /// <summary>
    /// 从播放列表中批量移除歌曲并重新调整位置
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="songIds">要移除的歌曲 ID 集合</param>
    public async Task RemoveSongsFromPlaylistAsync(int playlistId, IEnumerable<int> songIds)
    {
        await EnsureMaintenanceCompletedAsync();
        var ids = songIds.ToHashSet();
        if (ids.Count == 0) return;

        // 一次性删除目标记录
        await _database.ExecuteAsync(
            "DELETE FROM PlaylistSongs WHERE PlaylistId = ? AND SongId IN (" + string.Join(",", ids) + ")",
            playlistId);

        // 重新整理剩余歌曲的位置
        var remaining = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .OrderBy(ps => ps.Position)
            .ToListAsync();
        for (int i = 0; i < remaining.Count; i++)
        {
            if (remaining[i].Position != i)
            {
                remaining[i].Position = i;
                await _database.UpdateAsync(remaining[i]);
            }
        }

        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist != null)
        {
            playlist.SongCount = remaining.Count;
            await UpdatePlaylistAsync(playlist);
        }
    }

    /// <summary>
    /// 获取播放列表中的所有歌曲（按位置排序，含艺术家和专辑信息）
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <returns>歌曲列表</returns>
    public async Task<List<Song>> GetPlaylistSongsAsync(int playlistId)
    {
        await EnsureMaintenanceCompletedAsync();
        var entries = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .OrderBy(ps => ps.Position)
            .ToListAsync();
        if (entries.Count == 0) return new List<Song>();

        var songIds = entries.Select(e => e.SongId).ToList();
        var songs = await _database.Table<Song>().Where(s => songIds.Contains(s.Id)).ToListAsync();
        var artists = await _database.Table<Artist>().ToListAsync();
        var albums = await _database.Table<Album>().ToListAsync();
        var artistDict = SafeToDict(artists, a => a.Id, a => a.Name);
        var albumDict = SafeToDict(albums, a => a.Id, a => a.Title);

        var songMap = SafeToDict(songs, s => s.Id, s => s);

        var sorted = new List<Song>(entries.Count);
        var allArtistsDict5 = await GetAllArtistsForSongsAsync(songs.Select(s => s.Id));
        foreach (var entry in entries)
        {
            if (songMap.TryGetValue(entry.SongId, out var song))
            {
                song.Artist = artistDict.TryGetValue(song.ArtistId, out var an) ? an : "未知艺术家";
                song.Album = albumDict.TryGetValue(song.AlbumId, out var al) ? al : "未知专辑";
                song.AllArtists = allArtistsDict5.TryGetValue(song.Id, out var aa) ? aa : song.Artist;
                sorted.Add(song);
            }
        }
        return sorted;
    }

    /// <summary>
    /// 更新播放列表中歌曲的位置
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="songId">歌曲 ID</param>
    /// <param name="newPosition">新位置索引</param>
    public async Task UpdateSongPositionAsync(int playlistId, int songId, int newPosition)
    {
        await EnsureMaintenanceCompletedAsync();
        var entry = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId && ps.SongId == songId)
            .FirstOrDefaultAsync();
        if (entry == null) return;

        entry.Position = newPosition;
        await _database.UpdateAsync(entry);
    }

    /// <summary>
    /// 批量更新播放列表中所有歌曲的顺序位置
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="orderedSongIds">排序后的歌曲 ID 列表</param>
    public async Task UpdatePlaylistOrderAsync(int playlistId, List<int> orderedSongIds)
    {
        await EnsureMaintenanceCompletedAsync();

        var allEntries = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .ToListAsync();

        Log.Debug("MusicDatabase", $"[DB] UpdatePlaylistOrderAsync: playlistId={playlistId}, orderedSongIds.Count={orderedSongIds.Count}, existingEntries.Count={allEntries.Count}");

        var entryDict = SafeToDict(allEntries, e => e.SongId, e => e);

        int updatedCount = 0;
        int missingCount = 0;

        for (int i = 0; i < orderedSongIds.Count; i++)
        {
            var songId = orderedSongIds[i];
            if (entryDict.TryGetValue(songId, out var entry))
            {
                entry.Position = i;
                await _database.UpdateAsync(entry);
                updatedCount++;
            }
            else
            {
                missingCount++;
                Log.Debug("MusicDatabase", $"[DB] UpdatePlaylistOrderAsync: SongId={songId} not found in PlaylistSong for playlist {playlistId}");
            }
        }

        Log.Debug("MusicDatabase", $"[DB] UpdatePlaylistOrderAsync completed: {updatedCount} updated, {missingCount} missing");
    }

    /// <summary>
    /// 获取播放列表中的歌曲数量
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <returns>歌曲数量</returns>
    public async Task<int> GetPlaylistSongCountAsync(int playlistId)
    {
        return await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .CountAsync();
    }

    /// <summary>
    /// 获取播放列表中的第一首歌曲
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <returns>歌曲对象，播放列表为空时返回 null</returns>
    public async Task<Song?> GetFirstSongInPlaylistAsync(int playlistId)
    {
        await EnsureMaintenanceCompletedAsync();
        var entry = await _database.Table<PlaylistSong>()
            .Where(ps => ps.PlaylistId == playlistId)
            .OrderBy(ps => ps.Position)
            .FirstOrDefaultAsync();
        if (entry == null) return null;

        var song = await _database.Table<Song>().Where(s => s.Id == entry.SongId).FirstOrDefaultAsync();
        if (song == null) return null;

        var artist = await _database.Table<Artist>().Where(a => a.Id == song.ArtistId).FirstOrDefaultAsync();
        var album = await _database.Table<Album>().Where(a => a.Id == song.AlbumId).FirstOrDefaultAsync();
        song.Artist = artist?.Name ?? "未知艺术家";
        song.Album = album?.Title ?? "未知专辑";

        // 填充多艺术家
        var allArtistsDict = await GetAllArtistsForSongsAsync(new[] { song.Id });
        song.AllArtists = allArtistsDict.TryGetValue(song.Id, out var aa) ? aa : song.Artist;

        return song;
    }

    // ═══════════ CachedSong CRUD ═══════════

    /// <summary>
    /// 保存或更新缓存歌曲信息
    /// </summary>
}
