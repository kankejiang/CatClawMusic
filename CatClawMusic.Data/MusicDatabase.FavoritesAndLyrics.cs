using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using SQLite;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Data;

/// <summary>SQLite 数据库操作层 —— partial 分域文件之一。</summary>
public partial class MusicDatabase
{
    public async Task SetFavoriteAsync(int songId, bool isFav)
    {
        // 在线插件（网易云等）入队用临时负 Id（FM/列表伪 Id），不是真实 DB 歌曲，
        // 写入收藏会造成污染：重启后伪 Id 序列重新计数，别的歌命中同一个负 Id 会误显示"已收藏"。
        if (songId <= 0) return;
        await EnsureMaintenanceCompletedAsync();
        var fav = await _database.Table<Favorite>().Where(f => f.SongId == songId).FirstOrDefaultAsync();
        if (isFav && fav == null)
            await _database.InsertAsync(new Favorite { SongId = songId, AddedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        else if (!isFav && fav != null)
            await _database.DeleteAsync(fav);
    }

    /// <summary>
    /// 批量收藏歌曲（仅 INSERT 不存在的，跳过已收藏的）。
    /// 用于备份恢复等需要一次性收藏大量歌曲的场景。
    /// </summary>
    /// <param name="songIds">需要收藏的歌曲 ID 集合</param>
    public async Task SetFavoritesBatchAsync(IEnumerable<int> songIds)
    {
        var ids = songIds?.Distinct().Where(id => id > 0).ToList();
        if (ids == null || ids.Count == 0) return;
        await EnsureMaintenanceCompletedAsync();

        // 一次性查询已存在的收藏记录
        const int chunkSize = 500;
        var existingIds = new HashSet<int>();
        for (int i = 0; i < ids.Count; i += chunkSize)
        {
            var chunk = ids.Skip(i).Take(chunkSize).ToList();
            var existing = await _database.Table<Favorite>()
                .Where(f => chunk.Contains(f.SongId))
                .ToListAsync();
            foreach (var f in existing) existingIds.Add(f.SongId);
        }

        var missing = ids.Where(id => !existingIds.Contains(id)).ToList();
        if (missing.Count == 0) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _database.RunInTransactionAsync(tran =>
        {
            foreach (var id in missing)
                tran.Execute(
                    "INSERT OR IGNORE INTO Favorites(SongId, AddedAt) VALUES (?, ?)",
                    id, now);
        });
    }

    /// <summary>
    /// 检查歌曲是否已收藏
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    /// <returns>是否已收藏</returns>
    public async Task<bool> IsFavoriteAsync(int songId)
    {
        // 负 Id 是在线插件临时伪 Id（非真实 DB 歌曲），一律视为未收藏，
        // 避免重启后伪 Id 序列重计导致的"未收藏却实心红心"误判。
        if (songId <= 0) return false;
        await EnsureMaintenanceCompletedAsync();
        return await _database.Table<Favorite>().Where(f => f.SongId == songId).CountAsync() > 0;
    }

    /// <summary>按远程 ID 查找已入库的歌曲 Id（在线收藏镜像用；未找到返回 0）</summary>
    public async Task<int> GetSongIdByRemoteIdAsync(string? remoteId)
    {
        if (string.IsNullOrWhiteSpace(remoteId)) return 0;
        await EnsureMaintenanceCompletedAsync();
        try
        {
            var row = await _database.Table<Song>()
                .Where(s => s.RemoteId == remoteId)
                .FirstOrDefaultAsync();
            return row?.Id ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 获取所有收藏记录
    /// </summary>
    /// <returns>收藏记录列表</returns>
    public Task<List<Favorite>> GetFavoritesAsync()
        => _database.Table<Favorite>().ToListAsync();

    /// <summary>获取收藏歌曲完整信息（含艺术家/专辑名）</summary>
    /// <returns>按收藏时间降序排列的歌曲列表</returns>
    public async Task<List<Song>> GetFavoriteSongsAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        var favs = await _database.Table<Favorite>().ToListAsync();
        if (favs.Count == 0) return new List<Song>();

        var allFavIds = favs.Select(f => f.SongId).ToList();
        // 单条 IN 查询批量取回，替代逐 ID 的 N+1 循环（大收藏列表下显著提速）。
        // 最终排序由下方按 AddedAt 降序处理，无需在此保持顺序。
        var favSongs = await _database.Table<Song>().Where(s => allFavIds.Contains(s.Id)).ToListAsync();
        if (favSongs.Count == 0) return new List<Song>();

        var neededArtistIds = favSongs.Select(s => s.ArtistId).Distinct().ToList();
        var neededAlbumIds = favSongs.Select(s => s.AlbumId).Distinct().ToList();
        var artists = await _database.Table<Artist>().Where(a => neededArtistIds.Contains(a.Id)).ToListAsync();
        var albums = await _database.Table<Album>().Where(a => neededAlbumIds.Contains(a.Id)).ToListAsync();
        var artistDict = SafeToDict(artists, a => a.Id, a => a.Name);
        var albumDict = SafeToDict(albums, a => a.Id, a => a.Title);

        foreach (var s in favSongs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
        }

        var favDict = SafeToDict(favs, f => f.SongId, f => f.AddedAt);

        // 填充多艺术家
        var allArtistsDict4 = await GetAllArtistsForSongsAsync(favSongs.Select(s => s.Id));
        foreach (var s in favSongs)
            s.AllArtists = allArtistsDict4.TryGetValue(s.Id, out var aa) ? aa : s.Artist;

        return favSongs.OrderByDescending(s => favDict.TryGetValue(s.Id, out var t) ? t : 0).ToList();
    }

    // ═══════════ Lyric ═══════════

    /// <summary>
    /// 保存或更新歌词信息
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    /// <param name="lrcPath">LRC 文件路径</param>
    /// <param name="content">歌词内容</param>
    public async Task SaveLyricAsync(int songId, string? lrcPath, string? content)
    {
        await EnsureMaintenanceCompletedAsync();
        var l = await _database.Table<Lyric>().Where(x => x.SongId == songId).FirstOrDefaultAsync();
        if (l != null) { l.LrcPath = lrcPath; l.Content = content; await _database.UpdateAsync(l); }
        else await _database.InsertAsync(new Lyric { SongId = songId, LrcPath = lrcPath, Content = content });
    }

    /// <summary>
    /// 获取指定歌曲的歌词信息
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    /// <returns>歌词信息，未找到时返回 null</returns>
    public Task<Lyric?> GetLyricAsync(int songId) =>
        _database.Table<Lyric>().Where(x => x.SongId == songId).FirstOrDefaultAsync();

    // ═══════════ Connection Profile ═══════════

    /// <summary>
    /// 保存或更新连接配置
    /// </summary>
    /// <param name="profile">连接配置对象</param>
}
