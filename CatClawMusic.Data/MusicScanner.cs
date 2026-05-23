using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

/// <summary>
/// 统一音乐扫描入库器——所有来源（本地/WebDAV/SMB/Navidrome）共用
/// 渐进式批量策略：1-10首逐条入库 → 11-100首每20条 → 100首以上每100条
/// </summary>
public class MusicScanner
{
    private readonly MusicDatabase _db;
    private readonly Action<List<Song>>? _batchCallback;
    private readonly List<Song> _pending = new();
    private int _totalInserted;
    private int _cachedDefaultArtistId = -1;
    private int _cachedDefaultAlbumId = -1;
    private const string DefaultArtist = "未知艺术家";
    private const string DefaultAlbum = "未知专辑";

    /// <summary>
    /// 创建音乐扫描器实例
    /// </summary>
    /// <param name="db">数据库操作实例</param>
    /// <param name="batchCallback">每批次入库后的回调，传递本批次入库的歌曲列表</param>
    public MusicScanner(MusicDatabase db, Action<List<Song>>? batchCallback = null)
    {
        _db = db;
        _batchCallback = batchCallback;
    }

    /// <summary>已入库歌曲总数（含本次扫描新增）</summary>
    public int TotalInserted => _totalInserted;

    /// <summary>当前待刷新批次大小</summary>
    private int GetBatchSize()
    {
        if (_totalInserted < 10) return 1;
        if (_totalInserted < 100) return 20;
        return 100;
    }

    /// <summary>
    /// 添加一首歌曲到待入库批次，达到阈值时自动刷新入库
    /// </summary>
    public async Task AddSongAsync(Song song)
    {
        _pending.Add(song);
        if (_pending.Count >= GetBatchSize())
            await FlushAsync();
    }

    /// <summary>
    /// 手动刷新待入库批次（扫描结束或需要立即入库时调用）
    /// </summary>
    public async Task FlushAsync()
    {
        if (_pending.Count == 0) return;

        var toInsert = _pending.ToList();
        _pending.Clear();

        if (_cachedDefaultArtistId < 0)
            _cachedDefaultArtistId = await _db.EnsureArtistAsync(DefaultArtist);
        if (_cachedDefaultAlbumId < 0)
            _cachedDefaultAlbumId = await _db.EnsureAlbumAsync(DefaultAlbum, _cachedDefaultArtistId);

        var inserted = new List<Song>();

        foreach (var s in toInsert)
        {
            try
            {
                if (!string.IsNullOrEmpty(s.Artist))
                    s.ArtistId = s.Artist == DefaultArtist ? _cachedDefaultArtistId
                        : await _db.EnsureArtistAsync(s.Artist);
                if (!string.IsNullOrEmpty(s.Album))
                    s.AlbumId = s.Album == DefaultAlbum ? _cachedDefaultAlbumId
                        : await _db.EnsureAlbumAsync(s.Album, s.ArtistId);
                s.DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await _db.InsertSongAsync(s);
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
}
