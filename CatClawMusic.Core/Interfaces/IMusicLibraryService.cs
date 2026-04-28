using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 音乐库管理服务接口
/// </summary>
public interface IMusicLibraryService
{
    /// <summary>
    /// 扫描本地音乐
    /// </summary>
    Task<List<Song>> ScanLocalAsync();
    
    /// <summary>
    /// 扫描网络音乐（WebDAV）
    /// </summary>
    Task<List<Song>> ScanNetworkAsync(ConnectionProfile profile);
    
    /// <summary>
    /// 搜索歌曲（本地 + 网络）
    /// </summary>
    Task<List<Song>> SearchAsync(string keyword);
    
    /// <summary>
    /// 获取专辑封面（本地或网络）
    /// </summary>
    Task<Stream?> GetAlbumCoverAsync(Song song);
    
    /// <summary>
    /// 获取所有歌曲
    /// </summary>
    Task<List<Song>> GetAllSongsAsync();
    
    /// <summary>
    /// 按艺术家获取歌曲
    /// </summary>
    Task<List<Song>> GetSongsByArtistAsync(string artist);
    
    /// <summary>
    /// 按专辑获取歌曲
    /// </summary>
    Task<List<Song>> GetSongsByAlbumAsync(string album);
    
    /// <summary>
    /// 获取所有专辑
    /// </summary>
    Task<List<Album>> GetAllAlbumsAsync();
}
