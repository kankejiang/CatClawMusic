using CatClawMusic.Core.Models;

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
    /// 每次刷写后清空。
    /// </summary>
    private readonly HashSet<string> _batchRemoteIds = new(StringComparer.OrdinalIgnoreCase);

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
    ///   <item>已插入 &lt; 10：批次大小为 1，逐条提交，便于初期快速反馈和调试。</item>
    ///   <item>已插入 &lt; 100：批次大小为 20，逐步提升吞吐量。</item>
    ///   <item>已插入 ≥ 100：批次大小为 100，最大化批量入库效率。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <returns>当前应使用的批次大小。</returns>
    private int GetBatchSize()
    {
        if (_totalInserted < 10) return 1;
        if (_totalInserted < 100) return 20;
        return 100;
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
        if (!string.IsNullOrEmpty(song.FilePath) && !_batchFilePaths.Add(song.FilePath))
            return;
        if (!string.IsNullOrEmpty(song.RemoteId) && !_batchRemoteIds.Add(song.RemoteId))
            return;

        _pending.Add(song);
        if (_pending.Count >= GetBatchSize())
            await FlushAsync();
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
        if (_pending.Count == 0) return;

        if (!_cacheLoaded)
        {
            await LoadCachesAsync();
            _cacheLoaded = true;
        }

        var toInsert = _pending.ToList();
        _pending.Clear();
        _batchFilePaths.Clear();
        _batchRemoteIds.Clear();

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

        var inserted = new List<Song>();

        foreach (var s in toInsert)
        {
            try
            {
                // 确保艺术家存在：优先查缓存，缓存未命中则调用数据库并更新缓存
                if (!string.IsNullOrEmpty(s.Artist))
                {
                    if (_artistCache.TryGetValue(s.Artist, out var aid))
                        s.ArtistId = aid;
                    else
                    {
                        s.ArtistId = await _db.EnsureArtistAsync(s.Artist);
                        _artistCache[s.Artist] = s.ArtistId;
                    }
                }

                // 确保专辑存在：优先查缓存，缓存未命中则调用数据库并更新缓存
                if (!string.IsNullOrEmpty(s.Album))
                {
                    var albumKey = (s.Album, s.ArtistId);
                    if (_albumCache.TryGetValue(albumKey, out var albId))
                        s.AlbumId = albId;
                    else
                    {
                        s.AlbumId = await _db.EnsureAlbumAsync(s.Album, s.ArtistId);
                        _albumCache[albumKey] = s.AlbumId;
                    }
                }

                // 设置入库时间戳为当前UTC时间的Unix秒数
                s.DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await _db.InsertSongAsync(s);

                // 插入成功（Id > 0 表示数据库已分配主键）则加入成功列表
                if (s.Id > 0)
                    inserted.Add(s);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] 入库失败: {s.Title} - {ex.Message}");
            }
        }

        _totalInserted += inserted.Count;
        _batchCallback?.Invoke(inserted);
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
