using CatClawMusic.Core.Models;
using CatClawMusic.Data.Tests.Helpers;
using Xunit;

namespace CatClawMusic.Data.Tests;

public class SongDetailsTests
{
    private static Song NewSong(string title, int artistId = 0, int albumId = 0, SongSource source = SongSource.Local, string? remoteId = null)
        => new()
        {
            Title = title,
            FilePath = $@"C:\music\{Guid.NewGuid():N}.mp3",
            Source = source,
            RemoteId = remoteId,
            ArtistId = artistId,
            AlbumId = albumId,
        };

    [Fact]
    public async Task GetAllSongsWithDetails_AggregatesPlayCountsAndNames()
    {
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var artistId = await db.EnsureArtistAsync("测试歌手");
        var albumId = await db.EnsureAlbumAsync("测试专辑", artistId);
        var song1 = NewSong("曲一", artistId, albumId);
        var song2 = NewSong("曲二", artistId, albumId);
        await db.SaveSongAsync(song1);
        await db.SaveSongAsync(song2);
        await db.RecordPlayBatchAsync(new List<(int, int)> { (song1.Id, 2), (song1.Id, 3), (song2.Id, 1) });

        var songs = await db.GetAllSongsWithDetailsAsync();

        var s1 = songs.Single(s => s.Id == song1.Id);
        Assert.Equal(5, s1.PlayCount);
        Assert.Equal("测试歌手", s1.Artist);
        Assert.Equal("测试专辑", s1.Album);
        var s2 = songs.Single(s => s.Id == song2.Id);
        Assert.Equal(1, s2.PlayCount);
    }

    [Fact]
    public async Task GetSongsWithDetails_OnlyReturnsLocalSongs()
    {
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var local = NewSong("本地歌");
        var remote = NewSong("网络歌", source: SongSource.WebDAV, remoteId: "webdav:1");
        await db.SaveSongAsync(local);
        await db.SaveSongAsync(remote);

        var songs = await db.GetSongsWithDetailsAsync();

        Assert.Single(songs);
        Assert.Equal(local.Id, songs[0].Id);
    }

    [Fact]
    public async Task FillDetails_UnknownArtistAlbumFallback()
    {
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var song = NewSong("孤儿歌", artistId: 98765, albumId: 98765);
        await db.SaveSongAsync(song);

        var songs = await db.GetAllSongsWithDetailsAsync();

        var s = songs.Single(x => x.Id == song.Id);
        Assert.Equal("未知艺术家", s.Artist);
        Assert.Equal("未知专辑", s.Album);
    }

    [Fact]
    public async Task FillDetails_AllArtistsIncludesSecondaryArtist()
    {
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var mainId = await db.EnsureArtistAsync("主歌手");
        var secondId = await db.EnsureArtistAsync("第二歌手");
        var song = NewSong("合唱曲", mainId);
        await db.SaveSongAsync(song);
        // 多艺术家关联以 SongArtists 表为准(主艺术家也有一行,与迁移后的数据形态一致)
        TestDatabaseFactory.ExecuteRaw(db.DatabasePath!,
            "INSERT INTO SongArtists (SongId, ArtistId) VALUES (?, ?)", song.Id, mainId);
        TestDatabaseFactory.ExecuteRaw(db.DatabasePath!,
            "INSERT INTO SongArtists (SongId, ArtistId) VALUES (?, ?)", song.Id, secondId);

        var songs = await db.GetAllSongsWithDetailsAsync();

        var s = songs.Single(x => x.Id == song.Id);
        Assert.Contains("主歌手", s.AllArtists);
        Assert.Contains("第二歌手", s.AllArtists);
    }

    [Fact]
    public async Task GetPlayCountTotals_AggregatesInSql()
    {
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var song1 = NewSong("热歌");
        var song2 = NewSong("冷门歌");
        await db.SaveSongAsync(song1);
        await db.SaveSongAsync(song2);
        await db.RecordPlayBatchAsync(new List<(int, int)> { (song1.Id, 7), (song2.Id, 3), (song1.Id, 2) });

        var totals = await db.GetPlayCountTotalsAsync();

        Assert.Equal(9, totals[song1.Id]);
        Assert.Equal(3, totals[song2.Id]);
    }
}
