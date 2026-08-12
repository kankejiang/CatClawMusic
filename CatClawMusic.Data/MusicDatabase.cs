using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using SQLite;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Data;

/// <summary>持久化迁移标记，记录已完成的一次性维护任务，避免每次启动重复执行</summary>
[Table("MigrationFlag")]
internal class MigrationFlag
{
    /// <summary>迁移任务名称（主键）</summary>
    [PrimaryKey] public string Name { get; set; } = "";

    /// <summary>迁移任务状态值，"done" 表示已完成</summary>
    public string Value { get; set; } = "";
}

/// <summary>
/// SQLite 数据库操作层，管理歌曲、艺术家、专辑、播放列表、收藏等数据的持久化。
/// 主体文件：字段、构造、初始化与维护任务调度；其余功能域见同目录
/// MusicDatabase.Songs / Artists / PlayHistory / FavoritesAndLyrics /
/// ProfilesAndPlaylists / NetworkCache / Migrations / Chat 的 partial 文件。
/// </summary>
public partial class MusicDatabase
{
    /// <summary>
    /// 安全的 ToDictionary：遇到重复键时保留第一个，避免异常
    /// </summary>
    private static Dictionary<TKey, TValue> SafeToDict<TSource, TKey, TValue>(
        IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TValue> valueSelector)
        where TKey : notnull
    {
        var dict = new Dictionary<TKey, TValue>();
        foreach (var item in source)
        {
            var key = keySelector(item);
            if (!dict.ContainsKey(key))
                dict[key] = valueSelector(item);
        }
        return dict;
    }
    /// <summary>
    /// SQLite 异步数据库连接
    /// </summary>
    private readonly SQLiteAsyncConnection _database;

    /// <summary>
    /// 数据库是否已完成初始化
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// 从文件路径提取艺术家名称的回调（由 UI 层设置，用于修复 ArtistId=0 的歌曲）
    /// </summary>
    public Func<string, string?>? ExtractArtistNameCallback { get; set; }

    /// <summary>
    /// 初始化信号量，确保并发安全
    /// </summary>
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);

    /// <summary>
    /// 后台维护任务信号量，确保维护任务与依赖维护完成的查询串行
    /// </summary>
    private readonly SemaphoreSlim _maintenanceSemaphore = new(1, 1);

    /// <summary>
    /// 播放记录信号量，确保并发的 RecordPlayAsync 串行执行，避免竞态下同一 SongId 被插入多条记录
    /// </summary>
    private readonly SemaphoreSlim _playHistoryLock = new(1, 1);

    /// <summary>
    /// 后台维护任务（拆分合并艺术家、修复专辑关联），在基础初始化完成后启动
    /// </summary>
    private Task? _maintenanceTask;

    /// <summary>
    /// 后台维护是否已完成
    /// </summary>
    private bool _maintenanceCompleted;

    /// <summary>
    /// 使用指定的数据库路径创建 MusicDatabase 实例
    /// </summary>
    public MusicDatabase(string dbPath)
    {
        _database = new SQLiteAsyncConnection(dbPath);
    }

    /// <summary>
    /// 确保数据库表已创建并完成必要迁移，多次调用安全。
    /// 合并艺术家拆分、专辑关联修复等耗时维护任务在后台执行，不阻塞启动。
    /// </summary>
    public async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;

        await _initSemaphore.WaitAsync();
        try
        {
            if (_isInitialized) return;

            await _database.EnableWriteAheadLoggingAsync();

            await _database.CreateTableAsync<Artist>();
            await _database.CreateTableAsync<Album>();
            await _database.CreateTableAsync<Song>();
            await _database.CreateTableAsync<Playlist>();
            await _database.CreateTableAsync<PlaylistSong>();

            // PlayHistory 迁移必须在 CreateTableAsync 之前，否则旧表缺少主键列会导致 "Cannot add a PRIMARY KEY column"
            await MigratePlayHistoryTableAsync();
            await _database.CreateTableAsync<PlayHistory>();
            await _database.CreateTableAsync<PlaySession>();
            await _database.CreateTableAsync<Favorite>();
            await _database.CreateTableAsync<Lyric>();
            await _database.CreateTableAsync<ConnectionProfile>();
            await _database.CreateTableAsync<CachedSong>();
            await _database.CreateTableAsync<SongArtist>();
            await _database.CreateTableAsync<MigrationFlag>();
            await _database.CreateTableAsync<ChatMessageRecord>();

            await CreateIndexesAsync();

            await MigratePlaylistsTableAsync();
            await MigratePlaylistSongsTableAsync();
            await MigrateArtistsTableAsync();
            await RecoverArtistsTableAsync();

            // 迁移现有单艺术家数据到多对多 SongArtists 表
            await MigrateToMultiArtistAsync();

            _isInitialized = true;

            // 耗时维护任务放到后台，避免阻塞启动页跳转
            _maintenanceTask = Task.Run(RunMaintenanceAsync);
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    /// <summary>
    /// 等待后台维护任务完成。读取/写入歌曲、艺术家、专辑相关数据前调用，确保数据修复已完成。
    /// </summary>
    public async Task EnsureMaintenanceCompletedAsync()
    {
        await EnsureInitializedAsync();
        var task = _maintenanceTask;
        if (task != null)
            await task;
    }

    /// <summary>
    /// 后台维护：执行耗时的历史数据修复任务（已完成的任务通过持久化标记跳过）
    /// </summary>
    private async Task RunMaintenanceAsync()
    {
        await _maintenanceSemaphore.WaitAsync();
        try
        {
            if (_maintenanceCompleted) return;

            // 将历史合并艺术家名（如 "国风堂/哦漏"）拆分为独立艺术家
            if (!await IsMigrationDoneAsync("split_combined_artists"))
            {
                await SplitCombinedArtistsAsync();
                await MarkMigrationDoneAsync("split_combined_artists");
            }

            // 修复早期版本中 ArtistId=0 导致 AlbumId 关联错误的问题
            if (!await IsMigrationDoneAsync("repair_album_associations"))
            {
                await RepairAlbumAssociationsAsync();
                await MarkMigrationDoneAsync("repair_album_associations");
            }

            // 合并 PlayHistory 中同一 SongId 的多条重复记录（历史竞态写入导致），只执行一次
            if (!await IsMigrationDoneAsync("consolidate_play_history"))
            {
                await ConsolidatePlayHistoryAsync();
                await MarkMigrationDoneAsync("consolidate_play_history");
            }

            _maintenanceCompleted = true;
        }
        finally
        {
            _maintenanceSemaphore.Release();
        }
    }

    /// <summary>检查指定迁移是否已完成（持久化标记）</summary>
    private async Task<bool> IsMigrationDoneAsync(string name)
    {
        try
        {
            var flag = await _database.Table<MigrationFlag>()
                .Where(f => f.Name == name).FirstOrDefaultAsync();
            return flag != null && flag.Value == "done";
        }
        catch { return false; }
    }

    /// <summary>标记指定迁移为已完成</summary>
    private async Task MarkMigrationDoneAsync(string name)
    {
        try
        {
            await _database.InsertOrReplaceAsync(new MigrationFlag { Name = name, Value = "done" });
        }
        catch { /* 标记写入失败不影响功能，下次启动会重试 */ }
    }

    /// <summary>
    /// 创建数据库查询索引以提升搜索和关联查询性能
    /// </summary>
    private async Task CreateIndexesAsync()
    {
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_artist ON Songs(ArtistId)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_album ON Songs(AlbumId)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_title ON Songs(Title)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_source ON Songs(Source)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_protocol ON Songs(Protocol)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_source_protocol ON Songs(Source, Protocol)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_filepath ON Songs(FilePath)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_remoteid ON Songs(RemoteId)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_albums_artist ON Albums(ArtistId)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_play_history_time ON PlayHistory(PlayedAt DESC)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_song_artists_song ON SongArtists(SongId)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_song_artists_artist ON SongArtists(ArtistId)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_chat_messages_id ON ChatMessageRecord(Id DESC)"); } catch { }
        try { await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_chat_messages_timestamp ON ChatMessageRecord(Timestamp)"); } catch { }
    }
}

