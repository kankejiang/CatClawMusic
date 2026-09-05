using CatClawMusic.Core.Models;
using CatClawMusic.Data.Tests.Helpers;
using Xunit;

namespace CatClawMusic.Data.Tests;

public class PlaylistDatabaseTests
{
    private static Song NewSong(string title, string path) => new()
    {
        Title = title,
        FilePath = path,
        Source = SongSource.Local,
    };

    private static async Task<(MusicDatabase Db, int PlaylistId, List<int> SongIds)> CreateWithSongsAsync(int songCount)
    {
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var playlistId = await db.CreatePlaylistAsync("测试歌单");
        var songIds = new List<int>();
        for (int i = 0; i < songCount; i++)
        {
            var song = NewSong($"歌{i}", $@"C:\music\{Guid.NewGuid():N}.mp3");
            await db.SaveSongAsync(song);
            songIds.Add(song.Id);
        }
        return (db, playlistId, songIds);
    }

    [Fact]
    public async Task AddSongsToPlaylistBatch_PersistsAllRows()
    {
        var (db, playlistId, songIds) = await CreateWithSongsAsync(3);

        await db.AddSongsToPlaylistBatchAsync(playlistId, songIds);

        Assert.Equal(3, await db.GetPlaylistSongCountAsync(playlistId));
    }

    [Fact]
    public async Task AddSongToPlaylist_IsIdempotent()
    {
        var (db, playlistId, songIds) = await CreateWithSongsAsync(1);

        await db.AddSongToPlaylistAsync(playlistId, songIds[0]);
        await db.AddSongToPlaylistAsync(playlistId, songIds[0]);

        Assert.Equal(1, await db.GetPlaylistSongCountAsync(playlistId));
    }

    [Fact]
    public async Task RemoveSongFromPlaylist_ClosesPositionGapAndUpdatesCount()
    {
        var (db, playlistId, songIds) = await CreateWithSongsAsync(3);
        await db.AddSongsToPlaylistBatchAsync(playlistId, songIds);

        await db.RemoveSongFromPlaylistAsync(playlistId, songIds[1]);

        var entries = await db.GetPlaylistSongsAsync(playlistId);
        Assert.Equal(2, entries.Count);
        Assert.Equal(new[] { songIds[0], songIds[2] }, entries.Select(s => s.Id));
        var playlist = await db.GetPlaylistByIdAsync(playlistId);
        Assert.NotNull(playlist);
        Assert.Equal(2, playlist!.SongCount);
    }

    [Fact]
    public async Task RemoveSongsFromPlaylist_PreservesRelativeOrder()
    {
        var (db, playlistId, songIds) = await CreateWithSongsAsync(4);
        await db.AddSongsToPlaylistBatchAsync(playlistId, songIds);

        await db.RemoveSongsFromPlaylistAsync(playlistId, new[] { songIds[0], songIds[2] });

        var entries = await db.GetPlaylistSongsAsync(playlistId);
        Assert.Equal(new[] { songIds[1], songIds[3] }, entries.Select(s => s.Id));
    }

    [Fact]
    public async Task UpdatePlaylistOrder_AppliesNewPositions()
    {
        var (db, playlistId, songIds) = await CreateWithSongsAsync(3);
        await db.AddSongsToPlaylistBatchAsync(playlistId, songIds);

        await db.UpdatePlaylistOrderAsync(playlistId, new List<int> { songIds[2], songIds[0], songIds[1] });

        var entries = await db.GetPlaylistSongsAsync(playlistId);
        Assert.Equal(new[] { songIds[2], songIds[0], songIds[1] }, entries.Select(s => s.Id));
    }

    [Fact]
    public async Task GetPlaylistSongs_ReturnsPositionOrderWithNames()
    {
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var playlistId = await db.CreatePlaylistAsync("带详情歌单");
        var song1 = NewSong("曲一", $@"C:\music\{Guid.NewGuid():N}.mp3");
        var song2 = NewSong("曲二", $@"C:\music\{Guid.NewGuid():N}.mp3");
        await db.SaveSongAsync(song1);
        await db.SaveSongAsync(song2);
        await db.AddSongsToPlaylistBatchAsync(playlistId, new[] { song1.Id, song2.Id });

        var songs = await db.GetPlaylistSongsAsync(playlistId);

        Assert.Equal(2, songs.Count);
        Assert.Equal(song1.Id, songs[0].Id);
    }

    [Fact]
    public async Task GetAllPlaylists_OrdersBySortOrder_NewAppendsToEnd()
    {
        var db = await TestDatabaseFactory.CreateInitializedAsync();

        var firstId = await db.CreatePlaylistAsync("第一个");
        var secondId = await db.CreatePlaylistAsync("第二个");
        var thirdId = await db.CreatePlaylistAsync("第三个");

        var playlists = await db.GetAllPlaylistsAsync();

        Assert.Equal(3, playlists.Count);
        Assert.Equal(new[] { firstId, secondId, thirdId }, playlists.Select(p => p.Id));
    }

    [Fact]
    public async Task UpdatePlaylistsOrder_PersistsCustomOrder()
    {
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var a = await db.CreatePlaylistAsync("A");
        var b = await db.CreatePlaylistAsync("B");
        var c = await db.CreatePlaylistAsync("C");

        // 拖拽后顺序：C, A, B
        await db.UpdatePlaylistsOrderAsync(new List<int> { c, a, b });

        var playlists = await db.GetAllPlaylistsAsync();
        Assert.Equal(new[] { c, a, b }, playlists.Select(p => p.Id));
        Assert.Equal(new[] { 1, 2, 3 }, playlists.Select(p => p.SortOrder));
    }

    [Fact]
    public async Task LegacyPlaylists_AllZeroSortOrder_NormalizedById()
    {
        // 构造旧版本遗留数据：Playlists 表无 SortOrder 列、多行历史歌单
        var dbPath = TestDatabaseFactory.CreateDbPath();
        TestDatabaseFactory.ExecuteRaw(dbPath,
            "CREATE TABLE Playlists (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT, CreatedAt INTEGER, UpdatedAt INTEGER, SongCount INTEGER, IsSystem INTEGER)");
        TestDatabaseFactory.ExecuteRaw(dbPath,
            "INSERT INTO Playlists (Name, CreatedAt, UpdatedAt, SongCount, IsSystem) VALUES ('旧一', 1, 1, 0, 0), ('旧二', 2, 2, 0, 0), ('旧三', 3, 3, 0, 0)");

        var db = new MusicDatabase(dbPath);
        await db.EnsureInitializedAsync();

        var playlists = await db.GetAllPlaylistsAsync();
        Assert.Equal(3, playlists.Count);
        // 归一化按 (Id) 升序写入密集序列，等价创建顺序
        Assert.Equal(new[] { 1, 2, 3 }, playlists.Select(p => p.SortOrder));
    }

    [Fact]
    public async Task FreshInit_CreatesPlaylistCompositeIndexes()
    {
        var dbPath = TestDatabaseFactory.CreateDbPath();
        var db = new MusicDatabase(dbPath);
        await db.EnsureInitializedAsync();

        var indexes = await TestDatabaseFactory.GetIndexesAsync(dbPath, "PlaylistSongs");
        var uniquePlaylistSong = indexes.FirstOrDefault(i =>
            i.Sql != null && i.Sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            && i.Sql.Contains("PlaylistId", StringComparison.OrdinalIgnoreCase)
            && i.Sql.Contains("SongId", StringComparison.OrdinalIgnoreCase));
        Assert.True(uniquePlaylistSong.Name != null, "缺少 (PlaylistId, SongId) 唯一索引");

        var positionIndex = indexes.FirstOrDefault(i =>
            i.Sql != null && !i.Sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            && i.Sql.Contains("PlaylistId", StringComparison.OrdinalIgnoreCase)
            && i.Sql.Contains("Position", StringComparison.OrdinalIgnoreCase));
        Assert.True(positionIndex.Name != null, "缺少 (PlaylistId, Position) 查询索引");
    }

    [Fact]
    public async Task LegacyDuplicateRows_AreDeduped_PositionsNormalized_UniqueIndexCreated()
    {
        // 构造旧版本遗留数据:重复 (PlaylistId, SongId) 行与不连续 Position
        var dbPath = TestDatabaseFactory.CreateDbPath();
        TestDatabaseFactory.ExecuteRaw(dbPath,
            "CREATE TABLE PlaylistSongs (Id INTEGER PRIMARY KEY AUTOINCREMENT, PlaylistId INTEGER, SongId INTEGER, Position INTEGER)");
        TestDatabaseFactory.ExecuteRaw(dbPath,
            "INSERT INTO PlaylistSongs (PlaylistId, SongId, Position) VALUES (1, 10, 0), (1, 10, 1), (1, 20, 5)");

        var db = new MusicDatabase(dbPath);
        await db.EnsureInitializedAsync();

        TestDatabaseFactory.ExecuteRaw(dbPath,
            "CREATE TABLE IF NOT EXISTS MigrationFlagProbe (Id INTEGER)");
        var rows = QueryPlaylistSongs(dbPath);
        Assert.Equal(2, rows.Count); // (10,0) 与 (20,1)
        Assert.Contains((10, 0), rows);
        Assert.Contains((20, 1), rows);

        var indexes = await TestDatabaseFactory.GetIndexesAsync(dbPath, "PlaylistSongs");
        Assert.Contains(indexes, i =>
            i.Sql != null && i.Sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            && i.Sql.Contains("SongId", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class PlaylistRow
    {
        public int SongId { get; set; }
        public int Position { get; set; }
    }

    private static List<(int SongId, int Position)> QueryPlaylistSongs(string dbPath)
    {
        using var raw = new SQLite.SQLiteConnection(dbPath);
        var rows = raw.Query<PlaylistRow>("SELECT SongId, Position FROM PlaylistSongs ORDER BY Position");
        return rows.Select(r => (r.SongId, r.Position)).ToList();
    }
}
