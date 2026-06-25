using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

/// <summary>
/// CatClaw 服务端 HTTP 客户端 —— 从 NAS 服务端获取元数据并入库
/// </summary>
public class CatClawServerClient : ICatClawServerService
{
    private readonly HttpClient _http;
    private readonly MusicDatabase _db;

    public string ServerUrl { get; set; } = "";

    public CatClawServerClient(MusicDatabase db)
    {
        _db = db;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<bool> TestConnectionAsync()
    {
        if (string.IsNullOrEmpty(ServerUrl))
            return false;

        try
        {
            var resp = await _http.GetAsync($"{ServerUrl.TrimEnd('/')}/api/status");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ServerStatus?> GetStatusAsync()
    {
        if (string.IsNullOrEmpty(ServerUrl))
            return null;

        try
        {
            var json = await _http.GetStringAsync($"{ServerUrl.TrimEnd('/')}/api/status");
            return JsonSerializer.Deserialize<ServerStatus>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<int> SyncMetadataAsync(IProgress<(string, int)>? progress = null)
    {
        if (string.IsNullOrEmpty(ServerUrl))
            return 0;

        progress?.Report(("连接服务端...", 5));
        var baseUrl = ServerUrl.TrimEnd('/');

        // Fetch full song list
        progress?.Report(("获取歌曲列表...", 10));
        var songsJson = await _http.GetStringAsync($"{baseUrl}/api/songs");
        var serverSongs = JsonSerializer.Deserialize<List<ServerSong>>(songsJson, JsonOptions) ?? new();

        if (serverSongs.Count == 0)
            return 0;

        progress?.Report(($"正在入库 {serverSongs.Count} 首歌曲...", 20));

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var inserted = 0;
        var batchSize = 50;

        // Pre-fetch existing songs for dedup
        var existingPaths = await _db.GetLocalSongPathModTimesAsync();

        for (int i = 0; i < serverSongs.Count; i += batchSize)
        {
            var batch = serverSongs.Skip(i).Take(batchSize);

            foreach (var ss in batch)
            {
                // Skip if already exists with same path
                if (existingPaths.ContainsKey(ss.FilePath))
                    continue;

                var song = new Song
                {
                    Title = ss.Title,
                    Artist = ss.Artist,
                    Album = ss.Album,
                    Duration = ss.Duration,
                    FilePath = ss.FilePath,
                    FileSize = ss.FileSize,
                    Bitrate = ss.Bitrate,
                    TrackNumber = ss.TrackNumber,
                    Year = ss.Year,
                    Genre = ss.Genre ?? "",
                    DateModified = ss.DateModified,
                    DateAdded = now,
                    Source = SongSource.Cache,   // Mark as cached from server
                    Protocol = ProtocolType.WebDAV, // Reuse existing protocol
                    RemoteId = $"catclaw-server:{ss.Id}",
                    CoverArtPath = ss.CoverArtPath,
                    LyricsPath = ss.LyricsPath
                };

                try
                {
                    var artistId = await _db.EnsureArtistAsync(song.Artist);
                    song.ArtistId = artistId;

                    var albumId = await _db.EnsureAlbumAsync(song.Album, artistId);
                    song.AlbumId = albumId;

                    await _db.SaveSongAsync(song);
                    inserted++;
                }
                catch
                {
                    // Skip duplicates gracefully
                }
            }

            var pct = 20 + (int)((float)(i + batchSize) / serverSongs.Count * 70);
            progress?.Report(($"入库进度 {Math.Min(i + batchSize, serverSongs.Count)}/{serverSongs.Count}", Math.Min(pct, 90)));
        }

        progress?.Report(($"同步完成，新增 {inserted} 首歌曲", 100));
        return inserted;
    }

    public async Task<List<Song>> SearchServerAsync(string keyword)
    {
        if (string.IsNullOrEmpty(ServerUrl))
            return new();

        try
        {
            var url = $"{ServerUrl.TrimEnd('/')}/api/search?q={Uri.EscapeDataString(keyword)}";
            var json = await _http.GetStringAsync(url);
            var serverSongs = JsonSerializer.Deserialize<List<ServerSong>>(json, JsonOptions) ?? new();

            return serverSongs.Select(ss => new Song
            {
                Id = 0,
                Title = ss.Title,
                Artist = ss.Artist,
                Album = ss.Album,
                Duration = ss.Duration,
                FilePath = ss.FilePath,
                FileSize = ss.FileSize,
                Bitrate = ss.Bitrate,
                TrackNumber = ss.TrackNumber,
                Year = ss.Year,
                Genre = ss.Genre ?? "",
                ArtistId = (int)ss.ArtistId,
                AlbumId = (int)ss.AlbumId,
                Source = SongSource.Cache,
                Protocol = ProtocolType.WebDAV,
                RemoteId = $"catclaw-server:{ss.Id}",
                CoverArtPath = ss.CoverArtPath
            }).ToList();
        }
        catch
        {
            return new();
        }
    }

    public string GetStreamUrl(int songId)
    {
        return $"{ServerUrl.TrimEnd('/')}/api/stream/{songId}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
