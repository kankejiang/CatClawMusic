using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Data;

/// <summary>
/// 音乐库服务，统一管理本地扫描、网络扫描、搜索、播放列表等操作
/// </summary>
public class MusicLibraryService : IMusicLibraryService
{
    /// <summary>
    /// 数据库操作实例
    /// </summary>
    private readonly MusicDatabase _db;

    /// <summary>
    /// 网络音乐服务（可选）
    /// </summary>
    private readonly INetworkMusicService? _networkMusic;

    /// <summary>
    /// 创建 MusicLibraryService 实例
    /// </summary>
    /// <param name="db">数据库操作实例</param>
    /// <param name="networkMusic">网络音乐服务（可选）</param>
    public MusicLibraryService(MusicDatabase db, INetworkMusicService? networkMusic = null)
    {
        _db = db;
        _networkMusic = networkMusic;
    }

    /// <summary>
    /// 扫描本地文件夹中的音乐文件，去重后入库
    /// </summary>
    /// <param name="customFolders">额外的自定义扫描目录，为 null 时仅扫描默认目录</param>
    /// <returns>去重后的歌曲列表</returns>
    public async Task<List<Song>> ScanLocalAsync(List<string>? customFolders = null)
    {
        await _db.EnsureInitializedAsync();

        var scanDirs = new List<string> { "/storage/emulated/0/Music", "/storage/emulated/0/Download" };
        if (customFolders != null)
        {
            foreach (var f in customFolders)
                if (!string.IsNullOrWhiteSpace(f) && Directory.Exists(f) && !scanDirs.Contains(f))
                    scanDirs.Add(f);
        }

        var result = await Task.Run(() =>
        {
            var allSongs = new List<Song>();
            var seenPaths = new HashSet<string>();

            foreach (var dir in scanDirs)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        var scanPaths = MusicUtility.ScanFolderRecursive(dir);
                        foreach (var path in scanPaths)
                        {
                            if (seenPaths.Add(path))
                            {
                                var song = TagReader.ReadSongInfo(path);
                                if (song != null) allSongs.Add(song);
                            }
                        }
                    }
                    catch { }
                }
            }
            return allSongs;
        });

        return await SaveAndDeduplicateAsync(result);
    }

    /// <summary>接受预扫描的歌曲列表，去重后入库</summary>
    /// <param name="songs">预扫描的歌曲列表</param>
    /// <returns>去重后的歌曲列表</returns>
    public async Task<List<Song>> ImportSongsAsync(List<Song> songs)
    {
        await _db.EnsureInitializedAsync();
        return await SaveAndDeduplicateAsync(songs);
    }

    /// <summary>
    /// 对歌曲列表按 FilePath 去重，填充 ArtistId 和 AlbumId 后批量入库
    /// </summary>
    private async Task<List<Song>> SaveAndDeduplicateAsync(List<Song> allSongs)
    {
        var distinct = allSongs
            .GroupBy(s => s.FilePath)
            .Select(g => g.First())
            .ToList();

        foreach (var song in distinct)
        {
            try
            {
                // 拆分多艺术家（如 "国风堂/哦漏" → ["国风堂", "哦漏"]）
                var artistNames = MusicUtility.SplitArtistNames(song.Artist);
                var artistIds = new List<int>();
                foreach (var name in artistNames)
                {
                    var id = await _db.EnsureArtistAsync(name);
                    artistIds.Add(id);
                }

                // 主艺术家
                song.ArtistId = artistIds.Count > 0 ? artistIds[0] : await _db.EnsureArtistAsync("未知艺术家");
                song.AlbumId = await _db.EnsureAlbumAsync(song.Album, song.ArtistId);
                song.DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await _db.SaveSongAsync(song);

                // 多艺术家关联（跳过主艺术家，避免重复）
                if (artistIds.Count > 1)
                {
                    var extraIds = artistIds.Skip(1).ToList();
                    if (extraIds.Count > 0)
                        await _db.SaveSongArtistsBatchAsync(new List<(int, List<int>)>
                        {
                            (song.Id, artistIds)
                        });
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CatClaw] 保存歌曲失败: {song.FilePath}, {ex.Message}"); }
        }
        return distinct;
    }

    /// <summary>
    /// 扫描网络音乐源（当前未实现，返回空列表）
    /// </summary>
    /// <param name="profile">连接配置</param>
    /// <returns>空列表</returns>
    public async Task<List<Song>> ScanNetworkAsync(ConnectionProfile profile)
    {
        return new List<Song>();
    }

    /// <summary>
    /// 按关键词搜索歌曲，合并本地数据库和网络搜索结果，按标题+艺术家去重
    /// </summary>
    /// <param name="keyword">搜索关键词</param>
    /// <returns>合并去重后的歌曲列表</returns>
    public async Task<List<Song>> SearchAsync(string keyword)
    {
        await _db.EnsureInitializedAsync();
        if (string.IsNullOrWhiteSpace(keyword))
            return await GetAllSongsAsync();

        // 本地数据库搜索
        var localResults = await _db.SearchSongsAsync(keyword);

        // 网络搜索（Navidrome/Subsonic）
        var networkResults = new List<Song>();
        if (_networkMusic != null)
        {
            try
            {
                var profiles = await _networkMusic.GetProfilesAsync();
                var enabledProfiles = profiles.Where(p => p.IsEnabled).ToList();
                foreach (var profile in enabledProfiles)
                {
                    try
                    {
                        var results = await _networkMusic.SearchAsync(keyword, profile);
                        networkResults.AddRange(results);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CatClaw] 网络({profile.Name})搜索失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] 获取网络配置失败: {ex.Message}");
            }
        }

        // 合并结果：本地 + 网络，按标题+艺术家去重（本地优先）
        if (networkResults.Count == 0) return localResults;

        var allResults = new List<Song>(localResults);
        var localKeys = new HashSet<string>(
            localResults.Select(s => ((s.Title ?? "").Trim() + "|" + (s.Artist ?? "").Trim()).ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var ns in networkResults)
        {
            var key = ((ns.Title ?? "").Trim() + "|" + (ns.Artist ?? "").Trim()).ToLowerInvariant();
            if (localKeys.Add(key))
                allResults.Add(ns);
        }

        return allResults;
    }

    /// <summary>
    /// 获取歌曲的专辑封面流
    /// </summary>
    /// <param name="song">歌曲对象</param>
    /// <returns>封面图片流，未找到时返回 null</returns>
    public async Task<Stream?> GetAlbumCoverAsync(Song song)
    {
        if (song.FilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            song.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;
        var coverBytes = TagReader.ExtractCoverArt(song.FilePath);
        if (coverBytes != null) return new MemoryStream(coverBytes);
        if (!string.IsNullOrEmpty(song.CoverArtPath) && File.Exists(song.CoverArtPath))
            return File.OpenRead(song.CoverArtPath);
        return null;
    }

    /// <summary>
    /// 获取所有本地歌曲
    /// </summary>
    /// <returns>本地歌曲列表</returns>
    public async Task<List<Song>> GetAllSongsAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetSongsAsync();
    }

    /// <summary>
    /// 获取去重合并的全部歌曲：本地 + 网络歌曲按标题+艺术家去重，本地优先，自动过滤已关闭协议
    /// </summary>
    /// <returns>去重合并后的歌曲列表</returns>
    public async Task<List<Song>> GetMergedSongsAsync()
    {
        var allSongs = await GetAllSongsAsync();
        var networkSongs = await _db.GetCachedNetworkSongsAsync();
        allSongs.AddRange(networkSongs);

        var enabledProtocols = await _db.GetEnabledProtocolsAsync();
        allSongs = _db.FilterByEnabledProtocols(allSongs, enabledProtocols);

        var deduped = allSongs
            .GroupBy(s => (s.Title?.Trim() ?? "").ToLowerInvariant() + "|" + (s.Artist?.Trim() ?? "").ToLowerInvariant())
            .Select(g =>
            {
                var local = g.FirstOrDefault(s => s.Source == SongSource.Local);
                if (local != null) return local;
                var webdav = g.FirstOrDefault(s => s.Source == SongSource.WebDAV);
                if (webdav != null) return webdav;
                var smb = g.FirstOrDefault(s => s.Source == SongSource.SMB);
                if (smb != null) return smb;
                return g.First();
            })
            .OrderBy(s => s.Title)
            .ToList();
        return deduped;
    }

    public async Task<int> GetMergedSongCountAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetMergedDedupedCountAsync();
    }

    public async Task<int> GetFavoriteSongCountAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetFavoriteCountAsync();
    }

    public async Task<int> GetRecentSongCountAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetRecentPlayCountAsync();
    }

    public async Task<int> GetFirstSongIdForAllAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetFirstSongIdForAllAsync();
    }

    public async Task<int> GetFirstFavoriteSongIdAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetFirstFavoriteSongIdAsync();
    }

    public async Task<int> GetFirstRecentSongIdAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetFirstRecentSongIdAsync();
    }

    /// <summary>
    /// 按艺术家名称获取歌曲列表
    /// </summary>
    /// <param name="artist">艺术家名称</param>
    /// <returns>歌曲列表</returns>
    public async Task<List<Song>> GetSongsByArtistAsync(string artist)
    {
        await _db.EnsureInitializedAsync();
        // 使用 SQL JOIN 在数据库层面过滤
        return await _db.GetSongsByArtistAsync(artist);
    }

    /// <summary>
    /// 按专辑名称获取歌曲列表
    /// </summary>
    /// <param name="album">专辑名称</param>
    /// <returns>歌曲列表</returns>
    public async Task<List<Song>> GetSongsByAlbumAsync(string album)
    {
        await _db.EnsureInitializedAsync();
        // 使用 SQL JOIN 在数据库层面过滤
        return await _db.GetSongsByAlbumAsync(album);
    }

    /// <summary>
    /// 获取所有专辑（聚合歌曲数据）
    /// </summary>
    /// <returns>包含歌曲数量和艺术家信息的专辑列表</returns>
    public async Task<List<Album>> GetAllAlbumsAsync()
    {
        await _db.EnsureInitializedAsync();
        var all = await _db.GetSongsAsync();
        return all.GroupBy(s => s.Album)
            .Select(g => new Album { Name = g.Key, Artist = g.First().Artist, SongCount = g.Count() })
            .ToList();
    }

    /// <summary>
    /// 委托：按名称确保艺术家存在
    /// </summary>
    /// <param name="name">艺术家名称</param>
    /// <returns>艺术家 ID</returns>
    public Task<int> EnsureArtistAsync(string name) => _db.EnsureArtistAsync(name);

    /// <summary>
    /// 委托：按标题和艺术家 ID 确保专辑存在
    /// </summary>
    /// <param name="title">专辑标题</param>
    /// <param name="artistId">艺术家 ID</param>
    /// <returns>专辑 ID</returns>
    public Task<int> EnsureAlbumAsync(string title, int artistId) => _db.EnsureAlbumAsync(title, artistId);

    /// <summary>
    /// 委托：保存歌曲
    /// </summary>
    /// <param name="song">歌曲对象</param>
    /// <returns>受影响的行数</returns>
    public async Task<int> SaveSongAsync(Song song) { await _db.EnsureInitializedAsync(); return await _db.SaveSongAsync(song); }

    /// <summary>
    /// 委托：获取收藏歌曲列表
    /// </summary>
    /// <returns>收藏歌曲列表</returns>
    public async Task<List<Song>> GetFavoriteSongsAsync() { await _db.EnsureInitializedAsync(); return await _db.GetFavoriteSongsAsync(); }

    /// <summary>
    /// 委托：获取最近播放歌曲列表
    /// </summary>
    /// <returns>最近播放歌曲列表</returns>
    public async Task<List<Song>> GetRecentSongsAsync() { await _db.EnsureInitializedAsync(); return await _db.GetRecentSongsAsync(); }

    /// <summary>
    /// 委托：获取播放次数最多的歌曲列表
    /// </summary>
    /// <param name="limit">最大返回数量</param>
    /// <returns>播放次数最多的歌曲列表</returns>
    public async Task<List<Song>> GetTopPlayedSongsAsync(int limit = 50) { await _db.EnsureInitializedAsync(); return await _db.GetTopPlayedSongsAsync(limit); }

    // ── Playlist CRUD ──

    /// <summary>
    /// 委托：获取所有播放列表
    /// </summary>
    /// <returns>播放列表列表</returns>
    public async Task<List<Playlist>> GetAllPlaylistsAsync() { await _db.EnsureInitializedAsync(); return await _db.GetAllPlaylistsAsync(); }

    /// <summary>
    /// 委托：根据 ID 获取播放列表
    /// </summary>
    /// <param name="id">播放列表 ID</param>
    /// <returns>播放列表对象，未找到时返回 null</returns>
    public async Task<Playlist?> GetPlaylistByIdAsync(int id) { await _db.EnsureInitializedAsync(); return await _db.GetPlaylistByIdAsync(id); }

    /// <summary>
    /// 委托：创建播放列表
    /// </summary>
    /// <param name="name">播放列表名称</param>
    /// <returns>新播放列表的 ID</returns>
    public async Task<int> CreatePlaylistAsync(string name) => await _db.CreatePlaylistAsync(name);

    /// <summary>
    /// 委托：更新播放列表
    /// </summary>
    /// <param name="playlist">播放列表对象</param>
    public async Task UpdatePlaylistAsync(Playlist playlist) => await _db.UpdatePlaylistAsync(playlist);

    /// <summary>
    /// 委托：删除播放列表
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    public async Task DeletePlaylistAsync(int playlistId) => await _db.DeletePlaylistAsync(playlistId);

    /// <summary>
    /// 委托：向播放列表添加歌曲
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="songId">歌曲 ID</param>
    public async Task AddSongToPlaylistAsync(int playlistId, int songId) => await _db.AddSongToPlaylistAsync(playlistId, songId);

    /// <summary>
    /// 委托：从播放列表移除歌曲
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="songId">歌曲 ID</param>
    public async Task RemoveSongFromPlaylistAsync(int playlistId, int songId) => await _db.RemoveSongFromPlaylistAsync(playlistId, songId);

    /// <summary>
    /// 委托：获取播放列表中的所有歌曲
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <returns>歌曲列表</returns>
    public async Task<List<Song>> GetPlaylistSongsAsync(int playlistId) => await _db.GetPlaylistSongsAsync(playlistId);

    /// <summary>
    /// 委托：更新播放列表中歌曲的位置
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="songId">歌曲 ID</param>
    /// <param name="newPosition">新位置索引</param>
    public async Task UpdateSongPositionAsync(int playlistId, int songId, int newPosition) => await _db.UpdateSongPositionAsync(playlistId, songId, newPosition);

    /// <summary>
    /// 委托：批量更新播放列表中所有歌曲的顺序位置
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="orderedSongIds">排序后的歌曲 ID 列表</param>
    public async Task UpdatePlaylistOrderAsync(int playlistId, List<int> orderedSongIds) => await _db.UpdatePlaylistOrderAsync(playlistId, orderedSongIds);

    /// <summary>
    /// 委托：获取播放列表歌曲数量
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <returns>歌曲数量</returns>
    public async Task<int> GetPlaylistSongCountAsync(int playlistId) => await _db.GetPlaylistSongCountAsync(playlistId);

    // ── CachedSong CRUD ──

    /// <summary>
    /// 委托：保存缓存歌曲
    /// </summary>
    /// <param name="cachedSong">缓存歌曲对象</param>
    public async Task SaveCachedSongAsync(CachedSong cachedSong) => await _db.SaveCachedSongAsync(cachedSong);

    /// <summary>
    /// 委托：获取所有缓存歌曲
    /// </summary>
    /// <returns>缓存歌曲列表</returns>
    public async Task<List<CachedSong>> GetCachedSongsAsync() { await _db.EnsureInitializedAsync(); return await _db.GetCachedSongsAsync(); }

    /// <summary>
    /// 委托：根据 ID 获取缓存歌曲
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    /// <returns>缓存歌曲信息，未找到时返回 null</returns>
    public async Task<CachedSong?> GetCachedSongAsync(int songId) { await _db.EnsureInitializedAsync(); return await _db.GetCachedSongAsync(songId); }

    /// <summary>
    /// 委托：删除缓存歌曲
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    public async Task DeleteCachedSongAsync(int songId) => await _db.DeleteCachedSongAsync(songId);
}
