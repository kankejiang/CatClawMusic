using CatClawMusic.Core.Models;
using CatClawMusic.Data.Tests.Helpers;
using Xunit;

namespace CatClawMusic.Data.Tests;

public class RemoveStaleSongsTests
{
    [Fact]
    public async Task LocalStaleCleanup_UsesDirectoryPrefixSemantics()
    {
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var keep1 = new Song { Title = "保留1", FilePath = @"C:\music\A\1.mp3" };
        var keep2 = new Song { Title = "保留2", FilePath = @"C:\music\B\2.mp3" };
        var stale = new Song { Title = "过期", FilePath = @"C:\other\3.mp3" };
        await db.SaveSongAsync(keep1);
        await db.SaveSongAsync(keep2);
        await db.SaveSongAsync(stale);
        await db.RecordPlayBatchAsync(new List<(int, int)> { (stale.Id, 1) });

        var deleted = await db.RemoveStaleSongsAsync(SongSource.Local, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\music" });

        Assert.Equal(1, deleted);
        var songs = await db.GetSongsAsync();
        Assert.Equal(2, songs.Count);
        Assert.DoesNotContain(songs, s => s.Id == stale.Id);
        // 孤立播放历史应被级联清理
        var history = await db.GetRecentPlaysAsync(10);
        Assert.DoesNotContain(history, h => h.SongId == stale.Id);
    }

    [Fact]
    public async Task LocalStaleCleanup_NothingStale_DeletesNothing()
    {
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var song = new Song { Title = "在库", FilePath = @"C:\music\A\1.mp3" };
        await db.SaveSongAsync(song);

        var deleted = await db.RemoveStaleSongsAsync(SongSource.Local, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\music" });

        Assert.Equal(0, deleted);
        Assert.Single(await db.GetSongsAsync());
    }

    [Fact]
    public async Task RemoteStaleCleanup_RemovesOnlyUnreferencedRemoteIds()
    {
        var db = await TestDatabaseFactory.CreateInitializedAsync();
        var keep = new Song { Title = "云保留", FilePath = "https://nas/keep.mp3", Source = SongSource.WebDAV, RemoteId = "webdav:keep" };
        var stale = new Song { Title = "云过期", FilePath = "https://nas/stale.mp3", Source = SongSource.WebDAV, RemoteId = "webdav:stale" };
        await db.SaveSongAsync(keep);
        await db.SaveSongAsync(stale);
        await db.RecordPlayBatchAsync(new List<(int, int)> { (stale.Id, 2) });

        var deleted = await db.RemoveStaleSongsAsync(SongSource.WebDAV, new HashSet<string>(), new HashSet<string> { "webdav:keep" });

        Assert.Equal(1, deleted);
        var songs = await db.GetAllSongsWithDetailsAsync();
        Assert.Single(songs);
        Assert.Equal(keep.Id, songs[0].Id);
        var history = await db.GetRecentPlaysAsync(10);
        Assert.DoesNotContain(history, h => h.SongId == stale.Id);
    }
}
