using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

/// <summary>
/// 网络音乐服务——按协议类型分发
/// </summary>
public class NetworkMusicService : INetworkMusicService
{
    private readonly MusicDatabase _db;
    private readonly ISubsonicService _subsonic;
    private readonly INetworkFileService _webDav;

    public NetworkMusicService(MusicDatabase db, ISubsonicService subsonic, INetworkFileService webDav)
    {
        _db = db;
        _subsonic = subsonic;
        _webDav = webDav;
    }

    public async Task<List<ConnectionProfile>> GetProfilesAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetConnectionProfilesAsync();
    }

    public async Task<List<Song>> ScanAsync(ConnectionProfile profile,
        IProgress<(int done, int total, string status)>? progress = null,
        Action<List<Song>>? songBatchCallback = null)
    {
        // 先清除旧的网络缓存
        try
        {
            await _db.EnsureInitializedAsync();
            await _db.ReplaceNetworkSongsBeginAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] 清除旧网络歌曲失败: {ex.Message}");
        }

        // 增量式拉取：每个专辑完成后立即入库 + 回调通知 UI
        var allSongs = new List<Song>();

        if (profile.Protocol == ProtocolType.Navidrome)
        {
            allSongs = await _subsonic.GetSongsAsync(profile, progress, async (batch) =>
            {
                // 每个专辑的歌曲立即入库
                try
                {
                    foreach (var s in batch)
                    {
                        if (!string.IsNullOrEmpty(s.Artist))
                            s.ArtistId = await _db.EnsureArtistAsync(s.Artist);
                        if (!string.IsNullOrEmpty(s.Album))
                            s.AlbumId = await _db.EnsureAlbumAsync(s.Album, s.ArtistId);
                        s.DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        await _db.InsertSongAsync(s);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CatClaw] 增量入库失败: {ex.Message}");
                }
                // 通知 ViewModel 增量刷新列表
                songBatchCallback?.Invoke(batch);
            });
        }
        else if (profile.Protocol == ProtocolType.WebDAV)
        {
            var songs = await ScanWebDavAsync(profile);
            allSongs = songs;
        }

        System.Diagnostics.Debug.WriteLine($"[CatClaw] ScanAsync 总计 {allSongs.Count} 首网络歌曲");
        return allSongs;
    }

    public async Task<List<Song>> SearchAsync(string keyword, ConnectionProfile profile)
    {
        return profile.Protocol switch
        {
            ProtocolType.Navidrome => await _subsonic.SearchAsync(keyword, profile),
            _ => new List<Song>()
        };
    }

    public async Task<Stream?> GetCoverAsync(string songId, ConnectionProfile profile)
    {
        if (profile.Protocol == ProtocolType.Navidrome)
        {
            var bytes = await _subsonic.GetCoverArtAsync(songId, profile);
            return bytes != null ? new MemoryStream(bytes) : null;
        }
        return null;
    }

    public Task<string> GetStreamUrlAsync(Song song, ConnectionProfile profile)
    {
        if (profile.Protocol == ProtocolType.Navidrome)
            return Task.FromResult(_subsonic.GetStreamUrl(song.FilePath, profile));
        return Task.FromResult(song.FilePath);
    }

    private async Task<List<Song>> ScanWebDavAsync(ConnectionProfile profile)
    {
        // TODO: 实际 WebDAV 文件扫描
        return new List<Song>();
    }
}
