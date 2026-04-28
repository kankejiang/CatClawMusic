using System;
using CatClawMusic.Core.Models;
using SQLite;

namespace CatClawMusic.Data;

/// <summary>
/// 音乐数据库上下文（SQLite）
/// </summary>
public class MusicDatabase
{
    private readonly SQLiteAsyncConnection _database;
    private bool _isInitialized = false;
    private readonly object _initLock = new();
    
    public MusicDatabase(string dbPath)
    {
        _database = new SQLiteAsyncConnection(dbPath);
    }
    
    /// <summary>
    /// 确保数据库已初始化（线程安全）
    /// </summary>
    public async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;
        
        lock (_initLock)
        {
            if (_isInitialized) return;
        }
        
        // 创建表
        await _database.CreateTableAsync<Song>();
        await _database.CreateTableAsync<Album>();
        await _database.CreateTableAsync<Playlist>();
        await _database.CreateTableAsync<PlaylistSong>();
        await _database.CreateTableAsync<PlaybackStats>();
        await _database.CreateTableAsync<RecentPlay>();
        await _database.CreateTableAsync<CachedSong>();
        await _database.CreateTableAsync<ConnectionProfile>();
        
        // 创建索引
        await CreateIndexesAsync();
        
        _isInitialized = true;
    }
    
    private async Task CreateIndexesAsync()
    {
        try
        {
            await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_artist ON Songs(Artist);");
            await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_album ON Songs(Album);");
            await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_lastplayed ON RecentPlays(PlayedAt DESC);");
            await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_playcount ON PlaybackStats(PlayCount DESC);");
        }
        catch
        {
            // 索引已存在，忽略
        }
    }
    
    // 歌曲表
    public SQLiteAsyncConnection Songs => _database;
    
    // 获取所有歌曲
    public async Task<List<Song>> GetSongsAsync()
    {
        await EnsureInitializedAsync();
        return await _database.Table<Song>().ToListAsync();
    }
    
    // 保存歌曲（插入或更新）
    public async Task<int> SaveSongAsync(Song song)
    {
        await EnsureInitializedAsync();
        if (song.Id != 0)
        {
            return await _database.UpdateAsync(song);
        }
        else
        {
            return await _database.InsertAsync(song);
        }
    }
    
    // 删除歌曲
    public async Task<int> DeleteSongAsync(Song song)
    {
        await EnsureInitializedAsync();
        return await _database.DeleteAsync(song);
    }

    // 保存连接配置
    public async Task<int> SaveConnectionProfileAsync(ConnectionProfile profile)
    {
        await EnsureInitializedAsync();
        if (profile.Id != 0)
            return await _database.UpdateAsync(profile);
        return await _database.InsertAsync(profile);
    }

    // 获取连接配置
    public async Task<ConnectionProfile?> GetConnectionProfileAsync()
    {
        await EnsureInitializedAsync();
        return await _database.Table<ConnectionProfile>()
            .Where(p => p.IsEnabled)
            .FirstOrDefaultAsync();
    }

    // 获取所有连接配置
    public async Task<List<ConnectionProfile>> GetConnectionProfilesAsync()
    {
        await EnsureInitializedAsync();
        return await _database.Table<ConnectionProfile>().ToListAsync();
    }

    // 保存播放列表
    public async Task<int> SavePlaylistAsync(Playlist playlist)
    {
        await EnsureInitializedAsync();
        if (playlist.Id != 0)
            return await _database.UpdateAsync(playlist);
        return await _database.InsertAsync(playlist);
    }

    // 获取所有播放列表
    public async Task<List<Playlist>> GetPlaylistsAsync()
    {
        await EnsureInitializedAsync();
        return await _database.Table<Playlist>().ToListAsync();
    }
}

/// <summary>
/// 播放列表-歌曲关联表
/// </summary>
public class PlaylistSong
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    public int PlaylistId { get; set; }
    public int SongId { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// 播放统计表
/// </summary>
public class PlaybackStats
{
    [PrimaryKey]
    public int SongId { get; set; }
    
    public int PlayCount { get; set; } = 0;
    public long TotalPlayTime { get; set; } = 0;
    public int SkipCount { get; set; } = 0;
    public int CompletePlayCount { get; set; } = 0;
    public long LastPlayedAt { get; set; } = 0;
}

/// <summary>
/// 最近播放表
/// </summary>
public class RecentPlay
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    public int SongId { get; set; }
    public long PlayedAt { get; set; }
}

/// <summary>
/// 缓存歌曲表
/// </summary>
public class CachedSong
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    public int SongId { get; set; }
    public string CachePath { get; set; } = string.Empty;
    public long CachedAt { get; set; }
    public long LastAccessedAt { get; set; }
    public long FileSize { get; set; }
}
