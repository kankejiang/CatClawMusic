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

    /// <summary>歌单数据变更事件，在歌单 CRUD 与歌曲增删后触发</summary>
    public event Action? PlaylistsChanged;

    /// <summary>触发歌单变更事件（安全调用，避免异常传播）</summary>
    private void RaisePlaylistsChanged()
    {
        try { PlaylistsChanged?.Invoke(); } catch { }
    }

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

        var scanDirs = new List<string>();
        if (customFolders != null)
        {
            foreach (var f in customFolders)
                if (!string.IsNullOrWhiteSpace(f) && Directory.Exists(f) && !scanDirs.Contains(f))
                    scanDirs.Add(f);
        }
        else
        {
            scanDirs.Add("/storage/emulated/0/Music");
            scanDirs.Add("/storage/emulated/0/Download");
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
    /// 对歌曲列表按 FilePath 去重，填充 ArtistId 和 AlbumId 后批量入库。
    /// 使用批量 API（EnsureArtistsBatchAsync / EnsureAlbumsBatchAsync / InsertSongsBatchAsync）大幅减少逐条 await 的 IO 次数。
    /// </summary>
    private async Task<List<Song>> SaveAndDeduplicateAsync(List<Song> allSongs)
    {
        var distinct = allSongs
            .GroupBy(s => s.FilePath)
            .Select(g => g.First())
            .ToList();

        if (distinct.Count == 0) return distinct;

        // 1. 收集所有艺术家名 + (专辑名, 主艺术家ID) 对，确保存在艺术家
        var allArtistNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var song in distinct)
        {
            foreach (var name in MusicUtility.SplitArtistNames(song.Artist))
                allArtistNames.Add(name);
        }
        if (allArtistNames.Count == 0)
            allArtistNames.Add("未知艺术家");

        var artistIdMap = await _db.EnsureArtistsBatchAsync(allArtistNames.ToList());
        var unknownArtistId = artistIdMap.TryGetValue("未知艺术家", out var uId) ? uId
            : (artistIdMap.FirstOrDefault().Value);

        // 2. 为每首歌解析主艺术家ID，构造 (album, artistId) 列表
        var songArtistIds = new List<List<int>>(distinct.Count);
        var albumInputs = new List<(string title, int artistId)>(distinct.Count);
        for (int i = 0; i < distinct.Count; i++)
        {
            var song = distinct[i];
            var names = MusicUtility.SplitArtistNames(song.Artist);
            var ids = new List<int>();
            foreach (var name in names)
            {
                if (artistIdMap.TryGetValue(name, out var id))
                    ids.Add(id);
            }
            if (ids.Count == 0)
                ids.Add(unknownArtistId);
            songArtistIds.Add(ids);
            song.ArtistId = ids[0];
            song.AlbumId = 0; // 待批量分配
            albumInputs.Add((song.Album, song.ArtistId));
            song.DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        // 3. 批量确保专辑存在
        var albumIdMap = await _db.EnsureAlbumsBatchAsync(albumInputs);
        for (int i = 0; i < distinct.Count; i++)
        {
            var song = distinct[i];
            if (albumIdMap.TryGetValue((song.Album, song.ArtistId), out var albumId))
                song.AlbumId = albumId;
        }

        // 4. 批量插入歌曲（内部已处理 FilePath/RemoteId 去重）
        var inserted = await _db.InsertSongsBatchAsync(distinct);

        // 5. 收集多艺术家关联，批量写入
        var songArtistEntries = new List<(int SongId, List<int> ArtistIds)>();
        for (int i = 0; i < inserted.Count; i++)
        {
            var song = inserted[i];
            var ids = songArtistIds[i];
            if (ids.Count > 1)
                songArtistEntries.Add((song.Id, ids));
        }
        if (songArtistEntries.Count > 0)
            await _db.SaveSongArtistsBatchAsync(songArtistEntries);

        return inserted;
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
                if (enabledProfiles.Count > 0)
                {
                    // 多台服务器并行搜索，每个 profile 独立无依赖
                    var searchTasks = enabledProfiles.Select(async profile =>
                    {
                        try { return await _networkMusic.SearchAsync(keyword, profile); }
                        catch (Exception ex)
                        {
                            Log.Debug("MusicLibraryService", $"[CatClaw] 网络({profile.Name})搜索失败: {ex.Message}");
                            return new List<Song>();
                        }
                    });
                    var results = await Task.WhenAll(searchTasks);
                    foreach (var r in results)
                        networkResults.AddRange(r);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("MusicLibraryService", $"[CatClaw] 获取网络配置失败: {ex.Message}");
            }
        }

        // 合并结果：本地 + 网络（不去重，保留同歌名不同版本）
        if (networkResults.Count == 0) return localResults;

        var allResults = new List<Song>(localResults);
        allResults.AddRange(networkResults);

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
            song.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            song.FilePath.StartsWith("smb://", StringComparison.OrdinalIgnoreCase) ||
            song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
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
    /// 获取全部歌曲：本地 + 网络歌曲合并，自动过滤已关闭协议（不去重，保留同歌名不同版本）
    /// </summary>
    /// <returns>合并后的歌曲列表</returns>
    public async Task<List<Song>> GetMergedSongsAsync()
    {
        var allSongs = await GetAllSongsAsync();
        var networkSongs = await _db.GetCachedNetworkSongsAsync();
        allSongs.AddRange(networkSongs);

        var enabledProtocols = await _db.GetEnabledProtocolsAsync();
        allSongs = _db.FilterByEnabledProtocols(allSongs, enabledProtocols);

        return allSongs.OrderBy(s => s.Title).ToList();
    }

    /// <summary>
    /// 委托：获取去重合并后的歌曲总数
    /// </summary>
    /// <returns>歌曲数量</returns>
    public async Task<int> GetMergedSongCountAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetMergedDedupedCountAsync();
    }

    /// <summary>
    /// 委托：获取收藏歌曲数量
    /// </summary>
    /// <returns>收藏歌曲数量</returns>
    public async Task<int> GetFavoriteSongCountAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetFavoriteCountAsync();
    }

    /// <summary>
    /// 委托：获取最近播放歌曲数量
    /// </summary>
    /// <returns>最近播放歌曲数量</returns>
    public async Task<int> GetRecentSongCountAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetRecentPlayCountAsync();
    }

    /// <summary>
    /// 委托：获取"全部音乐"中第一首歌曲的 ID
    /// </summary>
    /// <returns>歌曲 ID，无歌曲时返回 0</returns>
    public async Task<int> GetFirstSongIdForAllAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetFirstSongIdForAllAsync();
    }

    /// <summary>
    /// 委托：获取最近收藏的第一首歌曲 ID
    /// </summary>
    /// <returns>歌曲 ID，无收藏时返回 0</returns>
    public async Task<int> GetFirstFavoriteSongIdAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetFirstFavoriteSongIdAsync();
    }

    /// <summary>
    /// 委托：获取最近一次播放的歌曲 ID
    /// </summary>
    /// <returns>歌曲 ID，无播放历史时返回 0</returns>
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
    public async Task<int> CreatePlaylistAsync(string name)
    {
        var id = await _db.CreatePlaylistAsync(name);
        RaisePlaylistsChanged();
        return id;
    }

    /// <summary>
    /// 委托：更新播放列表
    /// </summary>
    /// <param name="playlist">播放列表对象</param>
    public async Task UpdatePlaylistAsync(Playlist playlist)
    {
        await _db.UpdatePlaylistAsync(playlist);
        RaisePlaylistsChanged();
    }

    /// <summary>
    /// 委托：删除播放列表
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    public async Task DeletePlaylistAsync(int playlistId)
    {
        await _db.DeletePlaylistAsync(playlistId);
        RaisePlaylistsChanged();
    }

    /// <summary>
    /// 委托：向播放列表添加歌曲
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="songId">歌曲 ID</param>
    public async Task AddSongToPlaylistAsync(int playlistId, int songId)
    {
        await _db.AddSongToPlaylistAsync(playlistId, songId);
        RaisePlaylistsChanged();
    }

    /// <summary>
    /// 委托：从播放列表移除歌曲
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="songId">歌曲 ID</param>
    public async Task RemoveSongFromPlaylistAsync(int playlistId, int songId)
    {
        await _db.RemoveSongFromPlaylistAsync(playlistId, songId);
        RaisePlaylistsChanged();
    }

    /// <summary>
    /// 委托：从播放列表批量移除歌曲
    /// </summary>
    /// <param name="playlistId">播放列表 ID</param>
    /// <param name="songIds">歌曲 ID 集合</param>
    public async Task RemoveSongsFromPlaylistAsync(int playlistId, IEnumerable<int> songIds)
    {
        await _db.RemoveSongsFromPlaylistAsync(playlistId, songIds);
        RaisePlaylistsChanged();
    }

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
