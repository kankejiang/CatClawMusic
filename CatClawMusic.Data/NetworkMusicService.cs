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
    private readonly INetworkFileService _smb;

    /// <summary>
    /// 创建网络音乐服务实例
    /// </summary>
    /// <param name="db">数据库操作实例</param>
    /// <param name="subsonic">Subsonic/Navidrome API 客户端</param>
    /// <param name="webDav">WebDAV 文件服务</param>
    /// <param name="smb">SMB 文件服务</param>
    public NetworkMusicService(MusicDatabase db, ISubsonicService subsonic, INetworkFileService webDav, INetworkFileService smb)
    {
        _db = db;
        _subsonic = subsonic;
        _webDav = webDav;
        _smb = smb;
    }

    /// <summary>
    /// 获取所有连接配置
    /// </summary>
    /// <returns>连接配置列表</returns>
    public async Task<List<ConnectionProfile>> GetProfilesAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetConnectionProfilesAsync();
    }

    /// <summary>
    /// 扫描网络音乐源，按协议类型分发到 Subsonic 或 WebDAV/SMB 扫描
    /// </summary>
    /// <param name="profile">连接配置</param>
    /// <param name="progress">进度报告回调</param>
    /// <param name="songBatchCallback">每批次歌曲扫描完成后的回调</param>
    /// <returns>扫描到的所有歌曲列表</returns>
    public async Task<List<Song>> ScanAsync(ConnectionProfile profile,
        IProgress<(int done, int total, string status)>? progress = null,
        Action<List<Song>>? songBatchCallback = null)
    {
        try { await _db.EnsureInitializedAsync(); } catch { }

        var scannedRemoteIds = new HashSet<string>();
        var allSongs = new List<Song>();

        if (profile.Protocol == ProtocolType.Navidrome)
        {
            var scanner = new MusicScanner(_db, songBatchCallback);
            allSongs = await _subsonic.GetSongsAsync(profile, progress, async (batch) =>
            {
                try
                {
                    foreach (var s in batch)
                    {
                        if (!string.IsNullOrEmpty(s.RemoteId)) scannedRemoteIds.Add(s.RemoteId);
                        await scanner.AddSongAsync(s);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CatClaw] 增量入库失败: {ex.Message}");
                }
                songBatchCallback?.Invoke(batch);
            });
            await scanner.FlushAsync();
        }
        else if (profile.Protocol == ProtocolType.WebDAV)
        {
            var (newSongs, allFoundIds) = await ScanWebDavAsync(profile, songBatchCallback);
            allSongs = newSongs;
            foreach (var id in allFoundIds)
            {
                if (!string.IsNullOrEmpty(id)) scannedRemoteIds.Add(id);
            }
        }
        else if (profile.Protocol == ProtocolType.SMB)
        {
            var (newSongs, allFoundIds) = await ScanSmbAsync(profile, songBatchCallback);
            allSongs = newSongs;
            foreach (var id in allFoundIds)
            {
                if (!string.IsNullOrEmpty(id)) scannedRemoteIds.Add(id);
            }
        }

        try
        {
            var source = profile.Protocol == ProtocolType.SMB ? SongSource.SMB : SongSource.WebDAV;
            var removed = await _db.RemoveStaleSongsAsync(source, new HashSet<string>(), scannedRemoteIds);
            if (removed > 0)
                System.Diagnostics.Debug.WriteLine($"[CatClaw] 清理 {removed} 首已移除的网络歌曲 ({source})");
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CatClaw] 清理旧网络歌曲失败: {ex.Message}"); }

        return allSongs;
    }

    /// <summary>
    /// 按协议类型搜索网络音乐（当前仅支持 Navidrome）
    /// </summary>
    /// <param name="keyword">搜索关键词</param>
    /// <param name="profile">连接配置</param>
    /// <returns>匹配的歌曲列表</returns>
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

    private async Task<MemoryStream?> DownloadSmbHeadAsync(string remotePath, ConnectionProfile profile)
    {
        _smb.Configure(profile);
        var head = await _smb.OpenReadRangeAsync(remotePath, 0, TagHeadSize);
        if (head.Length > 0)
            return new MemoryStream(head);

        try
        {
            using var stream = await _smb.OpenReadAsync(remotePath);
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
    /// <param name="songId">歌曲 ID 或文件路径</param>
    /// <param name="profile">连接配置</param>
    /// <returns>封面图流，失败时返回 null</returns>
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
        if (profile.Protocol == ProtocolType.SMB)
        {
            try
            {
                var ms = await DownloadSmbHeadAsync(songId, profile);
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
                System.Diagnostics.Debug.WriteLine($"[CatClaw] SMB 封面提取失败: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>
    /// 获取远程歌曲歌词，优先查找外部 .lrc 文件，回退到嵌入标签
    /// </summary>
    /// <param name="remotePath">远程文件路径</param>
    /// <param name="profile">连接配置</param>
    /// <returns>歌词文本，失败时返回 null</returns>
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
        if (profile.Protocol == ProtocolType.SMB)
        {
            _smb.Configure(profile);
            var lastDot = remotePath.LastIndexOf('.');
            if (lastDot > 0)
            {
                var lrcPath = remotePath.Substring(0, lastDot) + ".lrc";
                try
                {
                    using var lrcStream = await _smb.OpenReadAsync(lrcPath);
                    using var reader = new StreamReader(lrcStream);
                    var lrcText = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(lrcText))
                        return lrcText;
                }
                catch { }
            }
            try
            {
                var ms = await DownloadSmbHeadAsync(remotePath, profile);
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
                System.Diagnostics.Debug.WriteLine($"[CatClaw] SMB 歌词提取失败: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>
    /// 从远程文件头部数据中解析歌曲元数据，更新歌曲信息
    /// </summary>
    /// <param name="song">待更新元数据的歌曲对象</param>
    /// <param name="profile">连接配置</param>
    /// <returns>更新后的歌曲对象，失败时返回 null</returns>
    public async Task<Song?> FetchSongMetadataAsync(Song song, ConnectionProfile profile)
    {
        if (profile.Protocol == ProtocolType.WebDAV)
        {
            var result = await FetchWebDavMetadataAsync(song, profile);
            if (result != null) return result;
        }
        if (profile.Protocol == ProtocolType.SMB)
        {
            var result = await FetchSmbMetadataAsync(song, profile);
            if (result != null) return result;
        }
        return null;
    }

    private async Task<Song?> FetchWebDavMetadataAsync(Song song, ConnectionProfile profile)
    {
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

    private async Task<Song?> FetchSmbMetadataAsync(Song song, ConnectionProfile profile)
    {
        var remotePath = song.RemoteId ?? song.CoverArtPath;
        if (string.IsNullOrEmpty(remotePath)) return null;

        _smb.Configure(profile);
        try
        {
            var ms = await DownloadSmbHeadAsync(remotePath, profile);
            if (ms != null)
            {
                try
                {
                    var tagSong = TagReader.ReadFromStream(ms, song.FilePath, remotePath, song.FileSize);
                    if (tagSong != null)
                    {
                        if (!string.IsNullOrWhiteSpace(tagSong.Title) && tagSong.Title != song.Title)
                            song.Title = tagSong.Title;
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
            System.Diagnostics.Debug.WriteLine($"[CatClaw] SMB 元数据获取失败: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 获取歌曲流 URL，按协议类型构建对应的播放地址
    /// </summary>
    /// <param name="song">歌曲对象</param>
    /// <param name="profile">连接配置</param>
    /// <returns>流播放 URL</returns>
    public Task<string> GetStreamUrlAsync(Song song, ConnectionProfile profile)
    {
        if (profile.Protocol == ProtocolType.Navidrome)
            return Task.FromResult(_subsonic.GetStreamUrl(song.RemoteId ?? song.FilePath, profile));
        if (profile.Protocol == ProtocolType.WebDAV)
            return Task.FromResult(BuildWebDavStreamUrl(song.RemoteId ?? song.FilePath, profile));
        if (profile.Protocol == ProtocolType.SMB)
            return Task.FromResult(BuildSmbStreamUrl(song.RemoteId ?? song.FilePath, profile));
        return Task.FromResult(song.FilePath);
    }

    private static string BuildSmbStreamUrl(string filePath, ConnectionProfile profile)
    {
        if (filePath.StartsWith("smb://")) return filePath;
        var host = profile.Host.Trim();
        var share = string.IsNullOrEmpty(profile.ShareName) ? "share" : profile.ShareName.Trim();
        var path = filePath.Replace('\\', '/').TrimStart('/');
        var auth = string.IsNullOrEmpty(profile.UserName) ? "" : $"{Uri.EscapeDataString(profile.UserName)}:{Uri.EscapeDataString(profile.Password)}@";
        return $"smb://{auth}{host}/{share}/{path}";
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
        // 清理主机地址
        var host = (profile.Host ?? "").TrimEnd('/');
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            host = host[7..];
        else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            host = host[8..];
        // 去掉端口（已经单独设置了 Profile.Port）
        var colonIdx = host.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(host[(colonIdx + 1)..], out _))
            host = host[..colonIdx];
        // 包含 Basic 认证信息的 URL（ExoPlayer 原生支持）
        var authUser = string.IsNullOrEmpty(profile.UserName) ? "" : Uri.EscapeDataString(profile.UserName);
        var authPass = string.IsNullOrEmpty(profile.Password) ? "" : Uri.EscapeDataString(profile.Password);
        var auth = string.IsNullOrEmpty(authUser) ? "" : $"{authUser}:{authPass}@";
        return $"{scheme}://{auth}{host}:{profile.Port}/{path}";
    }

    /// <summary>
    /// 递归扫描 WebDAV 目录，批量入库发现的音频文件
    /// </summary>
    private async Task<(List<Song> NewSongs, HashSet<string> AllFoundIds)> ScanWebDavAsync(
        ConnectionProfile profile, Action<List<Song>>? songBatchCallback)
    {
        var songs = new List<Song>();
        var basePath = profile.BasePath?.TrimEnd('/') ?? "/";
        if (string.IsNullOrEmpty(basePath)) basePath = "/";

        var connResult = await _webDav.TestConnectionAsync(profile);
        if (!connResult.Success)
        {
            return (songs, new HashSet<string>());
        }

        _webDav.Configure(profile);

        var foundIds = new HashSet<string>();
        var existingIds = new HashSet<string>();
        try
        {
            var existingSongs = await _db.GetCachedNetworkSongsAsync();
            existingIds = existingSongs
                .Where(s => s.Source == SongSource.WebDAV && !string.IsNullOrEmpty(s.RemoteId))
                .Select(s => s.RemoteId!)
                .ToHashSet();
        }
        catch { }

        var scanner = new MusicScanner(_db, songBatchCallback);
        var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await ScanWebDavDirectoryAsync(basePath, profile, songs, foundIds, existingIds, scanner, visitedDirs);
        await scanner.FlushAsync();

        return (songs, foundIds);
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
    /// 递归扫描 WebDAV 目录，按扩展名过滤音频文件，通过 MusicScanner 渐进式入库
    /// </summary>
    private async Task ScanWebDavDirectoryAsync(string path, ConnectionProfile profile, List<Song> songs,
        HashSet<string> foundIds, HashSet<string> existingIds, MusicScanner scanner, HashSet<string> visitedDirs, int depth = 0)
    {
        if (depth > MaxScanDepth)
        {
            return;
        }

        var normalizedDir = path.TrimEnd('/').TrimEnd('\\');
        if (string.IsNullOrEmpty(normalizedDir)) normalizedDir = "/";
        if (!visitedDirs.Add(normalizedDir))
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 跳过已访问目录: {path}");
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

        System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 目录 {path} 有 {files.Count} 个条目 (depth={depth})");

        foreach (var file in files)
        {
            if (file.IsDirectory)
            {
                await ScanWebDavDirectoryAsync(file.Path, profile, songs, foundIds, existingIds, scanner, visitedDirs, depth + 1);
            }
            else
            {
                var ext = System.IO.Path.GetExtension(file.Name)?.ToUpperInvariant() ?? "";
                if (string.IsNullOrEmpty(ext))
                    ext = System.IO.Path.GetExtension(file.Path)?.ToUpperInvariant() ?? "";
                if (!IsAudioExtension(ext))
                    continue;

                foundIds.Add(file.Path);

                if (existingIds.Contains(file.Path))
                    continue;

                var streamUrl = BuildWebDavStreamUrl(file.Path, profile);
                var title = System.IO.Path.GetFileNameWithoutExtension(file.Name) ?? file.Name;
                if (string.IsNullOrEmpty(title))
                    title = System.IO.Path.GetFileNameWithoutExtension(file.Path) ?? file.Path;
                var song = new Song
                {
                    Title = title,
                    Artist = "",
                    Album = "",
                    FilePath = streamUrl,
                    Duration = 0,
                    FileSize = file.Size,
                    DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Source = SongSource.WebDAV,
                    Protocol = ProtocolType.WebDAV,
                    RemoteId = file.Path,
                    CoverArtPath = file.Path
                };

                try
                {
                    var tagged = await FetchWebDavMetadataAsync(song, profile);
                    if (tagged == null)
                    {
                        song.Artist = "未知艺术家";
                        song.Album = "未知专辑";
                    }
                }
                catch
                {
                    song.Artist = "未知艺术家";
                    song.Album = "未知专辑";
                }

                songs.Add(song);
                await scanner.AddSongAsync(song);
            }
        }
    }

    private async Task<(List<Song> NewSongs, HashSet<string> AllFoundIds)> ScanSmbAsync(
        ConnectionProfile profile, Action<List<Song>>? songBatchCallback)
    {
        var songs = new List<Song>();
        var basePath = profile.BasePath?.TrimEnd('/', '\\') ?? "\\";
        if (string.IsNullOrEmpty(basePath) || basePath == "/") basePath = "\\";

        var connResult = await _smb.TestConnectionAsync(profile);
        if (!connResult.Success) return (songs, new HashSet<string>());

        _smb.Configure(profile);

        var foundIds = new HashSet<string>();
        var existingIds = new HashSet<string>();
        try
        {
            var existingSongs = await _db.GetCachedNetworkSongsAsync();
            existingIds = existingSongs
                .Where(s => s.Source == SongSource.SMB && !string.IsNullOrEmpty(s.RemoteId))
                .Select(s => s.RemoteId!)
                .ToHashSet();
        }
        catch { }

        var scanner = new MusicScanner(_db, songBatchCallback);
        var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await ScanSmbDirectoryAsync(basePath, profile, songs, foundIds, existingIds, scanner, visitedDirs);
        await scanner.FlushAsync();

        return (songs, foundIds);
    }

    private async Task ScanSmbDirectoryAsync(string path, ConnectionProfile profile, List<Song> songs,
        HashSet<string> foundIds, HashSet<string> existingIds, MusicScanner scanner, HashSet<string> visitedDirs, int depth = 0)
    {
        if (depth > MaxScanDepth) return;

        var normalizedDir = path.TrimEnd('/').TrimEnd('\\');
        if (string.IsNullOrEmpty(normalizedDir)) normalizedDir = "\\";
        if (!visitedDirs.Add(normalizedDir))
        {
            System.Diagnostics.Debug.WriteLine($"[SMB Scan] 跳过已访问目录: {path}");
            return;
        }

        List<RemoteFile> files;
        try
        {
            files = await _smb.ListFilesAsync(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SMB Scan] 列出 {path} 失败: {ex.Message}");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[SMB Scan] 目录 {path} 有 {files.Count} 个条目 (depth={depth})");

        foreach (var file in files)
        {
            if (file.IsDirectory)
            {
                await ScanSmbDirectoryAsync(file.Path, profile, songs, foundIds, existingIds, scanner, visitedDirs, depth + 1);
            }
            else
            {
                var ext = System.IO.Path.GetExtension(file.Name)?.ToUpperInvariant() ?? "";
                if (string.IsNullOrEmpty(ext))
                    ext = System.IO.Path.GetExtension(file.Path)?.ToUpperInvariant() ?? "";
                if (!IsAudioExtension(ext)) continue;

                foundIds.Add(file.Path);

                if (existingIds.Contains(file.Path)) continue;

                var streamUrl = BuildSmbStreamUrl(file.Path, profile);
                var title = System.IO.Path.GetFileNameWithoutExtension(file.Name) ?? file.Name;
                if (string.IsNullOrEmpty(title))
                    title = System.IO.Path.GetFileNameWithoutExtension(file.Path) ?? file.Path;
                var song = new Song
                {
                    Title = title,
                    Artist = "",
                    Album = "",
                    FilePath = streamUrl,
                    Duration = 0,
                    FileSize = file.Size,
                    DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Source = SongSource.SMB,
                    Protocol = ProtocolType.SMB,
                    RemoteId = file.Path,
                    CoverArtPath = file.Path
                };

                try
                {
                    var tagged = await FetchSmbMetadataAsync(song, profile);
                    if (tagged == null)
                    {
                        song.Artist = "未知艺术家";
                        song.Album = "未知专辑";
                    }
                }
                catch
                {
                    song.Artist = "未知艺术家";
                    song.Album = "未知专辑";
                }

                songs.Add(song);
                await scanner.AddSongAsync(song);
            }
        }
    }
}
