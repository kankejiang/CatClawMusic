using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using SQLite;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Data;

/// <summary>SQLite 数据库操作层 —— partial 分域文件之一。</summary>
public partial class MusicDatabase
{
    private async Task MigrateArtistsTableAsync()
    {
        try
        {
            var columns = await _database.QueryAsync<TableColumn>("PRAGMA table_info(Artists)");
            var columnNames = columns.Select(c => c.name).ToHashSet();

            if (!columnNames.Contains("Gender"))
                try { await _database.ExecuteAsync("ALTER TABLE Artists ADD COLUMN Gender TEXT"); } catch { }
            if (!columnNames.Contains("Birthday"))
                try { await _database.ExecuteAsync("ALTER TABLE Artists ADD COLUMN Birthday TEXT"); } catch { }
            if (!columnNames.Contains("Region"))
                try { await _database.ExecuteAsync("ALTER TABLE Artists ADD COLUMN Region TEXT"); } catch { }
            if (!columnNames.Contains("Description"))
                try { await _database.ExecuteAsync("ALTER TABLE Artists ADD COLUMN Description TEXT"); } catch { }
        }
        catch { }
    }

    /// <summary>
    /// 恢复 Artist 表数据：之前 [Table] 属性和 [PrimaryKey,AutoIncrement] 丢失导致：
    /// 1. ORM 创建了错误的 "Artist" 单数表（所有 Id=0）
    /// 2. "Artists" 复数表可能也有 Id=0 的脏数据
    /// 按 Name 合并数据，重建 Artists 表结构
    /// </summary>
    private async Task RecoverArtistsTableAsync()
    {
        try
        {
            // 检查 Artists 表是否有主键（没有说明表结构损坏需要重建）
            var artistsCols = await _database.QueryAsync<TableColumn>("PRAGMA table_info(Artists)");
            var hasPrimaryKey = artistsCols.Any(c => c.pk > 0);

            // 检查是否存在错误的 "Artist" 单数表
            var hasArtistTable = await TableExistsAsync("Artist");

            if (!hasPrimaryKey || hasArtistTable)
            {
                await RebuildArtistsTableAsync(hasArtistTable);
            }
        }
        catch { }
    }

    /// <summary>
    /// 重建 Artists 表：合并 Artist 和 Artists 表数据，按 Name 去重，重建正确表结构
    /// </summary>
    private async Task RebuildArtistsTableAsync(bool hasArtistTable)
    {
        // 读取两个表的所有数据（按 Name 去重合并）
        var mergedArtists = new Dictionary<string, ArtistRecoveryRow>();

        // 1. 从 Artists 表读取
        try
        {
            var artistsData = await _database.QueryAsync<ArtistRecoveryRow>(
                "SELECT Name, Cover, Gender, Birthday, Region, Description FROM Artists");
            foreach (var a in artistsData)
            {
                if (!string.IsNullOrEmpty(a.Name) && !mergedArtists.ContainsKey(a.Name))
                    mergedArtists[a.Name] = a;
            }
        }
        catch { }

        // 2. 从 Artist 表读取（补充 Artists 表没有的）
        if (hasArtistTable)
        {
            try
            {
                var artistData = await _database.QueryAsync<ArtistRecoveryRow>(
                    "SELECT Name, Cover, Gender, Birthday, Region, Description FROM Artist");
                foreach (var a in artistData)
                {
                    if (!string.IsNullOrEmpty(a.Name) && !mergedArtists.ContainsKey(a.Name))
                        mergedArtists[a.Name] = a;
                }
            }
            catch { }
        }

        // 3. 获取 Songs 表中引用的 ArtistId → 需要保留的映射
        // 先读取旧 Artists 表的 Id 映射
        var oldIdToName = new Dictionary<int, string>();
        try
        {
            var idNameRows = await _database.QueryAsync<IdNameRow>("SELECT Id, Name FROM Artists");
            foreach (var r in idNameRows)
                oldIdToName[r.Id] = r.Name;
        }
        catch { }

        // 4. 重建 Artists 表
        await _database.ExecuteAsync("DROP TABLE IF EXISTS Artists");
        if (hasArtistTable)
            await _database.ExecuteAsync("DROP TABLE IF EXISTS Artist");

        await _database.CreateTableAsync<Artist>();

        // 5. 插入合并后的数据，构建 Name → 新Id 映射
        var nameToNewId = new Dictionary<string, int>();
        foreach (var kvp in mergedArtists)
        {
            var a = kvp.Value;
            var artist = new Artist
            {
                Name = a.Name,
                Cover = a.Cover,
                Gender = a.Gender,
                Birthday = a.Birthday,
                Region = a.Region,
                Description = a.Description,
            };
            await _database.InsertAsync(artist);
            nameToNewId[a.Name] = artist.Id;
        }

        // 6. 更新 Songs 表的 ArtistId
        // 对于每个旧 ArtistId，找到对应的 Name，再找到新 Id
        var oldIdToNewId = new Dictionary<int, int>();
        foreach (var kvp in oldIdToName)
        {
            if (nameToNewId.TryGetValue(kvp.Value, out var newId))
                oldIdToNewId[kvp.Key] = newId;
        }

        // 批量更新 Songs.ArtistId
        foreach (var mapping in oldIdToNewId)
        {
            if (mapping.Key != mapping.Value)
            {
                try
                {
                    await _database.ExecuteAsync("UPDATE Songs SET ArtistId = ? WHERE ArtistId = ?",
                        mapping.Value, mapping.Key);
                }
                catch { }
            }
        }

        // 7. 修复 ArtistId=0 的歌曲：尝试从文件元数据重新关联
        await FixOrphanedArtistIdsAsync(nameToNewId, ExtractArtistNameCallback);
    }

    /// <summary>
    /// 修复 ArtistId=0 的歌曲：通过回调提取艺术家名称重新关联
    /// </summary>
    private async Task FixOrphanedArtistIdsAsync(Dictionary<string, int> nameToNewId, Func<string, string?>? extractArtistName = null)
    {
        try
        {
            // 获取所有 ArtistId=0 的歌曲
            var orphanSongs = await _database.Table<Song>().Where(s => s.ArtistId == 0).ToListAsync();
            if (orphanSongs.Count == 0) return;
            if (extractArtistName == null) return;

            // 尝试从文件元数据提取艺术家名称
            foreach (var song in orphanSongs)
            {
                if (string.IsNullOrEmpty(song.FilePath) || !System.IO.File.Exists(song.FilePath))
                    continue;

                try
                {
                    var artistName = extractArtistName(song.FilePath);

                    if (string.IsNullOrEmpty(artistName)) continue;

                    // 在 nameToNewId 中查找或创建
                    if (!nameToNewId.TryGetValue(artistName, out var artistId))
                    {
                        var newArtist = new Artist { Name = artistName };
                        await _database.InsertAsync(newArtist);
                        artistId = newArtist.Id;
                        nameToNewId[artistName] = artistId;
                    }

                    song.ArtistId = artistId;
                    await _database.UpdateAsync(song);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// 将现有歌曲的单 ArtistId 迁移到多对多 SongArtists 表。
    /// 对于 ArtistId > 0 且 SongArtists 中尚无记录的歌曲，创建一条 SongArtist 记录。
    /// </summary>
    private async Task MigrateToMultiArtistAsync()
    {
        try
        {
            // 检查是否已有 SongArtist 数据（避免重复迁移）
            var existingCount = await _database.Table<SongArtist>().CountAsync();
            if (existingCount > 0) return;

            // 查找所有有艺术家关联的歌曲
            var songs = await _database.Table<Song>().Where(s => s.ArtistId > 0).ToListAsync();
            if (songs.Count == 0) return;

            var entries = songs.Select(s => new SongArtist
            {
                SongId = s.Id,
                ArtistId = s.ArtistId
            }).ToList();

            await _database.InsertAllAsync(entries);
            Log.Debug("MusicDatabase", $"[CatClaw] 多艺术家迁移完成，为 {entries.Count} 首歌曲创建了 SongArtist 关联");
        }
        catch (Exception ex)
        {
            Log.Debug("MusicDatabase", $"[CatClaw] 多艺术家迁移失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 将历史遗留的合并艺术家名（如 "国风堂/哦漏"）拆分为多个独立艺术家。
    /// 流程：
    /// 1. 查找名称含 " / " 的艺术家
    /// 2. 拆分名称 → 为每个子名称查找或创建独立艺术家
    /// 3. 更新 Song.ArtistId → 指向第一个子艺术家
    /// 4. 更新 SongArtists → 将合并艺术家 ID 替换为各子艺术家 ID
    /// 5. 删除旧的合并艺术家记录
    /// </summary>
    private async Task SplitCombinedArtistsAsync()
    {
        try
        {
            // 查找所有名称含 "/" 或 "／" 的艺术家（历史合并艺术家）
            var allArtists = await _database.Table<Artist>().ToListAsync();
            var combinedArtists = allArtists
                .Where(a => a.Name.Contains(" / ") || a.Name.Contains("/") || a.Name.Contains("／"))
                .ToList();

            if (combinedArtists.Count == 0) return;

            Log.Debug("MusicDatabase", $"[CatClaw] 发现 {combinedArtists.Count} 个需要拆分的合并艺术家");

            await _database.RunInTransactionAsync(tran =>
            {
                foreach (var combined in combinedArtists)
                {
                    try
                    {
                        // 规范化：将全角斜杠替换为半角，便于统一拆分
                        var normalizedName = combined.Name.Replace('／', '/');
                        // 拆分名称：优先用 SplitArtistNames，如果没拆开则按 '/' 强拆
                        var names = CatClawMusic.Core.Services.MusicUtility.SplitArtistNames(normalizedName);
                        if (names.Count <= 1 && normalizedName.Contains('/'))
                        {
                            names = normalizedName.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Where(p => !string.IsNullOrEmpty(p))
                                .ToList();
                        }
                        if (names.Count <= 1) continue;

                        // 为每个子名称查找或创建独立艺术家
                        var individualIds = new List<int>();
                        var allArtistsSnapshot = tran.Query<Artist>("SELECT * FROM Artists").ToList();
                        foreach (var name in names)
                        {
                            var existing = allArtistsSnapshot.FirstOrDefault(a =>
                                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase) &&
                                a.Id != combined.Id);

                            if (existing != null)
                            {
                                individualIds.Add(existing.Id);
                            }
                            else
                            {
                                var newArtist = new Artist { Name = name };
                                tran.Insert(newArtist);
                                allArtistsSnapshot.Add(newArtist);
                                individualIds.Add(newArtist.Id);
                            }
                        }

                        // 更新 Song.ArtistId → 指向第一个子艺术家
                        if (individualIds.Count > 0)
                        {
                            tran.Execute("UPDATE Songs SET ArtistId = ? WHERE ArtistId = ?", individualIds[0], combined.Id);
                        }

                        // 更新 SongArtists → 将合并艺术家 ID 替换为子艺术家 ID
                        var songArtistRows = tran.Query<SongArtist>(
                            "SELECT * FROM SongArtists WHERE ArtistId = ?", combined.Id);
                        foreach (var sa in songArtistRows)
                        {
                            tran.Execute("DELETE FROM SongArtists WHERE Id = ?", sa.Id);
                            foreach (var id in individualIds)
                            {
                                try
                                {
                                    tran.Insert(new SongArtist { SongId = sa.SongId, ArtistId = id });
                                }
                                catch { }
                            }
                        }

                        // 删除旧的合并艺术家
                        tran.Execute("DELETE FROM Artists WHERE Id = ?", combined.Id);

                        Log.Debug("MusicDatabase", 
                            $"[CatClaw] 拆分艺术家 \"{combined.Name}\" → [{string.Join(", ", names)}]");
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("MusicDatabase", 
                            $"[CatClaw] 拆分艺术家 \"{combined.Name}\" 失败: {ex.Message}");
                    }
                }
            });

            // 清理可能产生的孤立 SongArtists 记录
            await CleanupOrphanedPlayHistoryAndFavoritesAsync();

            Log.Debug("MusicDatabase", "[CatClaw] 合并艺术家拆分迁移完成");
        }
        catch (Exception ex)
        {
            Log.Debug("MusicDatabase", $"[CatClaw] 合并艺术家拆分迁移失败: {ex.Message}");
        }
    }

    /// <summary>用于恢复时读取艺术家行的辅助类</summary>
    private class ArtistRecoveryRow
    {
        public string Name { get; set; } = "";
        public string? Cover { get; set; }
        public string? Gender { get; set; }
        public string? Birthday { get; set; }
        public string? Region { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>用于读取 Id-Name 映射的辅助类</summary>
    private class IdNameRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 迁移旧版 Playlist 表到新版 Playlists 表
    /// </summary>
    private async Task MigratePlaylistsTableAsync()
    {
        try
        {
            var hasOldTable = await TableExistsAsync("Playlist");
            var hasNewTable = await TableExistsAsync("Playlists");

            if (hasOldTable)
            {
                if (hasNewTable)
                    await _database.ExecuteAsync("DROP TABLE Playlists");
                await _database.ExecuteAsync("ALTER TABLE Playlist RENAME TO Playlists");
                hasNewTable = true;
            }

            if (!hasNewTable) return;

            var cols = await _database.QueryAsync<TableColumn>("PRAGMA table_info(Playlists)");
            if (cols.Any(c => c.pk > 0)) return;

            await _database.ExecuteAsync("ALTER TABLE Playlists RENAME TO Playlists_old");
            await _database.CreateTableAsync<Playlist>();
            await _database.ExecuteAsync(
                "INSERT INTO Playlists(Id, Name, CreatedAt, UpdatedAt, SongCount, IsSystem) " +
                "SELECT Id, Name, CreatedAt, UpdatedAt, SongCount, IsSystem FROM Playlists_old");
            await _database.ExecuteAsync("DROP TABLE Playlists_old");
        }
        catch { }
    }

    /// <summary>
    /// 迁移 PlaylistSongs 表结构，确保包含自增主键
    /// </summary>
    private async Task MigratePlaylistSongsTableAsync()
    {
        try
        {
            var exists = await TableExistsAsync("PlaylistSongs");
            if (!exists) return;

            var columns = await _database.QueryAsync<TableColumn>("PRAGMA table_info(PlaylistSongs)");
            if (columns.Any(c => c.pk > 0)) return;

            await _database.ExecuteAsync("ALTER TABLE PlaylistSongs RENAME TO PlaylistSongs_old");
            await _database.CreateTableAsync<PlaylistSong>();
            await _database.ExecuteAsync(
                "INSERT INTO PlaylistSongs(PlaylistId, SongId, Position) " +
                "SELECT PlaylistId, SongId, Position FROM PlaylistSongs_old");
            await _database.ExecuteAsync("DROP TABLE PlaylistSongs_old");
        }
        catch { }
    }

    /// <summary>
    /// PlaylistSongs 约束迁移（一次性，MigrationFlag 门控）：
    /// 历史版本对歌单条目的读取路径存在并发竞态且写放大严重，本次为表补齐
    /// (PlaylistId, SongId) 唯一索引与 (PlaylistId, Position) 查询索引。
    /// 建唯一索引前必须先清理历史重复行（旧版本竞态可写入同歌单同歌曲多条记录），
    /// 去重保留 Position 最小（其次 Id 最小）的一行，并将 Position 归一化为 0..n-1 密集序列。
    /// </summary>
    private async Task MigratePlaylistSongConstraintsAsync()
    {
        const string flag = "playlist_song_constraints_v1";
        try
        {
            if (await IsMigrationDoneAsync(flag)) return;

            var exists = await TableExistsAsync("PlaylistSongs");
            if (!exists)
            {
                await MarkMigrationDoneAsync(flag);
                return;
            }

            await _database.RunInTransactionAsync(tran =>
            {
                // 1. 去重：每个 (PlaylistId, SongId) 保留 Position 最小（其次 Id 最小）的一行
                tran.Execute(
                    "DELETE FROM PlaylistSongs WHERE EXISTS (" +
                    "  SELECT 1 FROM PlaylistSongs p2 WHERE" +
                    "    p2.PlaylistId = PlaylistSongs.PlaylistId AND p2.SongId = PlaylistSongs.SongId AND" +
                    "    (p2.Position < PlaylistSongs.Position OR" +
                    "     (p2.Position = PlaylistSongs.Position AND p2.Id < PlaylistSongs.Id)))");

                // 2. Position 归一化：按 (旧 Position, Id) 排序映射为 0..n-1 密集序列
                tran.Execute(
                    "UPDATE PlaylistSongs SET Position = (" +
                    "  SELECT COUNT(*) FROM PlaylistSongs p2 WHERE" +
                    "    p2.PlaylistId = PlaylistSongs.PlaylistId AND" +
                    "    (p2.Position < PlaylistSongs.Position OR" +
                    "     (p2.Position = PlaylistSongs.Position AND p2.Id <= PlaylistSongs.Id))) - 1");

                // 3. 唯一索引（防重复插入）+ 排序查询索引（歌单读取/重排热点路径）
                tran.Execute(
                    "CREATE UNIQUE INDEX IF NOT EXISTS ux_playlist_songs_playlist_song " +
                    "ON PlaylistSongs(PlaylistId, SongId)");
                tran.Execute(
                    "CREATE INDEX IF NOT EXISTS idx_playlist_songs_playlist_position " +
                    "ON PlaylistSongs(PlaylistId, Position)");
            });

            await MarkMigrationDoneAsync(flag);
        }
        catch
        {
            // 失败不落标记，下次启动重试（索引已在事务内创建则幂等跳过）
        }
    }

    /// <summary>
    /// 迁移 PlayHistory 表：添加自增主键 Id 列
    /// </summary>
    private async Task MigratePlayHistoryTableAsync()
    {
        try
        {
            var exists = await TableExistsAsync("PlayHistory");
            if (!exists)
            {
                // PlayHistory 不存在但 PlayHistory_old 可能残留 → 恢复
                var oldExists = await TableExistsAsync("PlayHistory_old");
                if (oldExists)
                {
                    await _database.ExecuteAsync("ALTER TABLE PlayHistory_old RENAME TO PlayHistory");
                }
                return;
            }

            var columns = await _database.QueryAsync<TableColumn>("PRAGMA table_info(PlayHistory)");
            if (columns.Any(c => c.pk > 0))
            {
                // 表结构已正确，清理残留旧表
                try { await _database.ExecuteAsync("DROP TABLE IF EXISTS PlayHistory_old"); } catch { }
                return;
            }

            await _database.ExecuteAsync("ALTER TABLE PlayHistory RENAME TO PlayHistory_old");
            await _database.CreateTableAsync<PlayHistory>();
            await _database.ExecuteAsync(
                "INSERT INTO PlayHistory(SongId, PlayedAt, PlayCount) " +
                "SELECT SongId, PlayedAt, PlayCount FROM PlayHistory_old");
            await _database.ExecuteAsync("DROP TABLE PlayHistory_old");
        }
        catch
        {
            // 迁移失败时尝试恢复旧表数据，而非直接丢弃
            try
            {
                var oldExists = await TableExistsAsync("PlayHistory_old");
                var newExists = await TableExistsAsync("PlayHistory");

                if (oldExists && !newExists)
                {
                    await _database.ExecuteAsync("ALTER TABLE PlayHistory_old RENAME TO PlayHistory");
                }
                else if (oldExists && newExists)
                {
                    try
                    {
                        await _database.ExecuteAsync(
                            "INSERT OR IGNORE INTO PlayHistory(SongId, PlayedAt, PlayCount) " +
                            "SELECT SongId, PlayedAt, PlayCount FROM PlayHistory_old");
                    }
                    catch { }
                    try { await _database.ExecuteAsync("DROP TABLE PlayHistory_old"); } catch { }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// 检查指定表是否存在于数据库中
    /// </summary>
    private async Task<bool> TableExistsAsync(string tableName)
    {
        var count = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", tableName);
        return count > 0;
    }

    /// <summary>
    /// SQLite PRAGMA table_info 返回的列信息
    /// </summary>
    private class TableColumn
    {
        /// <summary>
        /// 列序号
        /// </summary>
        public int cid { get; set; }

        /// <summary>
        /// 列名
        /// </summary>
        public string name { get; set; } = string.Empty;

        /// <summary>
        /// 列类型
        /// </summary>
        public string type { get; set; } = string.Empty;

        /// <summary>
        /// 是否非空约束
        /// </summary>
        public int notnull { get; set; }

        /// <summary>
        /// 默认值
        /// </summary>
        public string dflt_value { get; set; } = string.Empty;

        /// <summary>
        /// 是否主键
        /// </summary>
        public int pk { get; set; }
    }

    // ═══════════ ChatMessageRecord ═══════════
}
