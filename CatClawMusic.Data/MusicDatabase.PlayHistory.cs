using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using SQLite;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Data;

/// <summary>SQLite 数据库操作层 —— partial 分域文件之一。</summary>
public partial class MusicDatabase
{
    public async Task RecordPlayAsync(int songId, long durationMs = 0)
    {
        if (songId <= 0) return;
        try
        {
            await EnsureMaintenanceCompletedAsync();
            // 串行化，避免多处并发 fire-and-forget 调用导致同一 SongId 被插入多条记录
            await _playHistoryLock.WaitAsync();
            try
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var existing = await _database.Table<PlayHistory>()
                    .Where(h => h.SongId == songId)
                    .FirstOrDefaultAsync();
                if (existing != null)
                {
                    existing.PlayedAt = now;
                    existing.PlayCount++;
                    await _database.UpdateAsync(existing);
                }
                else
                {
                    await _database.InsertAsync(new PlayHistory { SongId = songId, PlayedAt = now });
                }

                // 仅累加「播放次数」。逐次聆听时长日志（PlaySession）由 LogListenSessionAsync /
                // UpdateListenSessionAsync 单独管理，避免每次 flush 都额外插入一行导致统计被放大。
                await TrimHistoryAsync(200);
            }
            finally
            {
                _playHistoryLock.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Debug("MusicDatabase", $"[CatClaw] RecordPlayAsync 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 写入一条播放会话日志（仅用于听歌趋势/时段分布/累计时长等逐次统计），不影响 PlayHistory.PlayCount。
    /// 每开始一次聆听调用一次，后续由 <see cref="UpdateListenSessionAsync"/> 把累计时长写回同一行。
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    /// <param name="durationMs">本次聆听时长（毫秒），首次建行可传 0，由后续 flush 累加</param>
    /// <returns>新建会话行的自增 Id，失败返回 -1</returns>
    public async Task<int> LogListenSessionAsync(int songId, long durationMs = 0)
    {
        if (songId <= 0) return -1;
        try
        {
            await EnsureMaintenanceCompletedAsync();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var session = new PlaySession { SongId = songId, PlayedAt = now, DurationMs = Math.Max(0, durationMs) };
            await _database.InsertAsync(session);
            await IncrementDailyStatsAsync(songId, now, 1, Math.Max(0, durationMs));
            await TrimPlaySessionAsync(2000);
            return session.Id;
        }
        catch (Exception ex)
        {
            Log.Debug("MusicDatabase", $"[CatClaw] LogListenSessionAsync 失败: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// 更新某条播放会话的聆听时长与发生时间，用于把一次聆听的累计时长写回同一行，
    /// 避免每 30 秒 flush 都新增一行，导致统计页「总播放次数」「听歌趋势」被放大。
    /// </summary>
    /// <param name="sessionId">由 <see cref="LogListenSessionAsync"/> 返回的自增 Id</param>
    /// <param name="durationMs">本次聆听累计时长（毫秒）</param>
    public async Task UpdateListenSessionAsync(int sessionId, long durationMs)
    {
        if (sessionId <= 0) return;
        try
        {
            await EnsureMaintenanceCompletedAsync();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var old = await _database.Table<PlaySession>().Where(s => s.Id == sessionId).FirstOrDefaultAsync();
            var newDuration = Math.Max(0, durationMs);
            await _database.ExecuteAsync(
                "UPDATE PlaySession SET DurationMs = ?, PlayedAt = ? WHERE Id = ?",
                newDuration, now, sessionId);
            if (old != null)
            {
                var delta = newDuration - Math.Max(0, old.DurationMs);
                if (delta != 0)
                    await IncrementDailyStatsAsync(old.SongId, old.PlayedAt, 0, delta);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("MusicDatabase", $"[CatClaw] UpdateListenSessionAsync 失败: {ex.Message}");
        }
    }

    /// <summary>增量更新按天/小时/歌曲汇总统计。</summary>
    private async Task IncrementDailyStatsAsync(int songId, long playedAtUnix, int playDelta, long durationDelta)
    {
        try
        {
            var local = DateTimeOffset.FromUnixTimeSeconds(playedAtUnix).LocalDateTime;
            var date = local.ToString("yyyy-MM-dd");
            var hour = local.Hour;
            var isNight = hour >= 21 || hour < 5;
            var nightDelta = isNight ? playDelta : 0;

            await _database.ExecuteAsync(
                "INSERT INTO DailyPlayStat(Date, PlayCount, TotalDurationMs, NightPlayCount) VALUES (?, ?, ?, ?) " +
                "ON CONFLICT(Date) DO UPDATE SET " +
                "PlayCount = PlayCount + excluded.PlayCount, " +
                "TotalDurationMs = TotalDurationMs + excluded.TotalDurationMs, " +
                "NightPlayCount = NightPlayCount + excluded.NightPlayCount",
                date, playDelta, durationDelta, nightDelta);

            var dateHour = $"{date} {hour:D2}";
            await _database.ExecuteAsync(
                "INSERT INTO HourlyPlayStat(DateHour, Date, Hour, PlayCount, TotalDurationMs) VALUES (?, ?, ?, ?, ?) " +
                "ON CONFLICT(DateHour) DO UPDATE SET " +
                "PlayCount = PlayCount + excluded.PlayCount, " +
                "TotalDurationMs = TotalDurationMs + excluded.TotalDurationMs",
                dateHour, date, hour, playDelta, durationDelta);

            var dateSong = $"{date}|{songId}";
            await _database.ExecuteAsync(
                "INSERT INTO DailySongStat(DateSong, Date, SongId, PlayCount, TotalDurationMs) VALUES (?, ?, ?, ?, ?) " +
                "ON CONFLICT(DateSong) DO UPDATE SET " +
                "PlayCount = PlayCount + excluded.PlayCount, " +
                "TotalDurationMs = TotalDurationMs + excluded.TotalDurationMs",
                dateSong, date, songId, playDelta, durationDelta);
        }
        catch (Exception ex)
        {
            Log.Debug("MusicDatabase", $"[CatClaw] IncrementDailyStatsAsync 失败: {ex.Message}");
        }
    }

    /// <summary>获取指定本地日期范围内的按天汇总统计。</summary>
    public async Task<List<DailyPlayStat>> GetDailyPlayStatsAsync(string startDate, string endDate)
    {
        await EnsureMaintenanceCompletedAsync();
        if (string.IsNullOrEmpty(startDate) && string.IsNullOrEmpty(endDate))
            return await _database.Table<DailyPlayStat>().ToListAsync();
        return await _database.QueryAsync<DailyPlayStat>(
            "SELECT * FROM DailyPlayStat WHERE Date >= ? AND Date <= ? ORDER BY Date",
            startDate, endDate);
    }

    /// <summary>获取指定本地日期范围内的小时汇总统计。</summary>
    public async Task<List<HourlyPlayStat>> GetHourlyPlayStatsAsync(string startDate, string endDate)
    {
        await EnsureMaintenanceCompletedAsync();
        if (string.IsNullOrEmpty(startDate) && string.IsNullOrEmpty(endDate))
            return await _database.Table<HourlyPlayStat>().ToListAsync();
        return await _database.QueryAsync<HourlyPlayStat>(
            "SELECT * FROM HourlyPlayStat WHERE Date >= ? AND Date <= ? ORDER BY Date, Hour",
            startDate, endDate);
    }

    /// <summary>获取指定本地日期范围内的按天+歌曲汇总统计。</summary>
    public async Task<List<DailySongStat>> GetDailySongStatsAsync(string startDate, string endDate)
    {
        await EnsureMaintenanceCompletedAsync();
        if (string.IsNullOrEmpty(startDate) && string.IsNullOrEmpty(endDate))
            return await _database.Table<DailySongStat>().ToListAsync();
        return await _database.QueryAsync<DailySongStat>(
            "SELECT * FROM DailySongStat WHERE Date >= ? AND Date <= ? ORDER BY Date",
            startDate, endDate);
    }

    /// <summary>如果汇总表为空但存在历史会话，则从 PlaySession 重建汇总，兼容旧版本升级。</summary>
    public async Task RebuildDailyStatsIfNeededAsync()
    {
        try
        {
            await EnsureMaintenanceCompletedAsync();
            var sessionCount = await _database.Table<PlaySession>().CountAsync();
            if (sessionCount == 0) return;
            var summaryPlays = await _database.ExecuteScalarAsync<int>(
                "SELECT COALESCE(SUM(PlayCount), 0) FROM DailyPlayStat");
            if (summaryPlays >= sessionCount) return;

            await _database.ExecuteAsync("DELETE FROM DailyPlayStat");
            await _database.ExecuteAsync("DELETE FROM HourlyPlayStat");
            await _database.ExecuteAsync("DELETE FROM DailySongStat");

            var sessions = await _database.Table<PlaySession>().ToListAsync();
            foreach (var s in sessions)
                await IncrementDailyStatsAsync(s.SongId, s.PlayedAt, 1, s.DurationMs);
        }
        catch (Exception ex)
        {
            Log.Debug("MusicDatabase", $"[CatClaw] RebuildDailyStatsIfNeededAsync 失败: {ex.Message}");
        }
    }



    /// <summary>
    /// 一次性校准历史播放计数：旧版本每 30 秒 flush 都会给 PlayHistory.PlayCount +1 并多插一条 PlaySession，
    /// 导致发现页「最多播放」与统计页「总播放次数」被放大。
    /// 本方法按时间把同一首歌的 PlaySession 聚成「真实聆听次数」簇（簇内行间隔 ≤ 阈值，簇间间隔 ≥ 半首歌），
    /// 将 PlayHistory.PlayCount 校正为簇数，并删除簇内冗余行（每簇仅留最早一行），使两套计数重新一致。
    /// 对修复后的干净数据幂等（每聆听本就一行，簇数 = 聆听数）。
    /// </summary>
    /// <returns>被修正 PlayCount 的歌曲数量（0 表示无需修正）</returns>
    public async Task<int> RecalibratePlayCountsAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        var sessions = await _database.Table<PlaySession>().OrderBy(s => s.SongId).ThenBy(s => s.PlayedAt).ToListAsync();
        if (sessions.Count == 0) return 0;

        var durations = (await _database.Table<Song>().ToListAsync())
            .ToDictionary(s => s.Id, s => Math.Max(1L, s.Duration));

        int changed = 0;
        foreach (var grp in sessions.GroupBy(s => s.SongId))
        {
            var songDurMs = durations.TryGetValue(grp.Key, out var d) ? d : 180_000L;
            // 同一聆听内的逐次日志间隔 ≤ 30 秒；两聆听之间至少隔整首歌（或更久）。
            // 用半首歌时长作阈值：既能区分连续重播（间隔≈整首歌 > 半首歌），又不会把一次聆听内的多行拆开。
            var thresholdMs = Math.Max(120_000L, songDurMs / 2);

            var rows = grp.OrderBy(s => s.PlayedAt).ToList();
            var clusters = new List<List<PlaySession>>();
            List<PlaySession>? cur = null;
            long prev = 0;
            foreach (var r in rows)
            {
                if (cur == null || (r.PlayedAt - prev) * 1000 > thresholdMs)
                {
                    cur = new List<PlaySession>();
                    clusters.Add(cur);
                }
                cur.Add(r);
                prev = r.PlayedAt;
            }

            // 删除簇内冗余行，仅保留最早一行（其 DurationMs 通常≈整首歌，最具代表性）
            foreach (var c in clusters)
                for (int i = 1; i < c.Count; i++)
                    await _database.DeleteAsync(c[i]);

            var listens = clusters.Count;
            if (listens <= 0) continue; // 仅有 PlayHistory 无会话时不动，避免误清零

            var maxPlayed = rows.Max(r => r.PlayedAt);
            // 预存为局部 int：sqlite-net 表达式编译器无法直接求值 IGrouping 捕获变量的 Key，
            // 内联 grp.Key 会抛出 NotSupportedException（Cannot store type: PlaySession）
            var songId = grp.Key;
            var hist = await _database.Table<PlayHistory>().Where(h => h.SongId == songId).FirstOrDefaultAsync();
            if (hist != null)
            {
                if (hist.PlayCount != listens)
                {
                    hist.PlayCount = listens;
                    changed++;
                }
                if (maxPlayed > hist.PlayedAt) hist.PlayedAt = maxPlayed;
                await _database.UpdateAsync(hist);
            }
            else
            {
                await _database.InsertAsync(new PlayHistory { SongId = grp.Key, PlayedAt = maxPlayed, PlayCount = listens });
                changed++;
            }
        }
        return changed;
    }

    /// <summary>
    /// 批量增加播放次数：对每个 SongId 一次性 +count，避免 N 次串行 await。
    /// 用于备份恢复等需要一次性恢复大量播放历史的场景。
    /// </summary>
    /// <param name="entries">[(songId, playCount), ...] 已合并后的计数</param>
    public async Task RecordPlayBatchAsync(List<(int SongId, int PlayCount)> entries)
    {
        if (entries == null || entries.Count == 0) return;
        await EnsureMaintenanceCompletedAsync();
        await _playHistoryLock.WaitAsync();
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await _database.RunInTransactionAsync(tran =>
            {
                foreach (var (songId, count) in entries)
                {
                    if (songId <= 0 || count <= 0) continue;
                    // 直接 UPDATE，命中则 PlayCount += count；未命中则 INSERT
                    var affected = tran.Execute(
                        "UPDATE PlayHistory SET PlayCount = PlayCount + ?, PlayedAt = ? WHERE SongId = ?",
                        count, now, songId);
                    if (affected == 0)
                    {
                        tran.Execute(
                            "INSERT INTO PlayHistory(SongId, PlayedAt, PlayCount) VALUES (?, ?, ?)",
                            songId, now, count);
                    }
                }
            });
            await TrimHistoryAsync(200);
        }
        catch (Exception ex)
        {
            Log.Debug("MusicDatabase", $"[CatClaw] RecordPlayBatchAsync 失败: {ex.Message}");
        }
        finally
        {
            _playHistoryLock.Release();
        }
    }

    /// <summary>
    /// 裁剪 PlaySession 表，仅保留最近指定数量的会话记录，避免无限增长。
    /// </summary>
    /// <param name="keepCount">保留记录数量上限</param>
    private async Task TrimPlaySessionAsync(int keepCount)
    {
        try
        {
            var count = await _database.Table<PlaySession>().CountAsync();
            if (count <= keepCount) return;
            var removeCount = count - keepCount;
            var removing = await _database.QueryAsync<PlaySession>(
                "SELECT * FROM PlaySession ORDER BY PlayedAt ASC LIMIT ?", removeCount);
            foreach (var row in removing)
            {
                await IncrementDailyStatsAsync(row.SongId, row.PlayedAt, -1, -row.DurationMs);
            }
            await _database.ExecuteAsync(
                "DELETE FROM PlaySession WHERE Id IN (SELECT Id FROM PlaySession ORDER BY PlayedAt ASC LIMIT ?)",
                removeCount);
        }
        catch { }
    }

    /// <summary>
    /// 裁剪播放历史，仅保留指定数量记录
    /// </summary>
    private async Task TrimHistoryAsync(int keepCount)
    {
        try
        {
            var count = await _database.Table<PlayHistory>().CountAsync();
            if (count <= keepCount) return;
            // 按主键 Id 删除最旧的记录，避免按 SongId 误删多条
            await _database.ExecuteAsync(
                "DELETE FROM PlayHistory WHERE Id IN (SELECT Id FROM PlayHistory ORDER BY PlayedAt ASC LIMIT ?)",
                count - keepCount);
        }
        catch { }
    }

    /// <summary>
    /// 合并 PlayHistory 中同一 SongId 的多条重复记录：保留 PlayedAt 最新的一条，
    /// 将其 PlayCount 累加为总和，其余重复行删除。用于修复历史竞态写入产生的重复数据。
    /// </summary>
    private async Task ConsolidatePlayHistoryAsync()
    {
        try
        {
            var rows = await _database.Table<PlayHistory>().ToListAsync();
            var dupGroups = rows.GroupBy(h => h.SongId).Where(g => g.Count() > 1).ToList();
            foreach (var g in dupGroups)
            {
                var keep = g.OrderByDescending(h => h.PlayedAt).First();
                var total = g.Sum(h => h.PlayCount);
                var latest = g.Max(h => h.PlayedAt);
                foreach (var h in g.Where(h => h.Id != keep.Id))
                    await _database.DeleteAsync(h);
                keep.PlayCount = total;
                keep.PlayedAt = latest;
                await _database.UpdateAsync(keep);
            }
        }
        catch { }
    }

    /// <summary>
    /// 获取最近的播放历史记录
    /// </summary>
    /// <param name="limit">最大返回数量</param>
    /// <returns>播放历史列表</returns>
    public Task<List<PlayHistory>> GetRecentPlaysAsync(int limit = 200) =>
        _database.Table<PlayHistory>().OrderByDescending(h => h.PlayedAt).Take(limit).ToListAsync();

    /// <summary>获取最近播放的歌曲（含艺术家/专辑名）</summary>
    /// <returns>按播放时间降序排列的歌曲列表</returns>
    public async Task<List<Song>> GetRecentSongsAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        var history = await _database.Table<PlayHistory>().OrderByDescending(h => h.PlayedAt).Take(200).ToListAsync();
        if (history.Count == 0) return new List<Song>();

        var songIds = history.Select(h => h.SongId).ToHashSet();
        // IN 查询只取命中歌曲，而非整表加载后内存过滤
        var songs = await _database.Table<Song>().Where(s => songIds.Contains(s.Id)).ToListAsync();
        if (songs.Count == 0) return new List<Song>();

        // 只过滤孤立记录，不删除（歌曲可能因权限过期暂时不可见，重新扫描后可恢复）
        var foundIds = songs.Select(s => s.Id).ToHashSet();
        var validHistory = history.Where(h => foundIds.Contains(h.SongId)).ToList();

        // 只取用到的艺术家/专辑，而非整表
        var neededArtistIds = songs.Select(s => s.ArtistId).Distinct().ToList();
        var neededAlbumIds = songs.Select(s => s.AlbumId).Distinct().ToList();
        var artists = await _database.Table<Artist>().Where(a => neededArtistIds.Contains(a.Id)).ToListAsync();
        var albums = await _database.Table<Album>().Where(a => neededAlbumIds.Contains(a.Id)).ToListAsync();
        var artistDict = SafeToDict(artists, a => a.Id, a => a.Name);
        var albumDict = SafeToDict(albums, a => a.Id, a => a.Title);

        foreach (var s in songs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
            s.PlayCount = validHistory.Where(h => h.SongId == s.Id).Sum(h => h.PlayCount);
        }

        var playTimeDict = validHistory.GroupBy(h => h.SongId).ToDictionary(g => g.Key, g => g.Max(h => h.PlayedAt));

        // 填充多艺术家
        var allArtistsDict2 = await GetAllArtistsForSongsAsync(songs.Select(s => s.Id));
        foreach (var s in songs)
            s.AllArtists = allArtistsDict2.TryGetValue(s.Id, out var aa) ? aa : s.Artist;

        return songs.OrderByDescending(s => playTimeDict.TryGetValue(s.Id, out var t) ? t : 0).ToList();
    }

    /// <summary>获取播放次数最多的歌曲（含艺术家/专辑名和播放计数）</summary>
    /// <param name="limit">最大返回数量</param>
    /// <returns>按播放次数降序排列的歌曲列表</returns>
    public async Task<List<Song>> GetTopPlayedSongsAsync(int limit = 50)
    {
        await EnsureMaintenanceCompletedAsync().ConfigureAwait(false);
        // SQL 端聚合：GROUP BY + ORDER BY + LIMIT，只取回 Top-N 行，
        // 避免把整张 PlayHistory（可能上万行）拉到内存做客户端 GroupBy（主线程对象分配风暴）。
        var topRows = await _database.QueryAsync<PlayCountTotal>(
            "SELECT SongId, SUM(PlayCount) AS Total FROM PlayHistory GROUP BY SongId ORDER BY Total DESC LIMIT ?",
            limit).ConfigureAwait(false);
        if (topRows.Count == 0) return new List<Song>();

        // 只取这批歌曲（IN 查询），而非整张 Songs 表
        var songIds = topRows.Select(r => r.SongId).ToList();
        var songs = await _database.Table<Song>().Where(s => songIds.Contains(s.Id)).ToListAsync().ConfigureAwait(false);
        if (songs.Count == 0) return new List<Song>();

        // 播放次数字典直接来自聚合结果，消除原 O(songs × history) 内层循环
        var playCountDict = topRows.ToDictionary(r => r.SongId, r => r.Total);

        // 只取用到的艺术家/专辑，而非整表
        var neededArtistIds = songs.Select(s => s.ArtistId).Distinct().ToList();
        var neededAlbumIds = songs.Select(s => s.AlbumId).Distinct().ToList();
        var artists = await _database.Table<Artist>().Where(a => neededArtistIds.Contains(a.Id)).ToListAsync().ConfigureAwait(false);
        var albums = await _database.Table<Album>().Where(a => neededAlbumIds.Contains(a.Id)).ToListAsync().ConfigureAwait(false);
        var artistDict = SafeToDict(artists, a => a.Id, a => a.Name);
        var albumDict = SafeToDict(albums, a => a.Id, a => a.Title);

        foreach (var s in songs)
        {
            s.Artist = artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家";
            s.Album = albumDict.TryGetValue(s.AlbumId, out var al) ? al : "未知专辑";
            s.PlayCount = playCountDict.TryGetValue(s.Id, out var c) ? c : 0;
        }

        // 填充多艺术家
        var allArtistsDict3 = await GetAllArtistsForSongsAsync(songs.Select(s => s.Id)).ConfigureAwait(false);
        foreach (var s in songs)
            s.AllArtists = allArtistsDict3.TryGetValue(s.Id, out var aa) ? aa : s.Artist;

        return songs.OrderByDescending(s => playCountDict.TryGetValue(s.Id, out var c) ? c : 0).ToList();
    }

    /// <summary>获取播放次数最多的艺术家（按 PlayHistory 聚合，合并多艺术家分隔）。
    /// 返回元组列表：(艺术家名, 总播放次数)。</summary>
    /// <param name="limit">最大返回数量</param>
    public async Task<List<(string Artist, int PlayCount)>> GetTopPlayedArtistsAsync(int limit = 10)
    {
        await EnsureMaintenanceCompletedAsync().ConfigureAwait(false);
        // SQL 端聚合：按 SongId 聚合 PlayHistory，只返回 (SongId, Total) 行，
        // 避免把整张 PlayHistory（可能上万行）拉到内存做客户端 GroupBy（主线程对象分配风暴）。
        var songPlayRows = await _database.QueryAsync<PlayCountTotal>(
            "SELECT SongId, SUM(PlayCount) AS Total FROM PlayHistory GROUP BY SongId").ConfigureAwait(false);
        if (songPlayRows.Count == 0) return new List<(string, int)>();

        // 只取有播放记录的歌曲（IN 查询），而非整张 Songs 表
        var songIds = songPlayRows.Select(r => r.SongId).ToList();
        var allSongs = await _database.Table<Song>().Where(s => songIds.Contains(s.Id)).ToListAsync().ConfigureAwait(false);
        if (allSongs.Count == 0) return new List<(string, int)>();

        // 只取用到的主艺术家，而非整表
        var neededArtistIds = allSongs.Select(s => s.ArtistId).Distinct().ToList();
        var artists = await _database.Table<Artist>().Where(a => neededArtistIds.Contains(a.Id)).ToListAsync().ConfigureAwait(false);
        var artistDict = SafeToDict(artists, a => a.Id, a => a.Name);

        // 多艺术家映射：SongId → " / " 分隔的全部艺术家名
        var allArtistsDict = await GetAllArtistsForSongsAsync(allSongs.Select(s => s.Id)).ConfigureAwait(false);

        // 播放次数字典直接来自 SQL 聚合结果，消除原对 history 的客户端 GroupBy
        var songPlayDict = songPlayRows.ToDictionary(r => r.SongId, r => r.Total);

        // 按艺术家聚合：用 AllArtists（含合作艺术家），以 " / " 拆分后单独计入
        var artistPlayDict = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in allSongs)
        {
            if (!songPlayDict.TryGetValue(s.Id, out var pc) || pc <= 0) continue;
            var names = allArtistsDict.TryGetValue(s.Id, out var aa) && !string.IsNullOrWhiteSpace(aa)
                ? aa
                : (artistDict.TryGetValue(s.ArtistId, out var an) ? an : "未知艺术家");
            foreach (var n in names.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                artistPlayDict.TryGetValue(n, out var cur);
                artistPlayDict[n] = cur + pc;
            }
        }

        return artistPlayDict.OrderByDescending(kv => kv.Value)
                             .Take(limit)
                             .Select(kv => (kv.Key, kv.Value))
                             .ToList();
    }

    /// <summary>
    /// 获取指定时间范围内的播放会话日志，用于趋势/时段分布/连续听歌等逐次统计。
    /// </summary>
    /// <param name="sinceUnix">起始 Unix 时间戳（秒），&lt;=0 表示不限</param>
    /// <param name="limit">最大返回数量</param>
    /// <returns>按时间升序排列的播放会话列表</returns>
    public async Task<List<PlaySession>> GetPlaySessionsAsync(long sinceUnix = 0, int limit = 5000)
    {
        await EnsureMaintenanceCompletedAsync();
        var q = _database.Table<PlaySession>();
        if (sinceUnix > 0) q = q.Where(s => s.PlayedAt >= sinceUnix);
        return await q.OrderByDescending(s => s.PlayedAt).Take(limit).ToListAsync();
    }

    /// <summary>获取全部播放会话（不受时间范围限制，用于"全部"时间范围的统计）。</summary>
    public async Task<List<PlaySession>> GetAllPlaySessionsAsync()
    {
        await EnsureMaintenanceCompletedAsync();
        return await _database.Table<PlaySession>().ToListAsync();
    }

    // ═══════════ Favorites ═══════════

    /// <summary>
    /// 设置或取消收藏指定歌曲
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
}
