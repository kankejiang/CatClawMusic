using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

public class MusicScanner
{
    private readonly MusicDatabase _db;
    private readonly Action<List<Song>>? _batchCallback;
    private readonly List<Song> _pending = new();
    private int _totalInserted;
    private const string DefaultArtist = "未知艺术家";
    private const string DefaultAlbum = "未知专辑";

    private readonly Dictionary<string, int> _artistCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string album, int artistId), int> _albumCache = new();
    private readonly HashSet<string> _batchFilePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _batchRemoteIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _cacheLoaded;

    public MusicScanner(MusicDatabase db, Action<List<Song>>? batchCallback = null)
    {
        _db = db;
        _batchCallback = batchCallback;
    }

    public int TotalInserted => _totalInserted;

    private int GetBatchSize()
    {
        if (_totalInserted < 10) return 1;
        if (_totalInserted < 100) return 20;
        return 100;
    }

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
