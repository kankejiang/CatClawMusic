using CatClawMusic.Core.Models;
using CatClawMusic.Data.Tests.Helpers;
using Xunit;

namespace CatClawMusic.Data.Tests;

public class PlayStatisticsTests
{
    private static async Task<(MusicDatabase Db, int SongId)> CreateWithSongAsync()
    {
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var song = new Song { Title = "统计用歌", FilePath = $@"C:\music\{Guid.NewGuid():N}.mp3" };
        await db.SaveSongAsync(song);
        return (db, song.Id);
    }

    [Fact]
    public async Task RebuildDailyStats_RebuildsSummaryFromSessions()
    {
        var (db, songId) = await CreateWithSongAsync();
        await db.LogListenSessionAsync(songId, 60_000);
        await db.LogListenSessionAsync(songId, 60_000);
        await db.LogListenSessionAsync(songId, 120_000);

        // 模拟汇总表损坏/丢失
        TestDatabaseFactory.ExecuteRaw(db.DatabasePath!, "DELETE FROM DailyPlayStat");
        TestDatabaseFactory.ExecuteRaw(db.DatabasePath!, "DELETE FROM HourlyPlayStat");
        TestDatabaseFactory.ExecuteRaw(db.DatabasePath!, "DELETE FROM DailySongStat");

        await db.RebuildDailyStatsIfNeededAsync();

        var daily = await db.GetDailyPlayStatsAsync(null, null);
        Assert.Single(daily);
        Assert.Equal(3, daily[0].PlayCount);
        Assert.Equal(240_000, daily[0].TotalDurationMs);

        var hourly = await db.GetHourlyPlayStatsAsync(null, null);
        Assert.Equal(3, hourly.Sum(h => h.PlayCount));

        var songStats = await db.GetDailySongStatsAsync(null, null);
        Assert.Single(songStats);
        Assert.Equal(3, songStats[0].PlayCount);
        Assert.Equal(240_000, songStats[0].TotalDurationMs);
    }

    [Fact]
    public async Task RecalibratePlayCounts_MergesFlushRowsWithinHalfSong()
    {
        // 歌曲时长 600 秒。两行会话间隔 200 秒,小于半首歌(300 秒),
        // 按秒语义应合并为一次聆听;旧的毫秒混用会把阈值压到 120 秒导致误拆。
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var song = new Song { Title = "长歌", FilePath = $@"C:\music\{Guid.NewGuid():N}.mp3", Duration = 600 };
        await db.SaveSongAsync(song);
        await db.RecordPlayBatchAsync(new List<(int, int)> { (song.Id, 5) });

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        TestDatabaseFactory.ExecuteRaw(db.DatabasePath!,
            "INSERT INTO PlaySession (SongId, PlayedAt, DurationMs) VALUES (?, ?, ?)",
            song.Id, now - 400, 300_000);
        TestDatabaseFactory.ExecuteRaw(db.DatabasePath!,
            "INSERT INTO PlaySession (SongId, PlayedAt, DurationMs) VALUES (?, ?, ?)",
            song.Id, now - 200, 300_000);

        var changed = await db.RecalibratePlayCountsAsync();

        Assert.True(changed > 0);
        var sessions = await db.GetAllPlaySessionsAsync();
        Assert.Single(sessions);

        var history = await db.GetRecentPlaysAsync(10);
        var hist = history.Single(h => h.SongId == song.Id);
        Assert.Equal(1, hist.PlayCount);
    }

    [Fact]
    public async Task RecalibratePlayCounts_KeepsSeparateFullSongReplays()
    {
        // 间隔 700 秒,大于半首歌(300 秒):两次独立聆听,不应合并
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var song = new Song { Title = "重播歌", FilePath = $@"C:\music\{Guid.NewGuid():N}.mp3", Duration = 600 };
        await db.SaveSongAsync(song);
        await db.RecordPlayBatchAsync(new List<(int, int)> { (song.Id, 2) });

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        TestDatabaseFactory.ExecuteRaw(db.DatabasePath!,
            "INSERT INTO PlaySession (SongId, PlayedAt, DurationMs) VALUES (?, ?, ?)",
            song.Id, now - 1400, 550_000);
        TestDatabaseFactory.ExecuteRaw(db.DatabasePath!,
            "INSERT INTO PlaySession (SongId, PlayedAt, DurationMs) VALUES (?, ?, ?)",
            song.Id, now - 700, 550_000);

        var changed = await db.RecalibratePlayCountsAsync();

        var sessions = await db.GetAllPlaySessionsAsync();
        Assert.Equal(2, sessions.Count);
        var history = await db.GetRecentPlaysAsync(10);
        var hist = history.Single(h => h.SongId == song.Id);
        Assert.Equal(2, hist.PlayCount);
    }

    [Fact]
    public async Task GetTopPlayedSongs_ReturnsSqlAggregatedTop()
    {
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var song1 = new Song { Title = "热歌", FilePath = $@"C:\music\{Guid.NewGuid():N}.mp3" };
        var song2 = new Song { Title = "冷歌", FilePath = $@"C:\music\{Guid.NewGuid():N}.mp3" };
        await db.SaveSongAsync(song1);
        await db.SaveSongAsync(song2);
        await db.RecordPlayBatchAsync(new List<(int, int)> { (song1.Id, 7), (song2.Id, 3) });

        var top = await db.GetTopPlayedSongsAsync(10);

        Assert.Equal(2, top.Count);
        Assert.Equal(song1.Id, top[0].Id);
        Assert.Equal(7, top[0].PlayCount);
    }

    [Fact]
    public async Task RecordPlayAsync_AggregatesIntoSingleRowPerSong()
    {
        var (db, songId) = await CreateWithSongAsync();
        await db.RecordPlayAsync(songId);
        await db.RecordPlayAsync(songId);

        var history = await db.GetRecentPlaysAsync(10);
        Assert.Single(history);
        Assert.Equal(2, history[0].PlayCount);
    }
}
