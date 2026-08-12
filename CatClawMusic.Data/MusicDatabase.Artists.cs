using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using SQLite;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Data;

/// <summary>SQLite 数据库操作层 —— partial 分域文件之一。</summary>
public partial class MusicDatabase
{
    public async Task<int> EnsureArtistAsync(string name)
    {
        await EnsureMaintenanceCompletedAsync();
        if (string.IsNullOrEmpty(name)) return 0;

        // 防御性拆分：无论调用方是否已拆分，此处统一处理
        // 避免 "国风堂/哦漏" 被直接写入 Artists 表
        var names = MusicUtility.SplitArtistNames(name);
        var firstId = 0;

        foreach (var n in names)
        {
            var a = await _database.Table<Artist>().Where(x => x.Name == n).FirstOrDefaultAsync();
            if (a != null)
            {
                if (firstId == 0) firstId = a.Id;
            }
            else
            {
                var newArtist = new Artist { Name = n };
                await _database.InsertAsync(newArtist);
                if (firstId == 0) firstId = newArtist.Id;
            }
        }

        return firstId > 0 ? firstId : 0;
    }

    /// <summary>
    /// 获取所有艺术家列表
    /// </summary>
    /// <returns>艺术家列表</returns>
    public async Task<List<Artist>> GetAllArtistsAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        return await _database.Table<Artist>().ToListAsync();
    }

    /// <summary>
    /// 按名称精确查询单个艺术家（单行查询，避免全表加载后内存过滤）
    /// </summary>
    /// <param name="name">艺术家名称</param>
    /// <returns>匹配的艺术家；无匹配返回 null</returns>
    public async Task<Artist?> FindArtistByNameAsync(string name)
    {
        await EnsureMaintenanceCompletedAsync();
        return await _database.Table<Artist>().Where(a => a.Name == name).FirstOrDefaultAsync();
    }

    /// <summary>
    /// 更新艺术家信息
    /// </summary>
    /// <param name="artist">艺术家对象</param>
    public async Task UpdateArtistAsync(Artist artist)
    {
        await EnsureMaintenanceCompletedAsync();
        await _database.UpdateAsync(artist);
    }

    /// <summary>批量更新艺术家（在单事务中执行所有 UPDATE）</summary>
    /// <param name="artists">需要更新的艺术家集合</param>
    public async Task UpdateArtistsBatchAsync(IEnumerable<Artist> artists)
    {
        var list = artists?.ToList();
        if (list == null || list.Count == 0) return;
        await EnsureMaintenanceCompletedAsync();
        await _database.RunInTransactionAsync(tran =>
        {
            foreach (var a in list)
                tran.Update(a);
        });
    }

    /// <summary>
    /// 获取所有专辑列表
    /// </summary>
    /// <returns>专辑列表</returns>
    public async Task<List<Album>> GetAllAlbumsAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        return await _database.Table<Album>().ToListAsync();
    }

    /// <summary>获取指定艺术家的所有专辑（包括主艺术家专辑和参与专辑）</summary>
    /// <param name="artistName">艺术家名称</param>
    /// <returns>专辑列表</returns>
    public async Task<List<Album>> GetAlbumsByArtistAsync(string artistName)
    {
        await EnsureMaintenanceCompletedAsync();
        var sql = @"
            SELECT DISTINCT al.*
            FROM Albums al
            LEFT JOIN Artists a ON al.ArtistId = a.Id
            LEFT JOIN Songs s ON s.AlbumId = al.Id
            LEFT JOIN SongArtists sa ON s.Id = sa.SongId
            LEFT JOIN Artists a2 ON sa.ArtistId = a2.Id
            WHERE a.Name = ? OR a2.Name = ?
            ORDER BY al.Year DESC, al.Title
        ";
        return await _database.QueryAsync<Album>(sql, artistName, artistName);
    }

    /// <summary>批量获取指定专辑ID列表的采样歌曲（每个专辑一首，有文件路径的）</summary>
    public async Task<List<Song>> GetSampleSongsForAlbumsAsync(IEnumerable<int> albumIds)
    {
        await EnsureMaintenanceCompletedAsync();
        var ids = albumIds.ToList();
        if (ids.Count == 0) return new List<Song>();
        var placeholders = string.Join(",", ids.Select(_ => "?"));
        var sql = $@"
            SELECT s.*
            FROM Songs s
            WHERE s.AlbumId IN ({placeholders})
              AND s.FilePath IS NOT NULL AND s.FilePath != ''
            GROUP BY s.AlbumId
        ";
        return await _database.QueryAsync<Song>(sql, ids.Cast<object>().ToArray());
    }

    /// <summary>
    /// 修复歌曲的 AlbumId 关联：根据歌曲的 Album 名称和 Artist 名称重新匹配正确的专辑 ID。
    /// 解决早期版本中 ArtistId=0 导致 AlbumId 关联错误的问题。
    /// </summary>
    public async Task RepairAlbumAssociationsAsync()
    {
        // 注意：此方法可能从 EnsureInitializedAsync 内部调用，不能再调 EnsureInitializedAsync 以避免信号量死锁
        // Song.Artist 和 Song.Album 是 [Ignore] 字段，不存储在数据库中
        // 需要从文件标签重新读取来修复关联
        try
        {
            var songs = await _database.Table<Song>().ToListAsync();
            var artists = await _database.Table<Artist>().ToListAsync();
            var albums = await _database.Table<Album>().ToListAsync();

            var artistDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in artists)
                if (!artistDict.ContainsKey(a.Name))
                    artistDict[a.Name] = a.Id;

            var albumDict = new Dictionary<(string title, int artistId), int>();
            foreach (var a in albums)
                if (!albumDict.ContainsKey((a.Title, a.ArtistId)))
                    albumDict[(a.Title, a.ArtistId)] = a.Id;

            int fixedCount = 0;
            await _database.RunInTransactionAsync(tran =>
            {
                foreach (var song in songs)
                {
                    // 从文件标签重新读取艺术家和专辑名
                    string? artistName = null;
                    string? albumName = null;

                    if (!string.IsNullOrEmpty(song.FilePath) && System.IO.File.Exists(song.FilePath))
                    {
                        try
                        {
                            var tagInfo = TagReader.ReadSongInfo(song.FilePath);
                            if (tagInfo != null)
                            {
                                artistName = tagInfo.Artist;
                                albumName = tagInfo.Album;
                            }
                        }
                        catch { }
                    }

                    // 回退到 ExtractArtistNameCallback
                    if (string.IsNullOrEmpty(artistName) && ExtractArtistNameCallback != null && !string.IsNullOrEmpty(song.FilePath))
                        artistName = ExtractArtistNameCallback(song.FilePath);

                    // 重新计算正确的 ArtistId
                    int correctArtistId = song.ArtistId;
                    if (!string.IsNullOrEmpty(artistName))
                    {
                        if (artistDict.TryGetValue(artistName, out var aid))
                        {
                            correctArtistId = aid;
                        }
                        else
                        {
                            // 艺术家不在数据库中，创建新的
                            var newArtist = new Artist { Name = artistName };
                            tran.Insert(newArtist);
                            correctArtistId = newArtist.Id;
                            artistDict[artistName] = correctArtistId;
                        }
                    }

                    // 重新计算正确的 AlbumId
                    int correctAlbumId = song.AlbumId;
                    if (!string.IsNullOrEmpty(albumName))
                    {
                        if (albumDict.TryGetValue((albumName, correctArtistId), out var albId))
                        {
                            correctAlbumId = albId;
                        }
                        else
                        {
                            // 创建新的专辑
                            var newAlbum = new Album { Title = albumName, ArtistId = correctArtistId };
                            tran.Insert(newAlbum);
                            correctAlbumId = newAlbum.Id;
                            albumDict[(albumName, correctArtistId)] = correctAlbumId;
                        }
                    }

                    // 如果 ArtistId 或 AlbumId 有误，更新
                    if (correctArtistId != song.ArtistId || correctAlbumId != song.AlbumId)
                    {
                        song.ArtistId = correctArtistId;
                        song.AlbumId = correctAlbumId;
                        tran.Update(song);
                        fixedCount++;
                    }
                }
            });
            Log.Debug("MusicDatabase", $"[CatClaw] 专辑关联修复完成，修正 {fixedCount} 首歌曲");
        }
        catch (Exception ex)
        {
            Log.Debug("MusicDatabase", $"[CatClaw] 专辑关联修复失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 批量确保艺术家存在，返回艺术家名到 ID 的映射。一次性查询 + 批量插入，避免逐条数据库往返。
    /// </summary>
    public async Task<Dictionary<string, int>> EnsureArtistsBatchAsync(List<string> names)
    {
        await EnsureMaintenanceCompletedAsync();
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) return result;

        // 统一拆分 "A/B" 等多艺术家名字
        var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            foreach (var n in MusicUtility.SplitArtistNames(name))
                allNames.Add(n);
        }

        if (allNames.Count == 0) return result;

        // 批量查询已存在的艺术家（分批避免 IN 子句过长）
        var nameList = allNames.ToList();
        const int chunkSize = 500;
        for (int i = 0; i < nameList.Count; i += chunkSize)
        {
            var chunk = nameList.Skip(i).Take(chunkSize).ToList();
            var existing = await _database.Table<Artist>()
                .Where(a => chunk.Contains(a.Name))
                .ToListAsync();
            foreach (var a in existing)
                result[a.Name] = a.Id;
        }

        // 批量插入缺失的艺术家
        var missing = allNames.Where(n => !result.ContainsKey(n)).ToList();
        if (missing.Count > 0)
        {
            await _database.RunInTransactionAsync(tran =>
            {
                foreach (var n in missing)
                {
                    var artist = new Artist { Name = n };
                    tran.Insert(artist);
                    result[n] = artist.Id;
                }
            });
        }

        return result;
    }

    /// <summary>
    /// 批量确保专辑存在，返回 (专辑名, 艺术家ID) 到 ID 的映射。一次性查询 + 批量插入。
    /// </summary>
    public async Task<Dictionary<(string title, int artistId), int>> EnsureAlbumsBatchAsync(List<(string title, int artistId)> albums)
    {
        await EnsureMaintenanceCompletedAsync();
        var result = new Dictionary<(string title, int artistId), int>();
        if (albums.Count == 0) return result;

        var uniqueAlbums = albums
            .Where(a => !string.IsNullOrEmpty(a.title))
            .Distinct()
            .ToList();

        if (uniqueAlbums.Count == 0) return result;

        // 按艺术家 ID 分批查询已存在的专辑
        var artistIds = uniqueAlbums.Select(a => a.artistId).Distinct().ToList();
        const int idChunkSize = 300;
        var existingDict = new Dictionary<(string title, int artistId), Album>();
        for (int i = 0; i < artistIds.Count; i += idChunkSize)
        {
            var chunk = artistIds.Skip(i).Take(idChunkSize).ToList();
            var existing = await _database.Table<Album>()
                .Where(al => chunk.Contains(al.ArtistId))
                .ToListAsync();
            foreach (var al in existing)
            {
                var key = (al.Title, al.ArtistId);
                if (!existingDict.ContainsKey(key))
                    existingDict[key] = al;
            }
        }

        foreach (var key in uniqueAlbums)
        {
            if (existingDict.TryGetValue(key, out var al))
                result[key] = al.Id;
        }

        // 批量插入缺失的专辑
        var missing = uniqueAlbums.Where(k => !result.ContainsKey(k)).ToList();
        if (missing.Count > 0)
        {
            await _database.RunInTransactionAsync(tran =>
            {
                foreach (var k in missing)
                {
                    var album = new Album { Title = k.title, ArtistId = k.artistId };
                    tran.Insert(album);
                    result[k] = album.Id;
                }
            });
        }

        return result;
    }

    /// <summary>
    /// 根据标题和艺术家 ID 查找或创建专辑，返回专辑 ID
    /// </summary>
    /// <param name="title">专辑标题</param>
    /// <param name="artistId">艺术家 ID</param>
    /// <returns>专辑 ID，标题为空时返回 0</returns>
    public async Task<int> EnsureAlbumAsync(string title, int artistId)
    {
        await EnsureMaintenanceCompletedAsync();
        if (string.IsNullOrEmpty(title)) return 0;
        var a = await _database.Table<Album>().Where(x => x.Title == title && x.ArtistId == artistId).FirstOrDefaultAsync();
        if (a != null) return a.Id;
        var newAlbum = new Album { Title = title, ArtistId = artistId };
        await _database.InsertAsync(newAlbum);
        return newAlbum.Id;
    }

    // ═══════════ Play History ═══════════

    /// <summary>
    /// 记录播放历史，已存在的记录会更新播放时间和次数。
    /// 同时写入 PlaySession 逐次日志（用于趋势/时段分布等统计）。
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
}
