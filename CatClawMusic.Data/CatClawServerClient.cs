using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

/// <summary>
/// CatClaw 服务端 HTTP 客户端 —— 从 NAS 服务端获取元数据并入库
/// </summary>
public class CatClawServerClient : ICatClawServerService
{
    /// <summary>HTTP 客户端，超时 30 秒</summary>
    private readonly HttpClient _http;
    /// <summary>数据库访问实例，用于将服务端元数据入库</summary>
    private readonly MusicDatabase _db;

    /// <summary>CatClaw 服务端基础 URL（如 http://nas.local:8080）</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>
    /// 初始化 CatClaw 服务端客户端。
    /// </summary>
    /// <param name="db">数据库访问实例。</param>
    public CatClawServerClient(MusicDatabase db)
    {
        _db = db;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// 测试与服务端的连接是否可用（GET /api/status）。
    /// </summary>
    /// <returns>连接成功返回 true，否则返回 false。</returns>
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

    /// <summary>
    /// 获取服务端状态信息（GET /api/status）。
    /// </summary>
    /// <returns>服务端状态对象；请求失败时返回 null。</returns>
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

    /// <summary>
    /// 从服务端同步全部歌曲元数据并入库。
    /// 已存在的文件路径会跳过（增量同步）。
    /// </summary>
    /// <param name="progress">进度回调，参数为 (状态消息, 百分比)。</param>
    /// <returns>本次新增入库的歌曲数量。</returns>
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

    /// <summary>
    /// 在服务端搜索歌曲（GET /api/search?q=...），返回匹配结果但不入库。
    /// </summary>
    /// <param name="keyword">搜索关键词。</param>
    /// <returns>匹配的歌曲列表；请求失败时返回空列表。</returns>
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

    /// <summary>
    /// 构建服务端歌曲流播放 URL（GET /api/stream/{songId}）。
    /// </summary>
    /// <param name="songId">服务端歌曲 ID。</param>
    /// <returns>流播放 URL。</returns>
    public string GetStreamUrl(int songId)
    {
        return $"{ServerUrl.TrimEnd('/')}/api/stream/{songId}";
    }

    /// <summary>JSON 反序列化选项：属性名大小写不敏感</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
