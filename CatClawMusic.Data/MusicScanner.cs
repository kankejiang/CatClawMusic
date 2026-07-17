using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Data;

/// <summary>
/// 音乐扫描器，负责将歌曲批量入库。
/// <para>
/// 核心设计要点：
/// <list type="bullet">
///   <item>动态批次大小：根据已插入数量自动调整每批入库的歌曲数，初期少量提交以便快速反馈，后期大批量提交以提升吞吐。</item>
///   <item>内存缓存：在首次刷写时从数据库加载全部艺术家和专辑到字典中，后续查重直接走内存，避免反复查询数据库。</item>
///   <item>批次内去重：使用 HashSet 在当前批次内对文件路径和远程ID进行去重，防止同一首歌重复入库。</item>
///   <item>回调通知：每批入库完成后通过回调将成功插入的歌曲列表通知给调用方。</item>
/// </list>
/// </para>
/// </summary>
public class MusicScanner
{
    /// <summary>
    /// 数据库访问实例，用于执行艺术家、专辑的确保（Ensure）操作以及歌曲插入。
    /// </summary>
    private readonly MusicDatabase _db;

    /// <summary>
    /// 批次完成后的回调函数，参数为本批次成功插入数据库的歌曲列表。
    /// 可为 null，表示无需回调通知。
    /// </summary>
    private readonly Action<List<Song>>? _batchCallback;

    /// <summary>
    /// 当前待刷写的歌曲缓冲区。当待处理数量达到动态批次大小时触发自动刷写。
    /// </summary>
    private readonly List<Song> _pending = new();

    /// <summary>
    /// 累计成功插入数据库的歌曲总数，同时用于动态批次大小的计算依据。
    /// </summary>
    private int _totalInserted;

    /// <summary>
    /// 当歌曲缺少艺术家信息时使用的默认艺术家名称。
    /// </summary>
    private const string DefaultArtist = "未知艺术家";

    /// <summary>
    /// 当歌曲缺少专辑信息时使用的默认专辑名称。
    /// </summary>
    private const string DefaultAlbum = "未知专辑";

    /// <summary>
    /// 艺术家名称到数据库ID的内存缓存。键为艺术家名称（忽略大小写），值为数据库中的主键ID。
    /// 首次刷写时从数据库一次性加载，后续新增艺术家也会同步更新此缓存。
    /// </summary>
    private readonly Dictionary<string, int> _artistCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 专辑到数据库ID的内存缓存。键为（专辑名称, 艺术家ID）元组，值为数据库中的主键ID。
    /// 使用元组作为键是因为同一专辑名可能属于不同艺术家。
    /// </summary>
    private readonly Dictionary<(string album, int artistId), int> _albumCache = new();

    /// <summary>
    /// 当前批次内已处理的文件路径集合，用于去重。忽略大小写比较。
    /// 每次刷写后清空。
    /// </summary>
    private readonly HashSet<string> _batchFilePaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 当前批次内已处理的远程ID集合，用于去重。忽略大小写比较。
    /// </summary>
    private readonly HashSet<string> _batchRemoteIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 同步锁，保护待处理缓冲区与批次去重集合的并发访问。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 串行化 FlushBatchAsync 的信号量，确保同一时间只有一个批次在写入数据库，
    /// 避免 SQLite 写锁竞争导致超时和重试。
    /// </summary>
    private readonly SemaphoreSlim _flushLock = new(1, 1);

    /// <summary>
    /// 标记内存缓存是否已从数据库加载。首次执行 FlushAsync 时进行懒加载，
    /// 避免在没有任何歌曲需要入库时浪费数据库查询。
    /// </summary>
    private bool _cacheLoaded;

    /// <summary>
    /// 初始化音乐扫描器。
    /// </summary>
    /// <param name="db">数据库访问实例，不可为 null。</param>
    /// <param name="batchCallback">批次完成后的可选回调，参数为成功插入的歌曲列表。</param>
    public MusicScanner(MusicDatabase db, Action<List<Song>>? batchCallback = null)
    {
        _db = db;
        _batchCallback = batchCallback;
    }

    /// <summary>
    /// 获取累计成功插入数据库的歌曲总数。
    /// </summary>
    public int TotalInserted => _totalInserted;

    /// <summary>
    /// 根据已插入数量动态计算当前批次大小。
    /// <para>
    /// 策略说明：
    /// <list type="bullet">
    ///   <item>已插入 &lt; 100：批次大小为 200，平衡内存与吞吐。</item>
    ///   <item>已插入 &lt; 500：批次大小为 500，提升批量入库效率。</item>
    ///   <item>已插入 ≥ 500：批次大小为 1000，最大化批量入库效率。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <returns>当前应使用的批次大小。</returns>
    private int GetBatchSize()
    {
        if (_totalInserted < 100) return 200;
        if (_totalInserted < 500) return 500;
        return 1000;
    }

    /// <summary>
    /// 批量添加歌曲到待处理缓冲区，比逐条调用 AddSongAsync 更高效。
    /// </summary>
    public async Task AddSongsBatchAsync(IEnumerable<Song> songs)
    {
        Song[]? flushBatch = null;
        lock (_lock)
        {
            foreach (var song in songs)
            {
                if (!string.IsNullOrEmpty(song.FilePath) && !_batchFilePaths.Add(song.FilePath))
                    continue;
                if (!string.IsNullOrEmpty(song.RemoteId) && !_batchRemoteIds.Add(song.RemoteId))
                    continue;

                _pending.Add(song);
            }

            if (_pending.Count >= GetBatchSize())
            {
                flushBatch = _pending.ToArray();
                _pending.Clear();
                _batchFilePaths.Clear();
                _batchRemoteIds.Clear();
            }
        }
        if (flushBatch != null)
            await FlushBatchAsync(flushBatch);
    }

    /// <summary>
    /// 添加一首歌曲到待处理缓冲区。
    /// <para>
    /// 处理流程：
    /// <list type="number">
    ///   <item>若歌曲有文件路径，检查当前批次是否已存在相同路径，存在则跳过（去重）。</item>
    ///   <item>若歌曲有远程ID，检查当前批次是否已存在相同远程ID，存在则跳过（去重）。</item>
    ///   <item>将歌曲加入待处理列表。</item>
    ///   <item>若待处理数量达到动态批次大小，自动触发刷写。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="song">待添加的歌曲对象。</param>
    public async Task AddSongAsync(Song song)
    {
        Song[]? flushBatch = null;
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(song.FilePath) && !_batchFilePaths.Add(song.FilePath))
                return;
            if (!string.IsNullOrEmpty(song.RemoteId) && !_batchRemoteIds.Add(song.RemoteId))
                return;

            _pending.Add(song);
            if (_pending.Count >= GetBatchSize())
            {
                flushBatch = _pending.ToArray();
                _pending.Clear();
                _batchFilePaths.Clear();
                _batchRemoteIds.Clear();
            }
        }
        if (flushBatch != null)
            await FlushBatchAsync(flushBatch);
    }

    /// <summary>
    /// 将待处理缓冲区中的所有歌曲批量写入数据库。
    /// <para>
    /// 处理流程：
    /// <list type="number">
    ///   <item>若缓冲区为空则直接返回。</item>
    ///   <item>首次刷写时从数据库加载艺术家和专辑缓存（懒加载）。</item>
    ///   <item>确保默认艺术家和默认专辑存在于数据库中。</item>
    ///   <item>遍历待处理歌曲，依次确保艺术家和专辑存在、设置添加时间戳、插入歌曲记录。</item>
    ///   <item>统计成功插入数量并触发回调通知。</item>
    /// </list>
    /// </para>
    /// </summary>
    public async Task FlushAsync()
    {
        Song[] batch;
        lock (_lock)
        {
            if (_pending.Count == 0) return;
            batch = _pending.ToArray();
            _pending.Clear();
            _batchFilePaths.Clear();
            _batchRemoteIds.Clear();
        }
        await FlushBatchAsync(batch);
    }

    /// <summary>
    /// 将一批歌曲批量写入数据库的内部实现。
    /// <para>
    /// 处理流程：
    /// <list type="number">
    ///   <item>懒加载艺术家/专辑内存缓存（仅首次）。</item>
    ///   <item>确保默认艺术家和默认专辑存在。</item>
    ///   <item>拆分多艺术家名称（如 "周杰伦/林俊杰"），收集全部艺术家名并批量入库。</item>
    ///   <item>设置每首歌的 ArtistId，再批量确保专辑存在并设置 AlbumId。</item>
    ///   <item>批量插入歌曲记录。</item>
    ///   <item>为多艺术家歌曲创建 SongArtist 多对多关联。</item>
    ///   <item>更新累计计数并触发批次回调。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="toInsert">待批量插入的歌曲数组。</param>
    private async Task FlushBatchAsync(Song[] toInsert)
    {
        if (toInsert.Length == 0) return;

        // 串行化数据库写入：多个并发 Task.Run 可能同时达到批次大小触发 FlushBatchAsync，
        // SQLite 写锁竞争会导致超时重试，严重拖慢入库速度。
        await _flushLock.WaitAsync();
        try
        {

        var flushSw = System.Diagnostics.Stopwatch.StartNew();

        if (!_cacheLoaded)
        {
            var cacheSw = System.Diagnostics.Stopwatch.StartNew();
            await LoadCachesAsync();
            _cacheLoaded = true;
            cacheSw.Stop();
            Log.Debug("MusicScanner", $"[CatClaw] 加载艺术家/专辑缓存：耗时 {cacheSw.ElapsedMilliseconds}ms");
        }

        if (!_artistCache.TryGetValue(DefaultArtist, out var defaultArtistId))
        {
            defaultArtistId = await _db.EnsureArtistAsync(DefaultArtist);
            _artistCache[DefaultArtist] = defaultArtistId;
        }
        if (!_albumCache.TryGetValue((DefaultAlbum, defaultArtistId), out var defaultAlbumId))
        {
            defaultAlbumId = await _db.EnsureAlbumAsync(DefaultAlbum, defaultArtistId);
            _albumCache[(DefaultAlbum, defaultArtistId)] = defaultAlbumId;
        }

        // 预处理：批量确保所有艺术家和专辑存在，减少逐条数据库查询
        //
        // 第一步：对每首歌的 Artist 字段进行拆分（处理 "周杰伦/林俊杰" 等多值情况），
        // 取第一个艺术家名作为该 Song 的主要艺术家，同时收集所有拆分出的名字以确保入库。
        var allArtistNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var songAllArtistNames = new Dictionary<Song, List<string>>(); // 保留每首歌的完整艺术家列表
        foreach (var s in toInsert)
        {
            if (string.IsNullOrEmpty(s.Artist))
                continue;

            var names = MusicUtility.SplitArtistNames(s.Artist);
            if (names.Count == 0)
                continue;

            // 保存完整艺术家列表，稍后用于创建 SongArtist 多对多关联
            songAllArtistNames[s] = names;

            // 第一个艺术家名作为该 Song 的主要艺术家
            s.Artist = names[0];

            // 收集所有拆分出的艺术家名用于后续确保入库
            foreach (var name in names)
                allArtistNames.Add(name);
        }

        // 第二步：批量确保所有艺术家存在（包含主艺术家和通过拆分得到的次要艺术家）
        var artistMap = await _db.EnsureArtistsBatchAsync(allArtistNames.ToList());
        foreach (var kvp in artistMap)
        {
            if (!_artistCache.ContainsKey(kvp.Key))
                _artistCache[kvp.Key] = kvp.Value;
        }

        // 第三步：先设置 ArtistId，再计算 albumKeys（否则 ArtistId=0 会导致专辑关联错误）
        foreach (var s in toInsert)
        {
            if (!string.IsNullOrEmpty(s.Artist) && _artistCache.TryGetValue(s.Artist, out var aid))
                s.ArtistId = aid;
            else
                s.ArtistId = defaultArtistId;
        }

        // 第四步：批量确保专辑存在（此时 ArtistId 已正确设置）
        var albumKeys = toInsert
            .Where(s => !string.IsNullOrEmpty(s.Album))
            .Select(s => (s.Album!, s.ArtistId))
            .Distinct()
            .ToList();

        var albumMap = await _db.EnsureAlbumsBatchAsync(albumKeys);
        foreach (var kvp in albumMap)
        {
            if (!_albumCache.ContainsKey(kvp.Key))
                _albumCache[kvp.Key] = kvp.Value;
        }

        // 第五步：设置 AlbumId 和 DateAdded
        foreach (var s in toInsert)
        {
            if (!string.IsNullOrEmpty(s.Album) && _albumCache.TryGetValue((s.Album, s.ArtistId), out var albId))
                s.AlbumId = albId;
            else
                s.AlbumId = defaultAlbumId;
            s.DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        // 批量插入：使用事务 + 内存去重，比逐条 InsertSongAsync 快 10 倍以上
        var inserted = await _db.InsertSongsBatchAsync(toInsert.ToList());

        // 第六步：为多艺术家歌曲创建 SongArtist 多对多关联
        if (songAllArtistNames.Count > 0)
        {
            var songArtistEntries = new List<(int SongId, List<int> ArtistIds)>();
            foreach (var kvp in songAllArtistNames)
            {
                var song = kvp.Key;
                var names = kvp.Value;

                if (song.Id <= 0) continue;

                var artistIds = names
                    .Select(name => _artistCache.TryGetValue(name, out var id) ? id : -1)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();

                if (artistIds.Count > 0)
                    songArtistEntries.Add((song.Id, artistIds));
            }

            if (songArtistEntries.Count > 0)
                await _db.SaveSongArtistsBatchAsync(songArtistEntries);
        }

        _totalInserted += inserted.Count;
        _batchCallback?.Invoke(inserted);

        flushSw.Stop();
        Log.Debug("MusicScanner", $"[CatClaw] 批次刷写：{toInsert.Length} 首，插入 {inserted.Count}，累计 {_totalInserted}，耗时 {flushSw.ElapsedMilliseconds}ms");
        }
        finally
        {
            _flushLock.Release();
        }
    }

    /// <summary>
    /// 从数据库加载全部艺术家和专辑到内存缓存中。
    /// <para>
    /// 此方法仅在首次刷写时被调用（懒加载模式），后续所有查重操作直接走内存缓存，
    /// 大幅减少数据库查询次数。加载失败时静默忽略，不影响后续流程
    /// （后续每个艺术家/专辑会通过 Ensure 方法逐个创建）。
    /// </para>
    /// </summary>
    private async Task LoadCachesAsync()
    {
        try
        {
            var artists = await _db.GetAllArtistsAsync();
            foreach (var a in artists)
                _artistCache[a.Name] = a.Id;

            var albums = await _db.GetAllAlbumsAsync();
            foreach (var a in albums)
                _albumCache[(a.Title, a.ArtistId)] = a.Id;
        }
        catch { }
    }
}
