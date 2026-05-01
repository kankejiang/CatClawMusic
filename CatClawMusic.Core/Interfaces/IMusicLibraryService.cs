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
    Task<List<Song>> ScanLocalAsync(List<string>? customFolders = null);

    /// <summary>
    /// 导入预扫描歌曲列表（用于 Android 端三路径扫描后入库）
    /// </summary>
    Task<List<Song>> ImportSongsAsync(List<Song> songs);
    
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
    /// 获取去重后的全部歌曲（本地 + 网络，同标题+同艺术家只保留本地优先）
    /// </summary>
    Task<List<Song>> GetMergedSongsAsync();
    
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

    /// <summary>确保艺术家存在，返回 ID</summary>
    Task<int> EnsureArtistAsync(string name);

    /// <summary>确保专辑存在，返回 ID</summary>
    Task<int> EnsureAlbumAsync(string title, int artistId);

    /// <summary>保存歌曲（去重），返回影响行数</summary>
    Task<int> SaveSongAsync(Song song);

    /// <summary>获取收藏歌曲列表</summary>
    Task<List<Song>> GetFavoriteSongsAsync();

    /// <summary>获取最近播放歌曲列表（保留 20 条）</summary>
    Task<List<Song>> GetRecentSongsAsync();
}
