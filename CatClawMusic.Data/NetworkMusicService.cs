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

    public async Task<List<Song>> ScanAsync(ConnectionProfile profile)
    {
        var songs = profile.Protocol switch
        {
            ProtocolType.Navidrome => await _subsonic.GetSongsAsync(profile),
            ProtocolType.WebDAV => await ScanWebDavAsync(profile),
            _ => new List<Song>()
        };

        // 保存到本地缓存
        if (songs.Count > 0)
        {
            try
            {
                await _db.EnsureInitializedAsync();
                await _db.ReplaceNetworkSongsAsync(songs);
                System.Diagnostics.Debug.WriteLine($"[CatClaw] 已缓存 {songs.Count} 首网络歌曲到本地");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] 缓存网络歌曲失败: {ex.Message}");
            }
        }

        return songs;
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
