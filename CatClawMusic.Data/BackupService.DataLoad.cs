using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services.AI;
using CatClawMusic.Core.Interfaces;
using SQLite;
using System.Text.Json;

namespace CatClawMusic.Data;

/// <summary>备份恢复服务 —— partial 分域文件。</summary>
public partial class BackupService
{
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
}
