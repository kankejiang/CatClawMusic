using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Data;

/// <summary>
/// 网络音乐服务——按协议类型分发
/// </summary>
public class NetworkMusicService : INetworkMusicService
{
    /// <summary>
    /// 数据库操作实例
    /// </summary>
    private readonly MusicDatabase _db;

    /// <summary>
    /// Subsonic/Navidrome API 客户端
    /// </summary>
    private readonly ISubsonicService _subsonic;

    /// <summary>
    /// WebDAV 文件服务
    /// </summary>
    private readonly INetworkFileService _webDav;

    /// <summary>
    /// 创建网络音乐服务实例
    /// </summary>
    public NetworkMusicService(MusicDatabase db, ISubsonicService subsonic, INetworkFileService webDav)
    {
        _db = db;
        _subsonic = subsonic;
        _webDav = webDav;
    }

    /// <summary>
    /// 获取所有连接配置
    /// </summary>
    public async Task<List<ConnectionProfile>> GetProfilesAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetConnectionProfilesAsync();
    }

    /// <summary>
    /// 扫描网络音乐源，按协议类型分发到 Subsonic 或 WebDAV 扫描
    /// </summary>
    public async Task<List<Song>> ScanAsync(ConnectionProfile profile,
        IProgress<(int done, int total, string status)>? progress = null,
        Action<List<Song>>? songBatchCallback = null)
    {
        // 先清除旧的网络缓存
        try
        {
            await _db.EnsureInitializedAsync();
            await _db.ReplaceNetworkSongsBeginAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] 清除旧网络歌曲失败: {ex.Message}");
        }

        // 增量式拉取：每个专辑完成后立即入库 + 回调通知 UI
        var allSongs = new List<Song>();

        if (profile.Protocol == ProtocolType.Navidrome)
        {
            allSongs = await _subsonic.GetSongsAsync(profile, progress, async (batch) =>
            {
                // 每个专辑的歌曲立即入库
                try
                {
                    foreach (var s in batch)
                    {
                        if (!string.IsNullOrEmpty(s.Artist))
                            s.ArtistId = await _db.EnsureArtistAsync(s.Artist);
                        if (!string.IsNullOrEmpty(s.Album))
                            s.AlbumId = await _db.EnsureAlbumAsync(s.Album, s.ArtistId);
                        s.DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        await _db.InsertSongAsync(s);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CatClaw] 增量入库失败: {ex.Message}");
                }
                // 通知 ViewModel 增量刷新列表
                songBatchCallback?.Invoke(batch);
            });
        }
        else if (profile.Protocol == ProtocolType.WebDAV)
        {
            allSongs = await ScanWebDavAsync(profile, songBatchCallback);
            // 入库已在 ScanWebDavAsync 内部按批次完成
        }

        System.Diagnostics.Debug.WriteLine($"[CatClaw] ScanAsync 总计 {allSongs.Count} 首网络歌曲");
        try { await _db.RestoreNetworkFavoritesAsync(); }
        catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"[CatClaw] 恢复收藏失败: {ex.Message}"); }
        return allSongs;
    }

    /// <summary>
    /// 按协议类型搜索网络音乐（当前仅支持 Navidrome）
    /// </summary>
    public async Task<List<Song>> SearchAsync(string keyword, ConnectionProfile profile)
    {
        return profile.Protocol switch
        {
            ProtocolType.Navidrome => await _subsonic.SearchAsync(keyword, profile),
            _ => new List<Song>()
        };
    }

    /// <summary>
    /// 下载文件头部数据用于读取标签信息的大小（512KB）
    /// </summary>
    private const int TagHeadSize = 512 * 1024;

    /// <summary>
    /// 下载远程文件的头部数据用于标签解析，失败时回退到完整下载
    /// </summary>
    private async Task<MemoryStream?> DownloadHeadAsync(string remotePath)
    {
        var head = await _webDav.OpenReadRangeAsync(remotePath, 0, TagHeadSize);
        if (head.Length > 0)
            return new MemoryStream(head);

        try
        {
            using var stream = await _webDav.OpenReadAsync(remotePath);
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            return ms;
        }
        catch { return null; }
    }

    /// <summary>
    /// 获取歌曲封面图流，按协议类型分发
    /// </summary>
    public async Task<Stream?> GetCoverAsync(string songId, ConnectionProfile profile)
    {
        if (profile.Protocol == ProtocolType.Navidrome)
        {
            var bytes = await _subsonic.GetCoverArtAsync(songId, profile);
            return bytes != null ? new MemoryStream(bytes) : null;
        }
        if (profile.Protocol == ProtocolType.WebDAV)
        {
            _webDav.Configure(profile);

            try
            {
                var ms = await DownloadHeadAsync(songId);
                if (ms != null)
                {
                    try
                    {
                        var coverBytes = TagReader.ExtractCoverFromStream(ms, songId);
                        if (coverBytes != null)
                            return new MemoryStream(coverBytes);
                    }
                    finally { ms.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] WebDAV 封面提取失败: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>
    /// 获取远程歌曲歌词，优先查找外部 .lrc 文件，回退到嵌入标签
    /// </summary>
    public async Task<string?> GetLyricsAsync(string remotePath, ConnectionProfile profile)
    {
        if (profile.Protocol == ProtocolType.WebDAV)
        {
            _webDav.Configure(profile);

            var lastDot = remotePath.LastIndexOf('.');
            if (lastDot > 0)
            {
                var lrcPath = remotePath.Substring(0, lastDot) + ".lrc";
                try
                {
                    using var lrcStream = await _webDav.OpenReadAsync(lrcPath);
                    using var reader = new StreamReader(lrcStream);
                    var lrcText = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(lrcText))
                        return lrcText;
                }
                catch { }
            }

            try
            {
                var ms = await DownloadHeadAsync(remotePath);
                if (ms != null)
                {
                    try
                    {
                        return TagReader.ReadEmbeddedLyricsFromStream(ms, remotePath);
                    }
                    finally { ms.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] WebDAV 歌词提取失败: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>
    /// 从远程文件头部数据中解析歌曲元数据，更新歌曲信息
    /// </summary>
    public async Task<Song?> FetchSongMetadataAsync(Song song, ConnectionProfile profile)
    {
        if (profile.Protocol != ProtocolType.WebDAV) return null;
        var remotePath = song.RemoteId ?? song.CoverArtPath;
        if (string.IsNullOrEmpty(remotePath)) return null;

        _webDav.Configure(profile);
        try
        {
            var ms = await DownloadHeadAsync(remotePath);
            if (ms != null)
            {
                try
                {
                    var decodedRemotePath = Uri.UnescapeDataString(remotePath);
                    var tagSong = TagReader.ReadFromStream(ms, song.FilePath, decodedRemotePath, song.FileSize);
                    if (tagSong != null)
                    {
                        if (!string.IsNullOrWhiteSpace(tagSong.Title) && tagSong.Title != song.Title)
                        {
                            var tagTitleDecoded = Uri.UnescapeDataString(tagSong.Title);
                            song.Title = tagTitleDecoded != song.Title ? tagTitleDecoded : song.Title;
                        }
                        song.Artist = !string.IsNullOrWhiteSpace(tagSong.Artist) && tagSong.Artist != "未知艺术家" ? tagSong.Artist : song.Artist;
                        song.Album = !string.IsNullOrWhiteSpace(tagSong.Album) && tagSong.Album != "未知专辑" ? tagSong.Album : song.Album;
                        song.Duration = tagSong.Duration > 0 ? tagSong.Duration : song.Duration;
                        song.Bitrate = tagSong.Bitrate > 0 ? tagSong.Bitrate : song.Bitrate;
                        song.Year = tagSong.Year > 0 ? tagSong.Year : song.Year;
                        song.TrackNumber = tagSong.TrackNumber > 0 ? tagSong.TrackNumber : song.TrackNumber;
                        song.Genre = tagSong.Genre;
                        return song;
                    }
                }
                finally { ms.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] WebDAV 元数据获取失败: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 获取歌曲流 URL，按协议类型构建对应的播放地址
    /// </summary>
    public Task<string> GetStreamUrlAsync(Song song, ConnectionProfile profile)
    {
        if (profile.Protocol == ProtocolType.Navidrome)
            return Task.FromResult(_subsonic.GetStreamUrl(song.FilePath, profile));
        if (profile.Protocol == ProtocolType.WebDAV)
            return Task.FromResult(BuildWebDavStreamUrl(song.RemoteId ?? song.FilePath, profile));
        return Task.FromResult(song.FilePath);
    }

    /// <summary>
    /// 构建包含认证信息的 WebDAV 流媒体 URL
    /// </summary>
    private static string BuildWebDavStreamUrl(string filePath, ConnectionProfile profile)
    {
        // 如果已经是完整 HTTP URL，直接返回
        if (filePath.StartsWith("http://") || filePath.StartsWith("https://"))
            return filePath;

        var scheme = profile.UseHttps ? "https" : "http";
        var path = filePath.TrimStart('/');
        // 包含 Basic 认证信息的 URL（ExoPlayer 原生支持）
        var authUser = string.IsNullOrEmpty(profile.UserName) ? "" : Uri.EscapeDataString(profile.UserName);
        var authPass = string.IsNullOrEmpty(profile.Password) ? "" : Uri.EscapeDataString(profile.Password);
        var auth = string.IsNullOrEmpty(authUser) ? "" : $"{authUser}:{authPass}@";
        return $"{scheme}://{auth}{profile.Host}:{profile.Port}/{path}";
    }

    /// <summary>
    /// 批量入库回调阈值
    /// </summary>
    private const int BatchSize = 10;

    /// <summary>
    /// 递归扫描 WebDAV 目录，批量入库发现的音频文件
    /// </summary>
    private async Task<List<Song>> ScanWebDavAsync(ConnectionProfile profile, Action<List<Song>>? songBatchCallback)
    {
        var songs = new List<Song>();
        var basePath = profile.BasePath?.TrimEnd('/') ?? "/";
        if (string.IsNullOrEmpty(basePath)) basePath = "/";

        // 先初始化 WebDAV 连接
        System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 初始化连接: {profile.Host}:{profile.Port}");
        var connResult = await _webDav.TestConnectionAsync(profile);
        if (!connResult.Success)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 连接失败: {connResult.Message}");
            return songs;
        }

        // 累积批次，满了就回调
        var batch = new List<Song>();

        System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 开始扫描: {basePath}");
        await ScanWebDavDirectoryAsync(basePath, profile, songs, batch, songBatchCallback);
        // 最后一批（不满 BatchSize 的残量）
        await FlushBatchAsync(batch, songBatchCallback);

        System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 发现 {songs.Count} 首歌曲");
        return songs;
    }

    /// <summary>
    /// 将批次中的歌曲批量入库并回调通知调用方
    /// </summary>
    private async Task FlushBatchAsync(List<Song> batch, Action<List<Song>>? songBatchCallback)
    {
        if (batch.Count == 0) return;

        var toAdd = batch.ToList();
        batch.Clear();

        var inserted = new List<Song>();

        foreach (var s in toAdd)
        {
            try
            {
                if (!string.IsNullOrEmpty(s.Artist))
                    s.ArtistId = await _db.EnsureArtistAsync(s.Artist);
                if (!string.IsNullOrEmpty(s.Album))
                    s.AlbumId = await _db.EnsureAlbumAsync(s.Album, s.ArtistId);
                s.DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await _db.InsertSongAsync(s);
                if (s.Id > 0)
                    inserted.Add(s);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] WebDAV 入库失败: {s.Title} - {ex.Message}");
            }
        }

        songBatchCallback?.Invoke(inserted);
    }

    /// <summary>
    /// WebDAV 目录扫描最大递归深度
    /// </summary>
    private const int MaxScanDepth = 20;

    /// <summary>
    /// 支持的音频文件扩展名集合
    /// </summary>
    private static readonly HashSet<string> AudioExtSet = new(
        new[] { ".MP3", ".WAV", ".FLAC", ".AAC", ".OGG", ".M4A", ".WMA", ".APE", ".AIFF", ".DSF" },
        StringComparer.Ordinal);

    /// <summary>
    /// 判断文件扩展名是否为支持的音频格式
    /// </summary>
    private static bool IsAudioExtension(string ext)
        => AudioExtSet.Contains(ext);

    /// <summary>
    /// 递归扫描 WebDAV 目录，按扩展名过滤音频文件，达到批量阈值后入库
    /// </summary>
    private async Task ScanWebDavDirectoryAsync(string path, ConnectionProfile profile, List<Song> songs, List<Song> batch,
        Action<List<Song>>? songBatchCallback, int depth = 0)
    {
        if (depth > MaxScanDepth)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 达到最大深度 {MaxScanDepth}，跳过: {path}");
            return;
        }

        List<RemoteFile> files;
        try
        {
            files = await _webDav.ListFilesAsync(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 列出 {path} 失败: {ex.Message}");
            return;
        }

        foreach (var file in files)
        {
            if (file.IsDirectory)
            {
                await ScanWebDavDirectoryAsync(file.Path, profile, songs, batch, songBatchCallback, depth + 1);
            }
            else
            {
                var ext = System.IO.Path.GetExtension(file.Name)?.ToUpperInvariant() ?? "";
                if (!IsAudioExtension(ext)) continue;

                var streamUrl = BuildWebDavStreamUrl(file.Path, profile);
                var title = System.IO.Path.GetFileNameWithoutExtension(file.Name) ?? file.Name;
                var song = new Song
                {
                    Title = title,
                    Artist = "未知艺术家",
                    Album = "未知专辑",
                    FilePath = streamUrl,
                    Duration = 0,
                    FileSize = file.Size,
                    DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Source = SongSource.WebDAV,
                    Protocol = ProtocolType.WebDAV,
                    RemoteId = file.Path,
                    CoverArtPath = file.Path
                };

                songs.Add(song);
                batch.Add(song);

                if (batch.Count >= BatchSize)
                    await FlushBatchAsync(batch, songBatchCallback);
            }
        }
    }
}
