using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 音乐库管理服务接口
/// </summary>
public interface IMusicLibraryService
{
    /// <summary>
    /// 歌单数据变更事件（创建/删除/重命名/添加歌曲/移除歌曲等操作后触发），
    /// 供 UI 层（如 PlaylistViewModel）订阅以刷新歌单列表。
    /// </summary>
    event Action? PlaylistsChanged;

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

    /// <summary>获取去重合并后的歌曲总数</summary>
    Task<int> GetMergedSongCountAsync();

    /// <summary>获取收藏歌曲的总数</summary>
    Task<int> GetFavoriteSongCountAsync();

    /// <summary>获取最近播放歌曲的总数</summary>
    Task<int> GetRecentSongCountAsync();

    /// <summary>获取全部歌曲中的第一首歌曲 ID（用于占位/默认选择）</summary>
    Task<int> GetFirstSongIdForAllAsync();

    /// <summary>获取收藏列表中的第一首歌曲 ID</summary>
    Task<int> GetFirstFavoriteSongIdAsync();

    /// <summary>获取最近播放列表中的第一首歌曲 ID</summary>
    Task<int> GetFirstRecentSongIdAsync();
    
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

    /// <summary>获取最近播放歌曲列表</summary>
    Task<List<Song>> GetRecentSongsAsync();

    /// <summary>获取播放次数最多的歌曲列表</summary>
    Task<List<Song>> GetTopPlayedSongsAsync(int limit = 50);

    /// <summary>获取所有播放列表</summary>
    Task<List<Playlist>> GetAllPlaylistsAsync();
    /// <summary>根据 ID 获取播放列表</summary>
    Task<Playlist?> GetPlaylistByIdAsync(int id);
    /// <summary>创建播放列表，返回新播放列表 ID</summary>
    Task<int> CreatePlaylistAsync(string name);
    /// <summary>更新播放列表信息</summary>
    Task UpdatePlaylistAsync(Playlist playlist);
    /// <summary>删除播放列表</summary>
    Task DeletePlaylistAsync(int playlistId);
    /// <summary>向播放列表添加歌曲</summary>
    Task AddSongToPlaylistAsync(int playlistId, int songId);
    /// <summary>从播放列表移除歌曲</summary>
    Task RemoveSongFromPlaylistAsync(int playlistId, int songId);
    /// <summary>从播放列表批量移除歌曲</summary>
    Task RemoveSongsFromPlaylistAsync(int playlistId, IEnumerable<int> songIds);
    /// <summary>获取播放列表中的歌曲</summary>
    Task<List<Song>> GetPlaylistSongsAsync(int playlistId);
    /// <summary>更新歌曲在播放列表中的位置</summary>
    Task UpdateSongPositionAsync(int playlistId, int songId, int newPosition);
    /// <summary>批量更新播放列表中所有歌曲的顺序位置</summary>
    Task UpdatePlaylistOrderAsync(int playlistId, List<int> orderedSongIds);
    /// <summary>获取播放列表中的歌曲数量</summary>
    Task<int> GetPlaylistSongCountAsync(int playlistId);

    /// <summary>保存缓存的歌曲信息</summary>
    Task SaveCachedSongAsync(CachedSong cachedSong);
    /// <summary>获取所有缓存的歌曲</summary>
    Task<List<CachedSong>> GetCachedSongsAsync();
    /// <summary>根据歌曲 ID 获取缓存的歌曲</summary>
    Task<CachedSong?> GetCachedSongAsync(int songId);
    /// <summary>删除缓存的歌曲</summary>
    Task DeleteCachedSongAsync(int songId);
}
